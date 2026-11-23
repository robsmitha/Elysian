namespace Elysian.Infrastructure.Settings
{
    /// <summary>
    /// The images host shared by every site drawn with a template (a cloud tenant). Section "PlatformImages".
    /// A site with its own code keeps the host named in "PhotoStorageSettings" and "PlacementPublishing".
    /// The same blob containers are behind both; only the public address differs, and so does the Cloudflare
    /// zone that address is purged from after a placement is republished.
    /// </summary>
    public class PlatformImagesSettings
    {
        public const string SectionName = "PlatformImages";

        /// <summary>
        /// e.g. https://images.example.com, with the containers directly under it (/photos, /placements).
        /// Empty: cloud tenants get the same addresses as everyone else.
        /// </summary>
        public string? PublicOrigin { get; set; }

        /// <summary>
        /// The Cloudflare zone <see cref="PublicOrigin"/> is in
        /// </summary>
        public string? CloudflareZoneId { get; set; }

        /// <summary>
        /// API token with only Zone → Cache Purge → Purge on that zone. Keep it in Key Vault.
        /// </summary>
        public string? CloudflareApiToken { get; set; }

        /// <summary>
        /// The origin without a trailing slash, or null when it isn't an https address
        /// </summary>
        public string? Origin =>
            Uri.TryCreate(PublicOrigin?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
                ? uri.GetLeftPart(UriPartial.Authority)
                : null;

        public bool CanPurge => Origin != null && !string.IsNullOrWhiteSpace(CloudflareZoneId) && !string.IsNullOrWhiteSpace(CloudflareApiToken);
    }
}
