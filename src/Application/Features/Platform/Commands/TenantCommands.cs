using Elysian.Application.Exceptions;
using Elysian.Application.Features.Placements.Models;
using Elysian.Application.Features.SiteContent;
using Elysian.Application.Features.UserManagement;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Platforms;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Settings;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elysian.Application.Features.Platform.Commands
{
    /// <param name="OwnerInviteCode">Give this to the site's owner. Shown once; only its hash is kept.</param>
    /// <param name="TemplateKey">The shared template a cloud site is drawn with; null for a site with its own code</param>
    /// <param name="Address">The address the site answers on from the start, e.g. friend.example-platform.com; null when it has none yet</param>
    public record CreatedTenant(string Identifier, string Platform, string Runtime, IReadOnlyList<string> Modules,
        string OwnerInviteCode, DateTimeOffset OwnerInviteExpiresAt, string? TemplateKey = null, int? TemplateVersion = null,
        string? Address = null)
    {
        // Keeps the code out of request logging
        public override string ToString() => $"{nameof(CreatedTenant)} {{ Identifier = {Identifier} }}";
    }

    /// <summary>
    /// Everything a new site needs to exist, in one step with nothing to do by hand afterwards: the tenant, its
    /// merchant record, the modules its platform includes, and an invite for its owner. Today a platform admin
    /// sends this; a public sign-up would put payment in front of the same command.
    ///
    /// A cloud site is pinned to a shared template: the one asked for, or its platform's default, at the
    /// newest version this host has. A site with its own code has none.
    ///
    /// A cloud site also gets its address under its platform's domain (<see cref="PlatformDomainSettings"/>),
    /// answering and primary, so it can be visited as soon as it exists.
    /// </summary>
    /// <param name="Template">Key of the template for a cloud site. Left out, the platform's default.</param>
    [RequiresPlatformAdmin]
    public record CreateTenantCommand(string? Identifier, string? Name, string? Platform, string? Runtime, string? OwnerEmail = null,
        string? Template = null) : IRequest<CreatedTenant>;

    public class CreateTenantCommandValidator : AbstractValidator<CreateTenantCommand>
    {
        public CreateTenantCommandValidator()
        {
            RuleFor(v => v.Identifier)
                .Must(i => TenantIdentifiers.IsWellFormed(TenantIdentifiers.Normalize(i)))
                .WithMessage("Use 3 to 63 lowercase letters, digits and dashes, starting and ending with a letter or digit.")
                .Must(i => !TenantIdentifiers.IsReserved(TenantIdentifiers.Normalize(i)))
                .WithMessage("That name is reserved. Please choose another.");

            RuleFor(v => v.Name)
                .NotEmpty().WithMessage("Please enter the site's name.")
                .MaximumLength(256).WithMessage("The name must be 256 characters or fewer.");

            RuleFor(v => v.Platform)
                .Must(p => PlatformCatalog.Find(p) != null)
                .WithMessage("Choose one of the platforms.");

            RuleFor(v => v.Runtime)
                .Must(r => Runtime(r) != null).WithMessage("Choose cloud or custom.")
                .Must((command, r) => Runtime(r) != RuntimeNames.Cloud || PlatformCatalog.Find(command.Platform) is not { HasCloudRuntime: false })
                .WithMessage("That platform has no shared template, so its sites are custom.");

            When(v => !string.IsNullOrWhiteSpace(v.OwnerEmail), () =>
                RuleFor(v => v.OwnerEmail)
                    .MaximumLength(320).WithMessage("Email must be 320 characters or fewer.")
                    .Must(e => UserManagementService.LooksLikeEmail(e)).WithMessage("Please enter a valid email address."));
        }

        public static string? Runtime(string? runtime) =>
            new[] { RuntimeNames.Cloud, RuntimeNames.Custom }.FirstOrDefault(r => string.Equals(r, runtime?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public class CreateTenantCommandHandler(PlatformContext platform, IClaimsPrincipalAccessor claimsPrincipalAccessor,
        TimeProvider timeProvider, IOptions<AuthSettings> options, SiteTemplateCatalog? templates = null,
        IPlacementPublishQueue? publishQueue = null, ILogger<CreateTenantCommandHandler>? logger = null,
        IOptions<PlatformDomainSettings>? domains = null, IEdgeHostMap? hostMap = null)
        : IRequestHandler<CreateTenantCommand, CreatedTenant>
    {
        public const string PublishReason = "site-created";

        public async Task<CreatedTenant> Handle(CreateTenantCommand request, CancellationToken cancellationToken)
        {
            var identifier = TenantIdentifiers.Normalize(request.Identifier);
            var definition = PlatformCatalog.Find(request.Platform)!;
            var runtime = CreateTenantCommandValidator.Runtime(request.Runtime)!;
            var name = request.Name!.Trim();
            var actor = claimsPrincipalAccessor.UserId ?? "system";
            var now = timeProvider.GetUtcNow();

            if (await platform.Tenants.AnyAsync(t => t.Identifier == identifier, cancellationToken)
                || await platform.Merchants.IgnoreQueryFilters().AnyAsync(m => m.MerchantIdentifier == identifier, cancellationToken))
            {
                throw new ConflictException($"A site named {identifier} already exists.");
            }

            var merchantType = await platform.MerchantTypes.FirstOrDefaultAsync(t => t.Name == definition.MerchantType, cancellationToken)
                ?? throw new InvalidOperationException($"The {definition.MerchantType} merchant type is missing, so a {definition.Name} site can't be created.");

            SiteTemplate? template = null;
            var templateKey = string.IsNullOrWhiteSpace(request.Template) ? definition.DefaultTemplate : request.Template.Trim().ToLowerInvariant();
            if (runtime == RuntimeNames.Cloud)
            {
                // Without a template a cloud site could never be drawn, so it is not created half made
                template = templates?.Latest(templateKey)
                    ?? throw new CustomValidationException([new FluentValidation.Results.ValidationFailure(nameof(request.Template),
                        $"This host has no template named {templateKey ?? "(none)"}.")]);
            }
            else if (!string.IsNullOrWhiteSpace(request.Template))
            {
                throw new CustomValidationException([new FluentValidation.Results.ValidationFailure(nameof(request.Template),
                    "Only a cloud site has a template. A custom site has its own code.")]);
            }

            var tenant = new ElysianTenantInfo
            {
                Id = Guid.NewGuid().ToString(),
                Identifier = identifier,
                Name = name,
                Platform = definition.Key,
                Runtime = runtime,
                Status = TenantStatusNames.Active,
                TemplateKey = template?.Key,
                TemplateVersion = template?.Version,
            };
            platform.Tenants.Add(tenant);

            platform.Merchants.Add(PlatformContext.Stamp(new Merchant
            {
                MerchantIdentifier = identifier,
                Name = name,
                Description = name,
                WebsiteUrl = string.Empty,
                MerchantTypeId = merchantType.MerchantTypeId,
            }, actor, now));

            foreach (var module in definition.IncludedModules)
            {
                platform.TenantModules.Add(PlatformContext.Stamp(new TenantModule
                {
                    TenantId = tenant.Id,
                    ModuleKey = module,
                    Status = TenantModuleStatus.Active,
                }, actor, now));
            }

            var code = InviteCodes.Generate();
            var invite = PlatformContext.Stamp(new TenantInvite
            {
                TenantId = tenant.Id,
                CodeHash = InviteCodes.Hash(code),
                Role = RoleNames.Owner,
                Email = string.IsNullOrWhiteSpace(request.OwnerEmail) ? null : UserManagementService.NormalizeEmail(request.OwnerEmail),
                ExpiresAt = now.AddDays(Math.Max(1, options.Value.InviteLifetimeDays)),
            }, actor, now);
            platform.TenantInvites.Add(invite);

            // Its own name under the platform's domain. The name is the site's alone, so the address is free
            // unless somebody gave it to another site by hand; then this site is made without one.
            var address = runtime == RuntimeNames.Cloud ? domains?.Value.AddressFor(definition.Key, identifier) : null;
            if (address != null && await platform.TenantDomains.AnyAsync(d => d.Host == address, cancellationToken))
            {
                logger?.LogWarning("{Address} already belongs to another site, so {Tenant} was created without an address", address, identifier);
                address = null;
            }
            if (address != null)
            {
                platform.TenantDomains.Add(PlatformContext.Stamp(new TenantDomain
                {
                    TenantId = tenant.Id,
                    Host = address,
                    Kind = TenantDomainKinds.Subdomain,
                    Status = TenantDomainStatuses.Active,
                    IsPrimary = true,
                }, actor, now));
            }

            // One save, one transaction: a site either exists completely or not at all
            await platform.SaveChangesAsync(cancellationToken);

            // Tell the edge, so the address answers. The site exists whether or not this works: saving the
            // address again from the admin app writes it again.
            if (address != null && hostMap != null)
            {
                try
                {
                    await hostMap.SyncAsync(identifier, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger?.LogError(ex, "Created {Tenant}, but could not tell the edge about {Address}", identifier, address);
                }
            }

            // A template's pages point at fixed image addresses from the first visit, so those are written
            // straight away: with no photos yet each one gets its plain frame, and none is ever missing. The
            // site exists whether or not this is queued; a publish that didn't happen shows in the admin app
            // as photos not yet published, with a button to try again.
            if (template?.ManifestJson != null && publishQueue != null)
            {
                try
                {
                    await publishQueue.SendAsync(new PlacementPublishMessage(identifier, PublishReason, Force: true), cancellationToken: cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger?.LogError(ex, "Created {Tenant}, but could not queue the first publish of its images", identifier);
                }
            }

            return new CreatedTenant(identifier, definition.Key, runtime, definition.IncludedModules, code, invite.ExpiresAt,
                template?.Key, template?.Version, address);
        }
    }

    /// <param name="PrimaryHost">The address the site is known by, or null when it has none recorded</param>
    /// <param name="ContentVersion">The published version of a cloud site's content, or null when it has never published</param>
    public record TenantSummary(string Identifier, string? Name, string? Platform, string? Runtime, string? Status,
        string? TemplateKey, int? TemplateVersion, int? ContentVersion, string? PrimaryHost, IReadOnlyList<string> Modules);

    /// <summary>
    /// Every site on the platform, by identifier
    /// </summary>
    [RequiresPlatformAdmin]
    public record GetTenantsQuery : IRequest<List<TenantSummary>>;

    public class GetTenantsQueryHandler(PlatformContext platform) : IRequestHandler<GetTenantsQuery, List<TenantSummary>>
    {
        public async Task<List<TenantSummary>> Handle(GetTenantsQuery request, CancellationToken cancellationToken)
        {
            var tenants = await platform.Tenants.AsNoTracking().ToListAsync(cancellationToken);
            var primaryHosts = (await platform.TenantDomains.AsNoTracking().Where(d => d.IsPrimary).ToListAsync(cancellationToken))
                .ToDictionary(d => d.TenantId, d => d.Host);
            var modules = (await platform.TenantModules.AsNoTracking()
                    .Where(m => m.Status != TenantModuleStatus.Suspended)
                    .ToListAsync(cancellationToken))
                .ToLookup(m => m.TenantId, m => m.ModuleKey);

            return [.. tenants
                .OrderBy(t => t.Identifier, StringComparer.Ordinal)
                .Select(t => new TenantSummary(t.Identifier!, t.Name, t.Platform, t.Runtime, t.Status,
                    t.TemplateKey, t.TemplateVersion, t.ContentVersion,
                    primaryHosts.GetValueOrDefault(t.Id!),
                    [.. modules[t.Id!].Order(StringComparer.Ordinal)]))];
        }
    }

    /// <summary>
    /// Suspends a site, or brings it back. A suspended site's scheduled jobs stop and the edge is refused on
    /// its behalf; its data is untouched.
    /// </summary>
    [RequiresPlatformAdmin]
    public record SetTenantStatusCommand(string? Identifier, string? Status) : IRequest;

    public class SetTenantStatusCommandValidator : AbstractValidator<SetTenantStatusCommand>
    {
        public SetTenantStatusCommandValidator()
        {
            RuleFor(v => v.Identifier).NotEmpty().WithMessage("Choose a site.");
            RuleFor(v => v.Status)
                .Must(s => Status(s) != null)
                .WithMessage("Choose trial, active or suspended.");
        }

        public static string? Status(string? status) =>
            new[] { TenantStatusNames.Trial, TenantStatusNames.Active, TenantStatusNames.Suspended }
                .FirstOrDefault(s => string.Equals(s, status?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public class SetTenantStatusCommandHandler(PlatformContext platform, IEdgeHostMap? hostMap = null) : IRequestHandler<SetTenantStatusCommand>
    {
        public async Task Handle(SetTenantStatusCommand request, CancellationToken cancellationToken)
        {
            var identifier = TenantIdentifiers.Normalize(request.Identifier);
            var tenant = await platform.Tenants.FirstOrDefaultAsync(t => t.Identifier == identifier, cancellationToken)
                ?? throw new NotFoundException("Site", identifier);

            tenant.Status = SetTenantStatusCommandValidator.Status(request.Status)!;
            await platform.SaveChangesAsync(cancellationToken);

            // The Worker that serves cloud sites reads a site's status from the edge
            if (hostMap != null)
            {
                await hostMap.SyncAsync(tenant.Identifier!, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Switches a module on or off for a site: an add-on bought or cancelled. Only modules the site's platform
    /// offers can be switched on. Hosts may go on serving the old answer from memory for up to Modules:CacheSeconds.
    /// </summary>
    [RequiresPlatformAdmin]
    public record SetTenantModuleCommand(string? Identifier, string? Module, bool Enabled, string? BillingReference = null) : IRequest;

    public class SetTenantModuleCommandHandler(PlatformContext platform, IClaimsPrincipalAccessor claimsPrincipalAccessor, TimeProvider timeProvider,
        IEdgeHostMap? edgeHostMap = null) : IRequestHandler<SetTenantModuleCommand>
    {
        /// <summary>
        /// A cloud site's pages follow its modules, and the edge is what draws them
        /// </summary>
        private Task SyncEdgeAsync(string identifier, CancellationToken cancellationToken) =>
            edgeHostMap?.SyncAsync(identifier, cancellationToken) ?? Task.CompletedTask;

        public async Task Handle(SetTenantModuleCommand request, CancellationToken cancellationToken)
        {
            var identifier = TenantIdentifiers.Normalize(request.Identifier);
            var tenant = await platform.Tenants.FirstOrDefaultAsync(t => t.Identifier == identifier, cancellationToken)
                ?? throw new NotFoundException("Site", identifier);

            var module = ModuleNames.All.FirstOrDefault(m => string.Equals(m, request.Module?.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new NotFoundException("Module", request.Module ?? string.Empty);

            var row = await platform.TenantModules.FirstOrDefaultAsync(m => m.TenantId == tenant.Id && m.ModuleKey == module, cancellationToken);
            var actor = claimsPrincipalAccessor.UserId ?? "system";
            var now = timeProvider.GetUtcNow();

            if (!request.Enabled)
            {
                if (row != null)
                {
                    row.Status = TenantModuleStatus.Suspended;
                    row.ModifiedByUserId = actor;
                    row.ModifiedAt = now;
                    await platform.SaveChangesAsync(cancellationToken);
                    await SyncEdgeAsync(identifier, cancellationToken);
                }
                return;
            }

            // A tenant from before platforms existed has none set; it keeps whatever it's given
            if (PlatformCatalog.Find(tenant.Platform) is { } definition && !definition.Offers(module))
            {
                throw new ConflictException($"{definition.Name} sites can't have the {module} module.");
            }

            if (row == null)
            {
                row = PlatformContext.Stamp(new TenantModule { TenantId = tenant.Id!, ModuleKey = module }, actor, now);
                platform.TenantModules.Add(row);
            }

            row.Status = TenantModuleStatus.Active;
            row.BillingReference = request.BillingReference ?? row.BillingReference;
            row.ModifiedByUserId = actor;
            row.ModifiedAt = now;
            await platform.SaveChangesAsync(cancellationToken);
            await SyncEdgeAsync(identifier, cancellationToken);
        }
    }
}
