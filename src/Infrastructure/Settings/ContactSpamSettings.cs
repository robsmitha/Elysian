namespace Elysian.Infrastructure.Settings
{
    /// <summary>
    /// Bound from the "ContactSpam" configuration section. Reading contact form messages for sales pitches:
    /// rules first, then Claude for what the rules let through. The key is Anthropic:ApiKey; without one only
    /// the rules run.
    /// </summary>
    public class ContactSpamSettings
    {
        public const string SectionName = "ContactSpam";

        /// <summary>Off, and every message is delivered unmarked, as before this existed</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Ask Claude about a message the rules passed</summary>
        public bool UseModel { get; set; } = true;

        /// <summary>A yes or no on a short message: the small, fast model is enough, and the visitor is waiting</summary>
        public string Model { get; set; } = "claude-haiku-5-5";

        /// <summary>low, medium, high, xhigh or max. This is a one-line judgement: low.</summary>
        public string Effort { get; set; } = "low";

        /// <summary>
        /// How long the visitor's message waits for Claude's answer before the rules decide alone. One try,
        /// no retries: someone is watching "Sending…".
        /// </summary>
        public int TimeoutSeconds { get; set; } = 5;
    }
}
