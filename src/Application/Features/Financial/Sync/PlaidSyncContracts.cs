using System.Text.Json;
using System.Text.Json.Serialization;
using Elysian.Domain.Constants;

namespace Elysian.Application.Features.Financial.Sync
{
    /// <summary>A queued request to sync one bank. The tenant travels with it because the queue has no request.</summary>
    public record PlaidSyncMessage(string Tenant, int InstitutionAccessItemId, SyncTrigger Trigger);

    public static class PlaidSyncQueue
    {
        public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };

        public static string Serialize(PlaidSyncMessage message) => JsonSerializer.Serialize(message, JsonOptions);

        public static PlaidSyncMessage Deserialize(string body) =>
            JsonSerializer.Deserialize<PlaidSyncMessage>(body, JsonOptions)
                ?? throw new InvalidOperationException("Empty plaid-sync message");
    }

    /// <summary>How one sync went. <see cref="Retry"/> is true for temporary failures the queue should try again.</summary>
    public record SyncOutcome(bool Succeeded, int Added = 0, int Modified = 0, int Removed = 0, string? ErrorCode = null, bool Retry = false, bool Skipped = false)
    {
        public static SyncOutcome Skip(string reason) => new(false, ErrorCode: reason, Skipped: true);
    }
}
