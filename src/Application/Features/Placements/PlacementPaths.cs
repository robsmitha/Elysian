using System.Text.Json;
using Elysian.Application.Features.Placements.Models;

namespace Elysian.Application.Features.Placements
{
    /// <summary>
    /// Blob naming for reserved placements. Unlike <c>PhotoPaths</c> variants these names are fixed and overwritten,
    /// so they're served with a short browser TTL and purged from the CDN on every publish.
    /// </summary>
    public static class PlacementPaths
    {
        /// <summary>
        /// {tenant}/{name}-{width}.webp, e.g. aspensmitha/home-hero-strip-photo1-720.webp
        /// </summary>
        public static string Variant(string tenantIdentifier, PlacementSlot slot, int width) =>
            $"{tenantIdentifier}/{slot.Name}-{width}.webp";

        /// <summary>
        /// Empty blob leased for the duration of a publish, one publish per tenant at a time
        /// </summary>
        public static string Lock(string tenantIdentifier) => $"{tenantIdentifier}/_publish.lock";
    }

    /// <summary>
    /// The storage queue that carries publish requests from admin changes to the host's queue trigger
    /// </summary>
    public static class PlacementQueue
    {
        public const string QueueName = "placements-publish";

        /// <summary>
        /// Trigger connection setting prefix: PlacementsQueue__queueServiceUri (managed identity) in Azure,
        /// or a plain PlacementsQueue connection string (Azurite) locally
        /// </summary>
        public const string ConnectionName = "PlacementsQueue";

        public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public static string Serialize(PlacementPublishMessage message) => JsonSerializer.Serialize(message, JsonOptions);

        public static PlacementPublishMessage Deserialize(string body) =>
            JsonSerializer.Deserialize<PlacementPublishMessage>(body, JsonOptions)
                ?? throw new InvalidOperationException("Empty placements-publish message");
    }
}
