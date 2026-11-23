namespace Elysian.Infrastructure.Settings
{
    /// <summary>
    /// Bound from the "EdgeStore" configuration section: the Cloudflare Workers KV namespace that published site
    /// content and the host map are written to. With any value missing, sites with their own code are
    /// unaffected and publishing a cloud site fails with the reason.
    /// </summary>
    public class EdgeStoreSettings
    {
        public const string SectionName = "EdgeStore";

        /// <summary>
        /// The Cloudflare account the namespace is in
        /// </summary>
        public string? AccountId { get; set; }

        public string? NamespaceId { get; set; }

        /// <summary>
        /// An API token with only Workers KV Storage: Edit, for that account. Keep it in Key Vault, never in
        /// committed config.
        /// </summary>
        public string? ApiToken { get; set; }

        public string ApiBaseUrl { get; set; } = "https://api.cloudflare.com/client/v4/";

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(AccountId) && !string.IsNullOrWhiteSpace(NamespaceId) && !string.IsNullOrWhiteSpace(ApiToken);
    }
}
