using Elysian.Application.Exceptions;
using Elysian.Application.Features.Sites;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.SiteContent
{
    /// <param name="Version">1 for the first publish, then counting up</param>
    /// <param name="IsLive">The version visitors see now</param>
    /// <param name="CanRestore">It was made with the template the site is on now, so it can be gone back to</param>
    public sealed record SiteContentVersionSummary(int Version, DateTimeOffset PublishedAt, string TemplateKey, int TemplateVersion,
        bool IsLive, bool CanRestore);

    /// <summary>
    /// The site's published versions, newest first: what there is to go back to
    /// </summary>
    [Authorize(Policy = PolicyNames.SiteRead)]
    public record GetSiteContentVersionsQuery : IRequest<List<SiteContentVersionSummary>>;

    public class GetSiteContentVersionsQueryHandler(ElysianContext context, SiteContentReader reader)
        : IRequestHandler<GetSiteContentVersionsQuery, List<SiteContentVersionSummary>>
    {
        /// <summary>More than anyone looks back through; every version is still kept</summary>
        public const int Most = 50;

        public async Task<List<SiteContentVersionSummary>> Handle(GetSiteContentVersionsQuery request, CancellationToken cancellationToken)
        {
            var template = reader.Template();
            var versions = await context.SiteContentVersions.AsNoTracking()
                .OrderByDescending(v => v.Version)
                .Take(Most)
                .Select(v => new { v.Version, v.CreatedAt, v.TemplateKey, v.TemplateVersion })
                .ToListAsync(cancellationToken);

            var live = versions.Count > 0 ? versions[0].Version : 0;
            return [.. versions.Select(v => new SiteContentVersionSummary(v.Version, v.CreatedAt, v.TemplateKey, v.TemplateVersion,
                IsLive: v.Version == live,
                CanRestore: v.TemplateKey == template.Key && v.TemplateVersion == template.Version))];
        }
    }

    /// <summary>
    /// Goes back to an earlier published version by making it the draft. Visitors see no change: the owner
    /// looks at it in the preview and publishes it, which makes a new version. What was in the draft is
    /// replaced, so anything unpublished in it is lost. Only a version made with the template the site is on
    /// now can be gone back to.
    /// </summary>
    [Authorize(Policy = PolicyNames.SiteWrite)]
    public record RestoreSiteContentVersionCommand(int Version) : IRequest<SiteContentDocument>;

    public class RestoreSiteContentVersionCommandHandler(ElysianContext context, SiteContentReader reader, SiteManifestReader? manifests = null)
        : IRequestHandler<RestoreSiteContentVersionCommand, SiteContentDocument>
    {
        public async Task<SiteContentDocument> Handle(RestoreSiteContentVersionCommand request, CancellationToken cancellationToken)
        {
            var template = reader.Template();
            var version = await context.SiteContentVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Version == request.Version, cancellationToken)
                ?? throw new NotFoundException("Version", request.Version);

            if (version.TemplateKey != template.Key || version.TemplateVersion != template.Version)
            {
                throw new ConflictException("That version was made with a different template, so it can't be brought back as it was.");
            }

            // Checked like any other save: what was right when it was published is held to today's rules
            var content = SiteContentReader.Checked(SiteContentDocument.FromJson(version.Json), template);

            var row = await context.SiteContentDrafts.FirstOrDefaultAsync(cancellationToken);
            if (row == null)
            {
                row = new SiteContentDraft();
                context.SiteContentDrafts.Add(row);
            }
            row.Json = content.ToJson();
            row.TemplateKey = template.Key;
            row.TemplateVersion = template.Version;
            await context.SaveChangesAsync(cancellationToken);

            manifests?.Invalidate();
            return content;
        }
    }
}
