namespace Elysian.Infrastructure.Settings
{
    /// <summary>
    /// Bound from the "Turnstile" configuration section. The public site key belongs to the client app, not here.
    /// </summary>
    public class TurnstileSettings
    {
        public const string SectionName = "Turnstile";

        [PlatformOnly]
        public string SecretKey { get; set; } = string.Empty;

        /// <summary>
        /// The secret of the widget that sites drawn with a shared template use. They share one widget, so
        /// they share one secret; a site with its own code uses its own, or <see cref="SecretKey"/>.
        /// </summary>
        [PlatformOnly]
        public string CloudSecretKey { get; set; } = string.Empty;
    }
}
