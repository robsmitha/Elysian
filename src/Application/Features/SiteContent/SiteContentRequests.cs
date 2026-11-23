using Elysian.Application.Exceptions;
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
    /// <param name="Template">What the site's template lets its owner change; the editing screen is built from it</param>
    /// <param name="Draft">The content being edited. A site nobody has edited yet gets the template's defaults.</param>
    /// <param name="PublishedVersion">The version visitors see, or null when nothing has been published</param>
    /// <param name="HasUnpublishedChanges">The draft differs from what visitors see</param>
    /// <param name="Templates">The templates the site could be drawn with, the current one among them</param>
    /// <param name="TemplateChange">A change of template that is under way, or null</param>
    public sealed record SiteContentModel(SiteTemplate Template, SiteContentDocument Draft, int? PublishedVersion,
        DateTimeOffset? PublishedAt, bool HasUnpublishedChanges, IReadOnlyList<SiteTemplateOption>? Templates = null,
        SiteTemplateChangeModel? TemplateChange = null);

    public sealed record SiteContentPublishedModel(int Version, DateTimeOffset PublishedAt);

    public sealed record SitePreviewLinkModel(string Token, DateTimeOffset ExpiresAt);

    /// <param name="TemplateKey">The template the content belongs to, so whoever draws the preview uses the right one</param>
    public sealed record SiteContentPreviewModel(string TemplateKey, int TemplateVersion, SiteContentDocument Content);

    /// <summary>
    /// The current tenant's site content for editing: its template's schema, the draft, and what is live
    /// </summary>
    [Authorize(Policy = PolicyNames.SiteRead)]
    public record GetSiteContentQuery : IRequest<SiteContentModel>;

    public class GetSiteContentQueryHandler(ElysianContext context, SiteContentReader reader, SiteTemplateChanger? changer = null)
        : IRequestHandler<GetSiteContentQuery, SiteContentModel>
    {
        public async Task<SiteContentModel> Handle(GetSiteContentQuery request, CancellationToken cancellationToken)
        {
            var template = reader.Template();
            var draft = await reader.DraftAsync(template, cancellationToken);

            var published = await context.SiteContentVersions.AsNoTracking()
                .OrderByDescending(v => v.Version)
                .FirstOrDefaultAsync(cancellationToken);

            return new SiteContentModel(template, draft, published?.Version, published?.CreatedAt,
                HasUnpublishedChanges: published == null || published.Json != draft.ToJson()
                    || published.TemplateKey != template.Key || published.TemplateVersion != template.Version,
                Templates: changer?.Options() ?? [],
                TemplateChange: changer == null ? null : await changer.PendingAsync(cancellationToken));
        }
    }

    /// <summary>
    /// Saves the draft. It is checked against the site's template first, and nothing is stored when anything
    /// is wrong: each problem comes back against its field, e.g. "text.home.heading". Visitors see no change
    /// until the draft is published.
    /// </summary>
    [Authorize(Policy = PolicyNames.SiteWrite)]
    public record SaveSiteContentDraftCommand(SiteContentDocument? Content) : IRequest<SiteContentDocument>;

    public class SaveSiteContentDraftCommandHandler(ElysianContext context, SiteContentReader reader, SiteManifestReader? manifests = null)
        : IRequestHandler<SaveSiteContentDraftCommand, SiteContentDocument>
    {
        public async Task<SiteContentDocument> Handle(SaveSiteContentDraftCommand request, CancellationToken cancellationToken)
        {
            var template = reader.Template();
            var content = SiteContentReader.Checked(request.Content, template);

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

            // The site's manifest carries the owner's category names, which may just have changed
            manifests?.Invalidate();
            return content;
        }
    }

    /// <summary>
    /// Makes the draft what visitors see. Write first, switch second: the draft becomes a new numbered version,
    /// that version is written to the edge, and only then is the site pointed at it. If any step fails the
    /// previous version keeps serving, and publishing again makes a fresh version.
    /// </summary>
    [Authorize(Policy = PolicyNames.SitePublish)]
    public record PublishSiteContentCommand : IRequest<SiteContentPublishedModel>;

    public class PublishSiteContentCommandHandler(ElysianContext context, PlatformContext platform, SiteContentReader reader,
        ISiteContentEdge edge, ILogger<PublishSiteContentCommandHandler> logger)
        : IRequestHandler<PublishSiteContentCommand, SiteContentPublishedModel>
    {
        public async Task<SiteContentPublishedModel> Handle(PublishSiteContentCommand request, CancellationToken cancellationToken)
        {
            var template = reader.Template();
            var tenant = reader.Tenant;

            // Checked again: the template may have moved on since the draft was saved
            var content = SiteContentReader.Checked(await reader.DraftAsync(template, cancellationToken), template);
            var json = content.ToJson();

            var last = await context.SiteContentVersions.MaxAsync(v => (int?)v.Version, cancellationToken) ?? 0;
            var version = new SiteContentVersion
            {
                Version = last + 1,
                Json = json,
                TemplateKey = template.Key,
                TemplateVersion = template.Version,
            };
            context.SiteContentVersions.Add(version);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // The unique index refused the number: someone else published at the same moment
                throw new ConflictException("The site was published a moment ago by someone else. Check the result, then publish again if it's still needed.");
            }

            await edge.WriteVersionAsync(tenant.Identifier!, version.Version, template.Key, template.Version, json, cancellationToken);
            await edge.PointToVersionAsync(tenant.Identifier!, version.Version, cancellationToken);

            // The record of which version is live. The edge already serves it, so a failure here is logged
            // and not thrown: the owner's publish worked.
            try
            {
                var row = await platform.Tenants.FirstOrDefaultAsync(t => t.Id == tenant.Id, cancellationToken);
                if (row != null)
                {
                    row.ContentVersion = version.Version;
                    await platform.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Published site content version {Version} for {Tenant}, but could not record it on the tenant", version.Version, tenant.Identifier);
            }

            return new SiteContentPublishedModel(version.Version, version.CreatedAt);
        }
    }

    /// <summary>
    /// Issues a short-lived token that opens the draft through <see cref="GetSiteContentPreviewQuery"/>, so an
    /// owner can look at unpublished changes on the public site without signing in there
    /// </summary>
    [Authorize(Policy = PolicyNames.SiteRead)]
    public record CreateSitePreviewLinkCommand : IRequest<SitePreviewLinkModel>;

    public class CreateSitePreviewLinkCommandHandler(SiteContentReader reader, ISitePreviewTokens tokens)
        : IRequestHandler<CreateSitePreviewLinkCommand, SitePreviewLinkModel>
    {
        public Task<SitePreviewLinkModel> Handle(CreateSitePreviewLinkCommand request, CancellationToken cancellationToken)
        {
            // Only a site with a template has anything to preview
            reader.Template();
            var (token, expiresAt) = tokens.Issue(reader.Tenant.Identifier!);
            return Task.FromResult(new SitePreviewLinkModel(token, expiresAt));
        }
    }

    /// <summary>
    /// Anonymous read of the current tenant's draft, for whoever holds an unexpired token from
    /// <see cref="CreateSitePreviewLinkCommand"/>. Hosts must not cache the response. A bad or expired token is
    /// "not found", with no hint of why.
    /// </summary>
    public record GetSiteContentPreviewQuery(string? Token) : IRequest<SiteContentPreviewModel>;

    public class GetSiteContentPreviewQueryHandler(SiteContentReader reader, ISitePreviewTokens tokens)
        : IRequestHandler<GetSiteContentPreviewQuery, SiteContentPreviewModel>
    {
        private const string NotFound = "This preview link is not valid or has expired.";

        public async Task<SiteContentPreviewModel> Handle(GetSiteContentPreviewQuery request, CancellationToken cancellationToken)
        {
            if (!tokens.IsValid(reader.Tenant.Identifier!, request.Token))
            {
                throw new NotFoundException(NotFound);
            }

            SiteTemplate template;
            try
            {
                template = reader.Template();
            }
            catch (ConflictException)
            {
                throw new NotFoundException(NotFound);
            }

            return new SiteContentPreviewModel(template.Key, template.Version, await reader.DraftAsync(template, cancellationToken));
        }
    }

    /// <summary>
    /// What the site content requests share: the current tenant, its template, and its draft
    /// </summary>
    public class SiteContentReader(ElysianContext context, SiteTemplateCatalog catalog,
        IMultiTenantContextAccessor<ElysianTenantInfo> multiTenantContextAccessor)
    {
        public ElysianTenantInfo Tenant => multiTenantContextAccessor.MultiTenantContext.TenantInfo!;

        /// <exception cref="ConflictException">The site has no template, or this host doesn't have its version</exception>
        public SiteTemplate Template()
        {
            var tenant = Tenant;
            if (string.IsNullOrEmpty(tenant.TemplateKey) || tenant.TemplateVersion == null)
            {
                throw new ConflictException("This site has its own code, not a shared template, so its colours and wording aren't edited here.");
            }

            return catalog.Find(tenant.TemplateKey, tenant.TemplateVersion)
                ?? throw new ConflictException($"This site uses the {tenant.TemplateKey} template, version {tenant.TemplateVersion}, which this host doesn't have.");
        }

        /// <summary>
        /// The stored draft, or the template's defaults when nothing has been saved. A draft saved under
        /// another template version is read as it is; saving or publishing checks it against the current one.
        /// </summary>
        public async Task<SiteContentDocument> DraftAsync(SiteTemplate template, CancellationToken cancellationToken)
        {
            var row = await context.SiteContentDrafts.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
            return row == null ? SiteContentDocument.DefaultFor(template).Normalize() : SiteContentDocument.FromJson(row.Json).Normalize();
        }

        /// <summary>
        /// The content normalized, or a validation error listing everything wrong with it
        /// </summary>
        public static SiteContentDocument Checked(SiteContentDocument? content, SiteTemplate template)
        {
            if (content == null)
            {
                throw new CustomValidationException([new ValidationFailure("content", "There is no content to save.")]);
            }

            var normalized = content.Normalize();
            if (normalized.ToJson().Length > SiteContentDocument.MaxLength)
            {
                throw new CustomValidationException([new ValidationFailure("content", $"The content is larger than {SiteContentDocument.MaxLength / 1024} KB.")]);
            }

            var problems = normalized.Validate(template);
            if (problems.Count > 0)
            {
                throw new CustomValidationException(problems.Select(p => new ValidationFailure(p.Field, p.Message)));
            }
            return normalized;
        }
    }
}
