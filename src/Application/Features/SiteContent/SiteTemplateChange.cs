using System.Text.Json;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Placements;
using Elysian.Application.Features.Placements.Models;
using Elysian.Application.Features.Sites;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Finbuckle.MultiTenant.Abstractions;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Elysian.Application.Features.SiteContent
{
    /// <summary>A template a site could be drawn with</summary>
    /// <param name="Current">The one the site is on now</param>
    public sealed record SiteTemplateOption(string Key, int Version, string Name, string? Description, bool Current);

    /// <summary>A change of template that has been asked for and is not finished yet</summary>
    public sealed record SiteTemplateChangeModel(string TemplateKey, int TemplateVersion, string TemplateName, DateTimeOffset RequestedAt);

    /// <summary>
    /// Moves a site from one shared template to another. Write first, switch second: the new template's reserved
    /// images are rendered to its own crops, under its own file names, while the old look goes on serving. Only
    /// when every one of them exists is the site pointed at the new template.
    ///
    /// What the owner wrote carries over, because templates share their field keys, and so do the photos picked
    /// for each spot. Colours and fonts belong to a template, so those fall back to the new template's own
    /// where it has no palette or font by the same name.
    ///
    /// The request is kept in the tenant's settings (section "TemplateChange") and carried by the queue message
    /// that renders the images, so nothing new is stored on the tenant until the change is complete.
    /// </summary>
    public class SiteTemplateChanger(ElysianContext context, PlatformContext platform, SiteTemplateCatalog catalog,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor, TimeProvider? timeProvider = null,
        SiteManifestReader? manifests = null, IEdgeHostMap? hostMap = null, IPlacementPublishQueue? queue = null,
        ILogger<SiteTemplateChanger>? logger = null)
    {
        public const string Section = "TemplateChange";
        public const string PublishReason = "template-change";
        public const string SettledReason = "template-changed";

        private sealed record Pending(string? TemplateKey, int? TemplateVersion, DateTimeOffset? RequestedAt);

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private ElysianTenantInfo Tenant => multiTenantContextAccessor.MultiTenantContext.TenantInfo!;

        /// <summary>
        /// The templates a site can be drawn with: the newest version of each that ships its own image slots
        /// </summary>
        public IReadOnlyList<SiteTemplateOption> Options()
        {
            var tenant = Tenant;
            return [.. catalog.All
                .GroupBy(t => t.Key)
                .Select(g => g.MaxBy(t => t.Version)!)
                .Where(t => t.ManifestJson != null)
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Select(t => new SiteTemplateOption(t.Key, t.Version, t.Name, t.Description, t.Key == tenant.TemplateKey && t.Version == tenant.TemplateVersion))];
        }

        /// <summary>
        /// The change under way, or null. One that names a template this host no longer has is no change.
        /// </summary>
        public async Task<SiteTemplateChangeModel?> PendingAsync(CancellationToken cancellationToken)
        {
            var row = await context.TenantSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Section == Section, cancellationToken);
            var pending = Read(row?.Json);
            var template = catalog.Find(pending?.TemplateKey, pending?.TemplateVersion);
            return template == null ? null : new SiteTemplateChangeModel(template.Key, template.Version, template.Name, pending!.RequestedAt ?? default);
        }

        /// <summary>
        /// Records the change and queues the rendering of the new template's images. Asking again replaces
        /// what was asked before, which is also how a change that got stuck is started again.
        /// </summary>
        public async Task<SiteTemplateChangeModel> RequestAsync(SiteTemplate target, CancellationToken cancellationToken)
        {
            if (queue == null)
            {
                throw new ConflictException("This host can't render a template's images, so a site's template can't be changed here.");
            }

            var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
            await StoreAsync(new Pending(target.Key, target.Version, now), cancellationToken);
            await queue.SendAsync(new PlacementPublishMessage(Tenant.Identifier!, PublishReason, TemplateKey: target.Key, TemplateVersion: target.Version),
                cancellationToken: cancellationToken);
            return new SiteTemplateChangeModel(target.Key, target.Version, target.Name, now);
        }

        public Task ClearAsync(CancellationToken cancellationToken) => StoreAsync(null, cancellationToken);

        /// <summary>
        /// The reserved image slots of a template the site is moving to, or null when this host doesn't have it
        /// </summary>
        public PlacementCatalog? CatalogFor(string templateKey, int templateVersion)
        {
            var json = catalog.Find(templateKey, templateVersion)?.ManifestFor(Tenant.Name ?? Tenant.Identifier!, "https://site.invalid");
            return json == null ? null : SiteManifestDocument.FromJson(json).Placements;
        }

        /// <summary>
        /// Points the site at the template, once its images exist. Does nothing when that change is no longer
        /// the one asked for. Safe to run twice: every step sets what is already set.
        /// </summary>
        /// <returns>Whether the site is now on the template</returns>
        public async Task<bool> CompleteAsync(string templateKey, int templateVersion, CancellationToken cancellationToken)
        {
            var tenant = Tenant;
            var pending = await PendingAsync(cancellationToken);
            var target = catalog.Find(templateKey, templateVersion);
            if (target == null || pending == null || pending.TemplateKey != templateKey || pending.TemplateVersion != templateVersion)
            {
                logger?.LogWarning("{Tenant} is no longer changing to {Template}@{Version}; left as it is", tenant.Identifier, templateKey, templateVersion);
                return false;
            }

            // 1. The draft, so it can be saved and published under the new template straight away
            var draft = await context.SiteContentDrafts.FirstOrDefaultAsync(cancellationToken);
            if (draft != null)
            {
                draft.Json = Fitted(SiteContentDocument.FromJson(draft.Json).Normalize(), target).ToJson();
                draft.TemplateKey = target.Key;
                draft.TemplateVersion = target.Version;
                await context.SaveChangesAsync(cancellationToken);
            }

            // 2. The record of which template the site is on: what the admin app and the next publish read
            var row = await platform.Tenants.FirstOrDefaultAsync(t => t.Id == tenant.Id, cancellationToken)
                ?? throw new InvalidOperationException($"Tenant {tenant.Identifier} is missing, so its template can't be changed.");
            row.TemplateKey = target.Key;
            row.TemplateVersion = target.Version;
            await platform.SaveChangesAsync(cancellationToken);
            manifests?.Invalidate();

            // 3. The edge, which is what visitors see. If this fails the message is retried and gets here again.
            if (hostMap != null)
            {
                await hostMap.SyncAsync(tenant.Identifier!, cancellationToken);
            }

            await ClearAsync(cancellationToken);

            // A photo picked while the images were being rendered would otherwise wait for the next change
            if (queue != null)
            {
                try
                {
                    await queue.SendAsync(new PlacementPublishMessage(tenant.Identifier!, SettledReason), cancellationToken: cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger?.LogWarning(ex, "{Tenant} changed template, but a follow-up publish of its images could not be queued", tenant.Identifier);
                }
            }

            logger?.LogInformation("{Tenant} is now drawn with {Template}@{Version}", tenant.Identifier, target.Key, target.Version);
            return true;
        }

        /// <summary>
        /// The content as the new template can hold it. A palette or font the template doesn't have becomes
        /// its first; an accent chosen against another palette goes with that palette.
        /// </summary>
        public static SiteContentDocument Fitted(SiteContentDocument content, SiteTemplate target)
        {
            var keepsPalette = target.FindPalette(content.Theme.Palette) != null;
            var fitted = new SiteContentDocument
            {
                Theme = new SiteContentTheme(
                    keepsPalette ? content.Theme.Palette : target.Palettes[0].Key,
                    keepsPalette ? content.Theme.Accent : null,
                    target.FindFont(content.Theme.Font) != null ? content.Theme.Font : target.Fonts[0].Key),
                Text = content.Text,
                Links = content.Links,
                Categories = content.Categories,
            };

            return fitted.Validate(target).Any(p => p.Field == "theme.accent")
                ? new SiteContentDocument { Theme = fitted.Theme with { Accent = null }, Text = fitted.Text, Links = fitted.Links, Categories = fitted.Categories }
                : fitted;
        }

        private async Task StoreAsync(Pending? pending, CancellationToken cancellationToken)
        {
            var row = await context.TenantSettings.FirstOrDefaultAsync(s => s.Section == Section, cancellationToken);
            if (row == null)
            {
                if (pending == null)
                {
                    return;
                }
                row = new TenantSetting { Section = Section };
                context.TenantSettings.Add(row);
            }

            row.Json = pending == null ? "{}" : JsonSerializer.Serialize(pending, JsonOptions);
            await context.SaveChangesAsync(cancellationToken);
        }

        private static Pending? Read(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            try
            {
                return JsonSerializer.Deserialize<Pending>(json, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Starts moving the site to another template, or to a newer version of its own. The site keeps its
    /// current look until the new one's images are ready, usually within a minute or two; the editing screen
    /// shows the change as under way until then.
    /// </summary>
    /// <param name="Template">Key of the template to move to, e.g. "gallery"</param>
    [Authorize(Policy = PolicyNames.SitePublish)]
    public record ChangeSiteTemplateCommand(string? Template) : IRequest<SiteTemplateChangeModel>;

    public class ChangeSiteTemplateCommandHandler(SiteContentReader reader, SiteTemplateCatalog catalog, SiteTemplateChanger changer)
        : IRequestHandler<ChangeSiteTemplateCommand, SiteTemplateChangeModel>
    {
        public Task<SiteTemplateChangeModel> Handle(ChangeSiteTemplateCommand request, CancellationToken cancellationToken)
        {
            // Only a site that is on a template can move to another
            var current = reader.Template();

            var target = catalog.Latest(request.Template?.Trim().ToLowerInvariant());
            if (target?.ManifestJson == null)
            {
                throw new CustomValidationException([new ValidationFailure(nameof(request.Template), "Choose one of the templates.")]);
            }
            if (target.Key == current.Key && target.Version == current.Version)
            {
                throw new ConflictException($"This site already uses the {target.Name} template.");
            }

            return changer.RequestAsync(target, cancellationToken);
        }
    }

    /// <summary>
    /// Gives up a change of template that hasn't finished. The site stays as it is. If the change completes
    /// in the same moment, it stands.
    /// </summary>
    [Authorize(Policy = PolicyNames.SitePublish)]
    public record CancelSiteTemplateChangeCommand : IRequest;

    public class CancelSiteTemplateChangeCommandHandler(SiteContentReader reader, SiteTemplateChanger changer) : IRequestHandler<CancelSiteTemplateChangeCommand>
    {
        public Task Handle(CancelSiteTemplateChangeCommand request, CancellationToken cancellationToken)
        {
            reader.Template();
            return changer.ClearAsync(cancellationToken);
        }
    }
    /// <summary>
    /// The templates a new site can be created on, for the platform's own screens. Outside any tenant, so
    /// none is marked as current.
    /// </summary>
    [RequiresPlatformAdmin]
    public record GetSiteTemplatesQuery : IRequest<List<SiteTemplateOption>>;

    public class GetSiteTemplatesQueryHandler(SiteTemplateCatalog catalog) : IRequestHandler<GetSiteTemplatesQuery, List<SiteTemplateOption>>
    {
        public Task<List<SiteTemplateOption>> Handle(GetSiteTemplatesQuery request, CancellationToken cancellationToken) =>
            Task.FromResult(catalog.All
                .GroupBy(t => t.Key)
                .Select(g => g.MaxBy(t => t.Version)!)
                .Where(t => t.ManifestJson != null)
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .Select(t => new SiteTemplateOption(t.Key, t.Version, t.Name, t.Description, Current: false))
                .ToList());
    }
}
