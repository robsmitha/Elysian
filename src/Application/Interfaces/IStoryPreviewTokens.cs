namespace Elysian.Application.Interfaces
{
    /// <summary>
    /// Short-lived tokens that let one story be read in any status without signing in, so a draft can be
    /// previewed on a public site that has no sign-in of its own. A token is good for one story of one tenant
    /// until it expires; it grants nothing else.
    /// </summary>
    public interface IStoryPreviewTokens
    {
        /// <summary>
        /// A new token for this tenant's story, and when it stops working
        /// </summary>
        (string Token, DateTimeOffset ExpiresAt) Issue(string tenantIdentifier, Guid storyId);

        /// <summary>
        /// The story a token is for, or null when the token is malformed, was issued for another tenant, has
        /// been altered, or has expired
        /// </summary>
        Guid? Read(string tenantIdentifier, string? token);
    }
}
