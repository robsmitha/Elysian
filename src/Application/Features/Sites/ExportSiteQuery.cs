using Elysian.Application.Features.SiteContent;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using Finbuckle.MultiTenant.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Sites
{
    public sealed record SiteExportPhoto(Guid Id, string Category, Guid? StoryId, int SortOrder, string? AltText, string FileName,
        long FileSize, int? Width, int? Height, double FocusX, double FocusY, string? OriginalUrl);

    public sealed record SiteExportStory(Guid Id, string Slug, string Category, string Title, string? Description, DateOnly? EventDate,
        string? VenueName, string? City, string? State, string Status, DateTimeOffset? PublishedAt, Guid? HeroPhotoId, string? SeoTitle,
        string? SeoDescription);

    public sealed record SiteExportSession(string Name, string Description, decimal? Price, int DurationMinutes, string? Location,
        string Collection, IReadOnlyList<string> Features, string? PortfolioCategory, Guid? CoverPhotoId, int SortOrder, bool IsBookable);

    public sealed record SiteExportVersion(int Version, DateTimeOffset PublishedAt, string TemplateKey, int TemplateVersion, SiteContentDocument Content);

    /// <summary>
    /// Everything of a site that its owner made, in one document: its wording as drafted and as published, its
    /// stories and sessions, which photo fills each spot, and every photo with a link to the file that was
    /// uploaded. The links are signed and stop working at <paramref name="LinksExpireAt"/>.
    ///
    /// Left out on purpose: bookings and messages from visitors (they are in the owner's calendar and inbox,
    /// not here), connected accounts' tokens, and who has access.
    /// </summary>
    public sealed record SiteExport(string Identifier, string? Name, string? Template, int? TemplateVersion, DateTimeOffset ExportedAt,
        DateTimeOffset? LinksExpireAt, IReadOnlyList<string> Addresses, SiteContentDocument? Draft, IReadOnlyList<SiteExportVersion> Published,
        IReadOnlyList<SiteExportStory> Stories, IReadOnlyList<SiteExportSession> Sessions, IReadOnlyDictionary<string, Guid> Placements,
        IReadOnlyList<SiteExportPhoto> Photos);

    /// <summary>
    /// The current tenant's export. For an owner taking a copy, and for the platform before a site is deleted.
    /// </summary>
    [Authorize(Policy = PolicyNames.SitePublish)]
    public record ExportSiteQuery : IRequest<SiteExport>;

    public class ExportSiteQueryHandler(ElysianContext context, IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor,
        TimeProvider timeProvider, IPhotoStorage? photoStorage = null) : IRequestHandler<ExportSiteQuery, SiteExport>
    {
        /// <summary>Long enough to download a library; short enough that a forgotten file is soon harmless</summary>
        public static readonly TimeSpan LinkLifetime = TimeSpan.FromHours(24);

        public async Task<SiteExport> Handle(ExportSiteQuery request, CancellationToken cancellationToken)
        {
            var tenant = multiTenantContextAccessor.MultiTenantContext.TenantInfo!;
            var now = timeProvider.GetUtcNow();

            var draft = await context.SiteContentDrafts.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
            var versions = await context.SiteContentVersions.AsNoTracking().OrderBy(v => v.Version).ToListAsync(cancellationToken);
            var stories = await context.Stories.AsNoTracking().OrderBy(s => s.Category).ThenBy(s => s.Slug).ToListAsync(cancellationToken);
            var sessions = await context.ProductSessions.AsNoTracking().Include(s => s.Product).OrderBy(s => s.SortOrder).ToListAsync(cancellationToken);
            var placements = await context.Placements.AsNoTracking().ToListAsync(cancellationToken);
            var photos = await context.Photos.AsNoTracking().OrderBy(p => p.Category).ThenBy(p => p.SortOrder).ToListAsync(cancellationToken);
            var addresses = await context.TenantDomains.AsNoTracking().OrderByDescending(d => d.IsPrimary).ThenBy(d => d.Host).Select(d => d.Host).ToListAsync(cancellationToken);

            var exported = new List<SiteExportPhoto>(photos.Count);
            foreach (var photo in photos)
            {
                var link = photoStorage == null || string.IsNullOrEmpty(photo.OriginalBlobName)
                    ? null
                    : (await photoStorage.CreateOriginalDownloadUriAsync(photo.OriginalBlobName, LinkLifetime, cancellationToken)).ToString();
                exported.Add(new SiteExportPhoto(photo.PhotoId, photo.Category, photo.StoryId, photo.SortOrder, photo.AltText, photo.OriginalFileName,
                    photo.OriginalFileSize, photo.Width, photo.Height, photo.FocusX, photo.FocusY, link));
            }

            return new SiteExport(
                tenant.Identifier!, tenant.Name, tenant.TemplateKey, tenant.TemplateVersion, now,
                photoStorage != null && exported.Count > 0 ? now.Add(LinkLifetime) : null,
                addresses,
                draft == null ? null : SiteContentDocument.FromJson(draft.Json),
                [.. versions.Select(v => new SiteExportVersion(v.Version, v.CreatedAt, v.TemplateKey, v.TemplateVersion, SiteContentDocument.FromJson(v.Json)))],
                [.. stories.Select(s => new SiteExportStory(s.StoryId, s.Slug, s.Category, s.Title, s.Description, s.EventDate, s.VenueName, s.City, s.State,
                    s.Status.ToString(), s.PublishedAt, s.HeroPhotoId, s.SeoTitle, s.SeoDescription))],
                [.. sessions.Select(s => new SiteExportSession(s.Product.Name, s.Product.Description, s.Product.Price, s.DurationMinutes, s.Location,
                    s.Collection, s.Features, s.PortfolioCategory, s.CoverPhotoId, s.SortOrder, s.IsBookable))],
                placements.GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.First().PhotoId),
                exported);
        }
    }
}
