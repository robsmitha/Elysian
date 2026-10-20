using Elysian.Domain.Data;

namespace Elysian.Application.Features.Placements.Models
{
    /// <summary>
    /// Where a slot's image came from. Anything but <see cref="Explicit"/> is a fallback.
    /// </summary>
    public enum PlacementSource
    {
        /// <summary>The admin picked a photo for this slot</summary>
        Explicit,
        /// <summary>First Ready gallery photo in the slot's fallback category</summary>
        Category,
        /// <summary>The photo picked for the catalog's site-default key</summary>
        SiteDefault,
        /// <summary>No photo anywhere: a plain frame, so the reserved URL never 404s</summary>
        Neutral,
    }

    public sealed record ResolvedSlot(PlacementSlot Slot, Photo? Photo, PlacementSource Source, string Fingerprint)
    {
        /// <summary>
        /// Width of the slot-shaped crop of the photo; below <see cref="PlacementSlot.LargestWidth"/>
        /// the larger variants can't be filled at full resolution
        /// </summary>
        public int? SourceWidth => Photo is { Width: > 0, Height: > 0 } p
            ? (double)p.Width.Value / p.Height.Value > Slot.Ratio
                ? (int)Math.Round(p.Height.Value * Slot.Ratio)
                : p.Width.Value
            : null;
    }

    /// <summary>
    /// One file for one slot. <see cref="NamedWidth"/> is the width in the file name; the real size is smaller only
    /// when the source can't fill it (never upscaled), and keeps the slot's ratio either way.
    /// </summary>
    public sealed record PlacementVariant(int NamedWidth, int Width, int Height, byte[] Content);

    /// <summary>
    /// Written on every variant; read back from the largest (written last) to tell whether a slot is current
    /// </summary>
    public sealed record PlacementMetadata(string Fingerprint, PlacementSource Source, Guid? PhotoId, int SourceWidth, DateTimeOffset PublishedAt);

    public sealed record PublishedPlacement(string? Fingerprint, string? Source, DateTimeOffset? PublishedAt);

    /// <summary>
    /// A file just written: its public URL and the ETag the CDN should now serve
    /// </summary>
    public sealed record PublishedFile(string Url, string ETag);

    /// <summary>
    /// Body of a placements-publish queue message. <see cref="Tenant"/> lets a host's tenant resolver read it from
    /// the trigger's binding data. <see cref="Repurge"/> set means "purge these exact URLs again".
    /// </summary>
    public sealed record PlacementPublishMessage(string Tenant, string Reason, bool Force = false, string[]? Repurge = null);

    public enum PlacementPublishOutcome
    {
        Published,
        UpToDate,
        Repurged,
        /// <summary>Another publish held the tenant's lock; the message was re-queued</summary>
        Busy,
        /// <summary>PlacementPublishing:Enabled is off</summary>
        Disabled,
    }

    public sealed record PlacementPublishResult(PlacementPublishOutcome Outcome, int ChangedSlots = 0, int PurgedUrls = 0);

    public sealed record PlacementSlotStatus(
        string Key,
        string Name,
        string Source,
        Guid? PhotoId,
        bool Live,
        DateTimeOffset? PublishedAt,
        int? SourceWidth,
        int LargestWidth);

    public sealed record PlacementStatusModel(bool Enabled, IReadOnlyList<PlacementSlotStatus> Slots);
}
