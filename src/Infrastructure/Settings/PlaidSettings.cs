namespace Elysian.Infrastructure.Settings
{
    public class PlaidSettings
    {
        /// <summary>https://sandbox.plaid.com/ locally, https://production.plaid.com/ when deployed</summary>
        public string BaseUrl { get; set; }
        public string ClientId { get; set; }
        public string Secret { get; set; }

        /// <summary>
        /// Where Plaid sends webhooks, e.g. https://robsmitha.com/api/PlaidWebhook?tenant=robsmitha. Empty turns
        /// webhooks off (local development); the nightly sync and "Sync now" still work.
        /// </summary>
        public string? WebhookUrl { get; set; }

        /// <summary>
        /// The OAuth return page registered in the Plaid dashboard (e.g. https://robsmitha.com/plaid-oauth). Optional:
        /// without it, OAuth banks still work in a desktop browser's popup.
        /// </summary>
        public string? RedirectUri { get; set; }

        /// <summary>History requested when a bank is first linked (decision D13: one year)</summary>
        public int DaysRequested { get; set; } = 365;

        /// <summary>
        /// Base64 of a 32-byte key that encrypts access tokens at rest (AES-GCM). Kept in app settings / user secrets.
        /// Without it, new banks can't be linked and stored tokens stay as they are.
        /// </summary>
        public string? TokenEncryptionKey { get; set; }

        /// <summary>A short name for the key, stored with each token so the key can be rotated later</summary>
        public string TokenEncryptionKeyId { get; set; } = "k1";

        /// <summary>Optional earlier keys by id, so tokens encrypted with a rotated-out key can still be read</summary>
        public Dictionary<string, string> PreviousTokenEncryptionKeys { get; set; } = new();

        /// <summary>The Storage queue name for sync jobs</summary>
        public string SyncQueueName { get; set; } = "plaid-sync";

        /// <summary>"Sync now" can run once per bank in this many minutes</summary>
        public int ManualSyncCooldownMinutes { get; set; } = 10;

        /// <summary>And this many times per person per hour, across all their banks</summary>
        public int ManualSyncsPerHour { get; set; } = 6;
    }
}
