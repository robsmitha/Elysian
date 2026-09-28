namespace Elysian.Infrastructure.Settings
{
    /// <summary>
    /// Bound from the "Resend" configuration section
    /// </summary>
    public class ResendSettings
    {
        public const string SectionName = "Resend";

        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Inbox that receives contact form submissions
        /// </summary>
        public string ToEmailAddress { get; set; } = string.Empty;

        /// <summary>
        /// Must be on a domain verified in Resend. The default is Resend's shared test sender,
        /// which can only deliver to the Resend account owner's address.
        /// </summary>
        public string FromEmailAddress { get; set; } = "Website <onboarding@resend.dev>";
    }
}
