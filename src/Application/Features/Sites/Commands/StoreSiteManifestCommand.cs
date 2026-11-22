using Elysian.Application.Exceptions;
using Elysian.Application.Features.Placements;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Elysian.Application.Features.Sites.Commands
{
    /// <param name="RemovedSlots">Public names of slots the previous manifest had and this one doesn't</param>
    /// <param name="PlacementsChanged">The slots differ from the previous manifest's, so a publish was queued</param>
    public sealed record StoredSiteManifestResult(IReadOnlyList<string> RemovedSlots, bool PlacementsChanged);

    /// <summary>
    /// Replaces the current tenant's site manifest. Today a platform admin sends it; a site's deploy will send
    /// the same command once sites publish their manifest themselves.
    ///
    /// A slot's <see cref="PlacementSlot.Name"/> is a public image address that live pages point at. A manifest
    /// that drops or renames one is refused unless <paramref name="AllowRemovedSlots"/> says it was meant: the
    /// usual cause is a mistake, and the slot would stop being republished. Its files are never deleted either way.
    /// </summary>
    /// <param name="Source">Where the manifest came from, e.g. "seed" or "deploy"</param>
    [RequiresPlatformAdmin]
    public record StoreSiteManifestCommand(string Json, string Source = "api", bool AllowRemovedSlots = false)
        : IRequest<StoredSiteManifestResult>
    {
        // Keeps a large document out of request logging
        public override string ToString() => $"{nameof(StoreSiteManifestCommand)} {{ Source = {Source}, Length = {Json?.Length ?? 0} }}";
    }

    public class StoreSiteManifestCommandHandler(ElysianContext context, SiteManifestReader reader,
        ILogger<StoreSiteManifestCommandHandler> logger, IPlacementPublishQueue? publishQueue = null)
        : IRequestHandler<StoreSiteManifestCommand, StoredSiteManifestResult>
    {
        public const int MaxSourceLength = 32;

        public async Task<StoredSiteManifestResult> Handle(StoreSiteManifestCommand request, CancellationToken cancellationToken)
        {
            var source = request.Source?.Trim();
            if (string.IsNullOrEmpty(source) || source.Length > MaxSourceLength)
            {
                throw Invalid(nameof(request.Source), $"Say where the manifest came from, in {MaxSourceLength} characters or fewer.");
            }

            SiteManifestDocument document;
            try
            {
                document = SiteManifestDocument.FromJson(request.Json);
            }
            catch (InvalidOperationException ex)
            {
                throw Invalid(nameof(request.Json), ex.Message);
            }

            var row = await context.SiteManifests.FirstOrDefaultAsync(cancellationToken);

            // Compared against what is stored, not the cached copy, and read leniently: a stored manifest that
            // no longer parses must not block the one that replaces it
            var previous = PreviousCatalog(row?.Json);
            var previousSlots = previous?.Slots ?? [];
            var newSlots = document.Placements?.Slots ?? [];
            var removed = previousSlots.Select(s => s.Name)
                .Except(newSlots.Select(s => s.Name), StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();

            if (removed.Count > 0 && !request.AllowRemovedSlots)
            {
                throw Invalid(nameof(request.Json),
                    $"This manifest no longer has these published image names: {string.Join(", ", removed)}. " +
                    "Pages may still show them. Put them back, or store the manifest again allowing removed slots.");
            }

            if (row == null)
            {
                row = new SiteManifest();
                context.SiteManifests.Add(row);
            }

            row.Json = request.Json;
            row.Source = source;
            await context.SaveChangesAsync(cancellationToken);
            reader.Invalidate();

            // A new recipe version re-renders every slot, through the fingerprints, at the next publish
            var placementsChanged = !SameSlots(previousSlots, newSlots)
                || previous?.RecipeVersion != document.Placements?.RecipeVersion
                || previous?.SiteDefaultKey != document.Placements?.SiteDefaultKey;
            if (placementsChanged && newSlots.Count > 0 && publishQueue != null)
            {
                // Never throws; the admin status shows any slot that didn't publish as out of date
                await publishQueue.EnqueueAsync("site-manifest");
            }

            logger.LogInformation("Stored a site manifest from {Source}: {Slots} slots, {Removed} removed, placements changed: {Changed}",
                source, newSlots.Count, removed.Count, placementsChanged);

            return new StoredSiteManifestResult(removed, placementsChanged);
        }

        private static PlacementCatalog? PreviousCatalog(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return SiteManifestDocument.FromJson(json).Placements;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>
        /// The same slots as far as publishing goes: key, public name, shape, sizes and fallback
        /// </summary>
        private static bool SameSlots(IReadOnlyList<PlacementSlot> previous, IReadOnlyList<PlacementSlot> current) =>
            previous.Count == current.Count
            && previous.OrderBy(s => s.Key, StringComparer.Ordinal).Zip(current.OrderBy(s => s.Key, StringComparer.Ordinal))
                .All(pair => pair.First.Key == pair.Second.Key
                    && pair.First.Name == pair.Second.Name
                    && pair.First.Ratio.Equals(pair.Second.Ratio)
                    && pair.First.Widths.SequenceEqual(pair.Second.Widths)
                    && pair.First.FallbackCategory == pair.Second.FallbackCategory);

        private static CustomValidationException Invalid(string property, string message) =>
            new([new ValidationFailure(property, message)]);
    }
}
