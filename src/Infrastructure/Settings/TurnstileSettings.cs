namespace Elysian.Infrastructure.Settings
{
    /// <summary>
    /// Bound from the "Turnstile" configuration section. The public site key belongs to the client app, not here.
    /// </summary>
    public class TurnstileSettings
    {
        public const string SectionName = "Turnstile";

        public string SecretKey { get; set; } = string.Empty;
    }
}
