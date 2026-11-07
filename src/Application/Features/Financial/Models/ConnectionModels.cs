using System.Text.Json.Serialization;
using Elysian.Domain.Constants;

namespace Elysian.Application.Features.Financial.Models
{
    /// <summary>The accounts page: every linked bank with its health, and recent sync activity.</summary>
    public record ConnectionsResponse(List<ConnectionModel> Connections, List<SyncActivityModel> RecentSyncs);

    public class ConnectionModel
    {
        public int InstitutionAccessItemId { get; set; }
        public string InstitutionName { get; set; }
        public string? InstitutionLogo { get; set; }
        public string? InstitutionColor { get; set; }
        /// <summary>healthy, login_required, pending_expiration, disconnected, error, or syncing (never synced yet)</summary>
        public string Status { get; set; }
        public string? StatusMessage { get; set; }
        public DateTimeOffset? LastSyncedAt { get; set; }
        public DateTimeOffset? ConsentExpiresAt { get; set; }
        public bool NewAccountsAvailable { get; set; }
        /// <summary>When "Sync now" can be used again; null when it can be used now</summary>
        public DateTimeOffset? SyncAvailableAt { get; set; }
        public List<ConnectionAccountModel> Accounts { get; set; } = [];
    }

    public class ConnectionAccountModel
    {
        public int FinancialAccountId { get; set; }
        public string Name { get; set; }
        public string? OfficialName { get; set; }
        public string? Mask { get; set; }
        public string Type { get; set; }
        public string? Subtype { get; set; }
        public decimal? CurrentBalance { get; set; }
        public decimal? AvailableBalance { get; set; }
        public decimal? CreditLimit { get; set; }
        public string? IsoCurrencyCode { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public AccountBudgetRole BudgetRole { get; set; }
    }

    public record SyncActivityModel(DateTimeOffset StartedAt, string InstitutionName, [property: JsonConverter(typeof(JsonStringEnumConverter))] SyncTrigger Trigger, int Added, int Modified, int Removed, string? ErrorCode);

    public record LinkedAccountInput(string Name, string? Mask, string? Subtype);

    /// <summary>What Plaid Link's onSuccess gives the page: the one-time token, the bank, and the accounts chosen.</summary>
    public record ExchangePublicTokenRequest(string PublicToken, string? InstitutionId, List<LinkedAccountInput>? Accounts);

    public record SyncRequestResult(bool Queued, DateTimeOffset SyncAvailableAt);

    /// <summary>How each stored status reads on the page</summary>
    public static class ConnectionStatus
    {
        public static string For(ItemStatus status, bool neverSynced) => status switch
        {
            ItemStatus.LoginRequired => AccessItemStatus.LoginRequired,
            ItemStatus.PendingExpiration => "pending_expiration",
            ItemStatus.Disconnected => "disconnected",
            ItemStatus.Error => AccessItemStatus.Error,
            _ => neverSynced ? "syncing" : AccessItemStatus.Healthy
        };

        public static string? MessageFor(ItemStatus status, DateTimeOffset? consentExpiresAt) => status switch
        {
            ItemStatus.LoginRequired => AccessItemLoader.LoginRequiredMessage,
            ItemStatus.PendingExpiration => consentExpiresAt is { } end
                ? $"This bank's access ends {end:MMM d}. Renew it to keep syncing."
                : "This bank's access ends soon. Renew it to keep syncing.",
            ItemStatus.Disconnected => "This bank was disconnected, so nothing new syncs. Its past transactions are kept.",
            ItemStatus.Error => AccessItemLoader.ErrorMessage,
            _ => null
        };
    }
}
