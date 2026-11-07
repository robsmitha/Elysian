namespace Elysian.Infrastructure.Settings
{
    /// <summary>The budget assistant (docs/assistant-plan.md §8). App settings use the section names: Assistant__Model etc.</summary>
    public class AssistantSettings
    {
        /// <summary>Off: both endpoints answer 404 and the drawer hides its button</summary>
        public bool Enabled { get; set; }
        /// <summary>"fake" (offline, the default) or "anthropic"</summary>
        public string Provider { get; set; } = "fake";
        public string Model { get; set; } = "claude-opus-5-5";
        /// <summary>low, medium, high, xhigh or max</summary>
        public string Effort { get; set; } = "low";
        public int PerMinute { get; set; } = 5;
        public int PerDay { get; set; } = 30;
        public int TenantPerDay { get; set; } = 300;
        /// <summary>Input plus output tokens per person per calendar month (UTC), failures included</summary>
        public int MonthlyTokens { get; set; } = 2_000_000;
        public int MaxRounds { get; set; } = 5;
        /// <summary>The whole question, every round; the Static Web App proxy gives up at about 45</summary>
        public int TotalSeconds { get; set; } = 35;
        /// <summary>One model call</summary>
        public int CallSeconds { get; set; } = 25;
    }

    public class AnthropicSettings
    {
        /// <summary>Kept in app settings (Anthropic__ApiKey) or user secrets; never in the client</summary>
        public string? ApiKey { get; set; }
    }
}
