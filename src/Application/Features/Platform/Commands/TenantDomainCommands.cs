using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Platforms;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Platform.Commands
{
    public record TenantDomainModel(string Host, string Kind, string Status, bool IsPrimary);

    /// <summary>
    /// The addresses a site answers at, its primary one first
    /// </summary>
    [RequiresPlatformAdmin]
    public record GetTenantDomainsQuery(string? Identifier) : IRequest<List<TenantDomainModel>>;

    public class GetTenantDomainsQueryHandler(PlatformContext platform) : IRequestHandler<GetTenantDomainsQuery, List<TenantDomainModel>>
    {
        public async Task<List<TenantDomainModel>> Handle(GetTenantDomainsQuery request, CancellationToken cancellationToken)
        {
            var tenant = await TenantDomains.FindTenantAsync(platform, request.Identifier, cancellationToken);
            var domains = await platform.TenantDomains.AsNoTracking()
                .Where(d => d.TenantId == tenant.Id)
                .ToListAsync(cancellationToken);

            return [.. domains
                .OrderByDescending(d => d.IsPrimary).ThenBy(d => d.Host, StringComparer.Ordinal)
                .Select(TenantDomains.Model)];
        }
    }

    /// <summary>
    /// Records an address for a site and, for a site drawn with a shared template, tells the edge about it.
    /// Pointing DNS at the site and attaching the domain to a Worker are separate steps, done by whoever
    /// calls this. A site's first address becomes its primary one.
    /// </summary>
    /// <param name="Kind">"subdomain" (the site's own name under a platform's domain) or "custom"</param>
    /// <param name="Status">"pending" or "active". Left out, the address is pending.</param>
    [RequiresPlatformAdmin]
    public record AddTenantDomainCommand(string? Identifier, string? Host, string? Kind, string? Status = null, bool MakePrimary = false)
        : IRequest<TenantDomainModel>;

    public class AddTenantDomainCommandValidator : AbstractValidator<AddTenantDomainCommand>
    {
        public AddTenantDomainCommandValidator()
        {
            RuleFor(v => v.Host)
                .Must(h => TenantHosts.IsWellFormed(TenantHosts.Normalize(h)))
                .WithMessage("Enter the address on its own, like www.example.com, with no https://, port or path.");

            RuleFor(v => v.Kind)
                .Must(k => TenantDomainKinds.Find(k) != null)
                .WithMessage("Choose subdomain or custom.");

            RuleFor(v => v.Host)
                .Must((command, h) => TenantHosts.IsSubdomainOf(TenantHosts.Normalize(h), TenantIdentifiers.Normalize(command.Identifier)))
                .When(v => TenantDomainKinds.Find(v.Kind) == TenantDomainKinds.Subdomain && TenantHosts.IsWellFormed(TenantHosts.Normalize(v.Host)))
                .WithMessage("A subdomain address starts with the site's own name, like friend.example.com for the site friend.");

            RuleFor(v => v.Status)
                .Must(s => TenantDomainStatuses.Find(s) != null)
                .When(v => !string.IsNullOrWhiteSpace(v.Status))
                .WithMessage("Choose pending or active.");
        }
    }

    public class AddTenantDomainCommandHandler(PlatformContext platform, IClaimsPrincipalAccessor claimsPrincipalAccessor, TimeProvider timeProvider,
        IEdgeHostMap? hostMap = null) : IRequestHandler<AddTenantDomainCommand, TenantDomainModel>
    {
        public async Task<TenantDomainModel> Handle(AddTenantDomainCommand request, CancellationToken cancellationToken)
        {
            var tenant = await TenantDomains.FindTenantAsync(platform, request.Identifier, cancellationToken);
            var host = TenantHosts.Normalize(request.Host);
            var actor = claimsPrincipalAccessor.UserId ?? "system";
            var now = timeProvider.GetUtcNow();

            // The database's unique index is the real guard; this turns the common case into a clear answer
            var taken = await platform.TenantDomains.FirstOrDefaultAsync(d => d.Host == host, cancellationToken);
            if (taken != null)
            {
                throw new ConflictException(taken.TenantId == tenant.Id
                    ? $"{host} is already one of this site's addresses."
                    : $"{host} already belongs to another site.");
            }

            var existing = await platform.TenantDomains.Where(d => d.TenantId == tenant.Id).ToListAsync(cancellationToken);
            var primary = request.MakePrimary || existing.Count == 0;
            if (primary)
            {
                TenantDomains.ClearPrimary(existing, actor, now);
            }

            var domain = PlatformContext.Stamp(new TenantDomain
            {
                TenantId = tenant.Id!,
                Host = host,
                Kind = TenantDomainKinds.Find(request.Kind)!,
                Status = TenantDomainStatuses.Find(request.Status) ?? TenantDomainStatuses.Pending,
                IsPrimary = primary,
            }, actor, now);
            platform.TenantDomains.Add(domain);

            await platform.SaveChangesAsync(cancellationToken);
            await TenantDomains.SyncAsync(hostMap, tenant, cancellationToken);
            return TenantDomains.Model(domain);
        }
    }

    /// <summary>
    /// Changes one of a site's addresses: whether it is answering yet, and whether it is the primary one. Making
    /// an address primary takes that from whichever had it.
    /// </summary>
    [RequiresPlatformAdmin]
    public record UpdateTenantDomainCommand(string? Identifier, string? Host, string? Status = null, bool? MakePrimary = null)
        : IRequest<TenantDomainModel>;

    public class UpdateTenantDomainCommandValidator : AbstractValidator<UpdateTenantDomainCommand>
    {
        public UpdateTenantDomainCommandValidator()
        {
            RuleFor(v => v.Status)
                .Must(s => TenantDomainStatuses.Find(s) != null)
                .When(v => !string.IsNullOrWhiteSpace(v.Status))
                .WithMessage("Choose pending or active.");

            RuleFor(v => v.MakePrimary)
                .Must(p => p != false)
                .WithMessage("To change the primary address, make another address primary.");
        }
    }

    public class UpdateTenantDomainCommandHandler(PlatformContext platform, IClaimsPrincipalAccessor claimsPrincipalAccessor, TimeProvider timeProvider,
        IEdgeHostMap? hostMap = null) : IRequestHandler<UpdateTenantDomainCommand, TenantDomainModel>
    {
        public async Task<TenantDomainModel> Handle(UpdateTenantDomainCommand request, CancellationToken cancellationToken)
        {
            var tenant = await TenantDomains.FindTenantAsync(platform, request.Identifier, cancellationToken);
            var host = TenantHosts.Normalize(request.Host);
            var actor = claimsPrincipalAccessor.UserId ?? "system";
            var now = timeProvider.GetUtcNow();

            var domains = await platform.TenantDomains.Where(d => d.TenantId == tenant.Id).ToListAsync(cancellationToken);
            var domain = domains.FirstOrDefault(d => d.Host == host) ?? throw new NotFoundException("Address", host);

            if (TenantDomainStatuses.Find(request.Status) is { } status)
            {
                domain.Status = status;
            }
            if (request.MakePrimary == true && !domain.IsPrimary)
            {
                TenantDomains.ClearPrimary(domains, actor, now);
                // Two rows can't both be primary even for a moment, so the old one is released first
                await platform.SaveChangesAsync(cancellationToken);
                domain.IsPrimary = true;
            }
            domain.ModifiedByUserId = actor;
            domain.ModifiedAt = now;

            await platform.SaveChangesAsync(cancellationToken);
            // Also the way to repair the edge's copy: updating an address with nothing changed rewrites it
            await TenantDomains.SyncAsync(hostMap, tenant, cancellationToken);
            return TenantDomains.Model(domain);
        }
    }

    /// <summary>
    /// Takes an address away from a site. The primary address can only go when it is the last one, so a site
    /// with addresses always has one the platform can use.
    /// </summary>
    [RequiresPlatformAdmin]
    public record RemoveTenantDomainCommand(string? Identifier, string? Host) : IRequest;

    public class RemoveTenantDomainCommandHandler(PlatformContext platform, IClaimsPrincipalAccessor claimsPrincipalAccessor, TimeProvider timeProvider,
        IEdgeHostMap? hostMap = null) : IRequestHandler<RemoveTenantDomainCommand>
    {
        public async Task Handle(RemoveTenantDomainCommand request, CancellationToken cancellationToken)
        {
            var tenant = await TenantDomains.FindTenantAsync(platform, request.Identifier, cancellationToken);
            var host = TenantHosts.Normalize(request.Host);

            var domains = await platform.TenantDomains.Where(d => d.TenantId == tenant.Id).ToListAsync(cancellationToken);
            var domain = domains.FirstOrDefault(d => d.Host == host) ?? throw new NotFoundException("Address", host);

            if (domain.IsPrimary && domains.Count > 1)
            {
                throw new ConflictException("Make another address primary before removing this one.");
            }

            // The edge first: if this fails the record is still there and the removal can be tried again.
            // The other way round would leave an address answering with no record of it.
            if (hostMap != null && tenant.Runtime == RuntimeNames.Cloud)
            {
                await hostMap.RemoveAsync(domain.Host, cancellationToken);
            }

            domain.IsDeleted = true;
            domain.IsPrimary = false;
            domain.ModifiedByUserId = claimsPrincipalAccessor.UserId ?? "system";
            domain.ModifiedAt = timeProvider.GetUtcNow();
            await platform.SaveChangesAsync(cancellationToken);
        }
    }

    internal static class TenantDomains
    {
        public static TenantDomainModel Model(TenantDomain domain) => new(domain.Host, domain.Kind, domain.Status, domain.IsPrimary);

        public static async Task<ElysianTenantInfo> FindTenantAsync(PlatformContext platform, string? identifier, CancellationToken cancellationToken)
        {
            var normalized = TenantIdentifiers.Normalize(identifier);
            return await platform.Tenants.FirstOrDefaultAsync(t => t.Identifier == normalized, cancellationToken)
                ?? throw new NotFoundException("Site", normalized);
        }

        /// <summary>
        /// Rewrites the edge's entries for a site's addresses from what is now recorded. The host map itself
        /// leaves out sites with their own code and hosts with no edge store.
        /// </summary>
        public static Task SyncAsync(IEdgeHostMap? hostMap, ElysianTenantInfo tenant, CancellationToken cancellationToken) =>
            hostMap == null ? Task.CompletedTask : hostMap.SyncAsync(tenant.Identifier!, cancellationToken);

        public static void ClearPrimary(IEnumerable<TenantDomain> domains, string actor, DateTimeOffset now)
        {
            foreach (var domain in domains.Where(d => d.IsPrimary))
            {
                domain.IsPrimary = false;
                domain.ModifiedByUserId = actor;
                domain.ModifiedAt = now;
            }
        }
    }
}
