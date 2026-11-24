using Elysian.Application.Exceptions;
using Elysian.Application.Features.Placements.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.SiteContent
{
    /// <summary>
    /// Where a site's logo files are and what one may be. They live beside the site's reserved images, so the
    /// images host serves them and deleting the site removes them, but under a name no slot has.
    /// </summary>
    public static class SiteLogoPaths
    {
        /// <summary>The most an upload may weigh. A logo is a small file; anything larger is a photo.</summary>
        public const int MaxUploadBytes = 2 * 1024 * 1024;

        /// <summary>The tallest and widest the larger file is made, in pixels: twice what a header shows</summary>
        public const int MaxHeight = 160;
        public const int MaxWidth = 800;

        /// <summary>Shorter than this, the larger file would be too coarse to show</summary>
        public const int MinHeight = 40;

        /// <summary>{tenant}/logo-{version}-{height}.webp</summary>
        public static string File(string tenantIdentifier, long version, int height) => $"{tenantIdentifier}/logo-{version}-{height}.webp";

        public static bool IsWellFormed(SiteContentLogo logo) =>
            logo.Version is > 0 and < 100_000_000_000
            && logo.Height is >= MinHeight and <= MaxHeight && logo.Height % 2 == 0
            && logo.Width is >= 1 and <= MaxWidth;
    }

    /// <summary>
    /// Sets the site's logo from an uploaded image, in the draft. Visitors see it once the draft is published.
    /// The upload is drawn again as two small WebP files and never served as it arrived.
    /// </summary>
    /// <param name="Content">The file as uploaded: a PNG, JPEG or WebP image</param>
    [Authorize(Policy = PolicyNames.SiteWrite)]
    public record UploadSiteLogoCommand(byte[]? Content) : IRequest<SiteContentDocument>;

    public class UploadSiteLogoCommandHandler(ElysianContext context, SiteContentReader reader, ISiteLogoRenderer renderer,
        TimeProvider timeProvider, IPlacementStore? store = null) : IRequestHandler<UploadSiteLogoCommand, SiteContentDocument>
    {
        public async Task<SiteContentDocument> Handle(UploadSiteLogoCommand request, CancellationToken cancellationToken)
        {
            var template = reader.Template();
            if (store == null)
            {
                throw new ConflictException("Images can't be stored on this host, so a logo can't be uploaded.");
            }
            if (request.Content is not { Length: > 0 })
            {
                throw new CustomValidationException([new ValidationFailure("logo", "Choose an image to upload.")]);
            }
            if (request.Content.Length > SiteLogoPaths.MaxUploadBytes)
            {
                throw new CustomValidationException([new ValidationFailure("logo", $"A logo must be {SiteLogoPaths.MaxUploadBytes / 1024 / 1024} MB or smaller.")]);
            }

            var files = await renderer.RenderAsync(request.Content, cancellationToken);

            // A new name every time: the files of the logo visitors see now are left exactly as they are
            var now = timeProvider.GetUtcNow();
            var logo = new SiteContentLogo(now.ToUnixTimeSeconds(), files.Width, files.Height);
            var tenant = reader.Tenant.Identifier!;
            var metadata = new PlacementMetadata("logo", PlacementSource.Explicit, null, files.Width, now);
            await store.UploadAsync(SiteLogoPaths.File(tenant, logo.Version, files.Height / 2), files.Small, metadata, cancellationToken);
            await store.UploadAsync(SiteLogoPaths.File(tenant, logo.Version, files.Height), files.Large, metadata, cancellationToken);

            return await SiteLogoDraft.SetAsync(context, reader, template, logo, cancellationToken);
        }
    }

    /// <summary>
    /// Takes the logo out of the draft, so the site shows its name again once published. The files stay: the
    /// published site may still be showing them, and an earlier version can be gone back to.
    /// </summary>
    [Authorize(Policy = PolicyNames.SiteWrite)]
    public record RemoveSiteLogoCommand : IRequest<SiteContentDocument>;

    public class RemoveSiteLogoCommandHandler(ElysianContext context, SiteContentReader reader) : IRequestHandler<RemoveSiteLogoCommand, SiteContentDocument>
    {
        public Task<SiteContentDocument> Handle(RemoveSiteLogoCommand request, CancellationToken cancellationToken) =>
            SiteLogoDraft.SetAsync(context, reader, reader.Template(), null, cancellationToken);
    }

    internal static class SiteLogoDraft
    {
        public static async Task<SiteContentDocument> SetAsync(ElysianContext context, SiteContentReader reader, SiteTemplate template,
            SiteContentLogo? logo, CancellationToken cancellationToken)
        {
            var content = (await reader.DraftAsync(template, cancellationToken)).WithLogo(logo);

            var row = await context.SiteContentDrafts.FirstOrDefaultAsync(cancellationToken);
            if (row == null)
            {
                row = new SiteContentDraft { TemplateKey = template.Key, TemplateVersion = template.Version };
                context.SiteContentDrafts.Add(row);
            }
            row.Json = content.ToJson();
            await context.SaveChangesAsync(cancellationToken);
            return content;
        }
    }
}
