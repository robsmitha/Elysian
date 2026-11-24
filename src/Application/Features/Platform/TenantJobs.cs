using Elysian.Application.Features.Assistant.Commands;
using Elysian.Application.Features.Congress.Commands;
using Elysian.Application.Features.Financial.Commands;
using Elysian.Application.Features.GoogleCalendar.Commands;
using Elysian.Application.Features.Instagram.Commands;
using Elysian.Domain.Platforms;
using MediatR;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elysian.Application.Features.Platform
{
    /// <summary>
    /// Work that runs on a schedule for each tenant that has the module. A timer queues one message per tenant
    /// and a queue-triggered function runs each on its own, so a slow or failing tenant can't hold up the rest
    /// and each gets the queue's retries.
    /// </summary>
    /// <param name="Module">The module a tenant needs for the job to be queued for it; null runs it for every tenant</param>
    /// <param name="CreateRequest">The request that does the work, sent inside the tenant</param>
    /// <param name="OnRequest">Queued for one tenant by something that asked for it, never for every tenant on a timer</param>
    public record TenantJobDefinition(string Key, string? Module, Func<IBaseRequest> CreateRequest, bool OnRequest = false);

    public static class TenantJobCatalog
    {
        public const string InstagramSync = "instagram-sync";
        public const string InstagramTokenRefresh = "instagram-token-refresh";
        public const string CalendarHealth = "calendar-health";

        /// <summary>The nightly catch-up of banks a webhook missed</summary>
        public const string BankSync = "bank-sync";
        public const string AssistantCleanup = "assistant-cleanup";
        public const string CongressRefresh = "congress-refresh";

        /// <summary>The queued half of deleting a site (see DeleteTenantCommand)</summary>
        public const string DeleteSite = "delete-site";

        public static readonly IReadOnlyList<TenantJobDefinition> All =
        [
            new(InstagramSync, ModuleNames.Instagram, () => new SyncInstagramPostsCommand()),
            new(InstagramTokenRefresh, ModuleNames.Instagram, () => new RefreshInstagramTokenCommand()),
            new(CalendarHealth, ModuleNames.Booking, () => new CheckGoogleCalendarHealthCommand()),
            new(BankSync, ModuleNames.Finance, () => new QueueDueBankSyncsCommand()),
            new(AssistantCleanup, ModuleNames.Assistant, () => new CleanUpAssistantActionsCommand()),
            new(CongressRefresh, ModuleNames.Congress, () => new RefreshTrackedBillsCommand()),
            new(DeleteSite, null, () => new Commands.PurgeSiteCommand(), OnRequest: true),
        ];

        public static TenantJobDefinition? Find(string? key) =>
            All.FirstOrDefault(j => string.Equals(j.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The queue message. "tenant" is lowercase on the wire because the Functions queue trigger exposes a JSON
    /// message's properties as binding data, and hosts resolve the tenant from the one named "tenant".
    /// </summary>
    public record TenantJobMessage(
        [property: JsonPropertyName("tenant")] string Tenant,
        [property: JsonPropertyName("job")] string Job)
    {
        public const string QueueName = "tenant-jobs";

        public string Serialize() => JsonSerializer.Serialize(this);

        public static TenantJobMessage Deserialize(string body) =>
            JsonSerializer.Deserialize<TenantJobMessage>(body) is { Tenant.Length: > 0, Job.Length: > 0 } message
                ? message
                : throw new InvalidOperationException("The tenant job message has no tenant or job.");
    }
}
