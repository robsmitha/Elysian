namespace Elysian.Application.Features.Instagram
{
    public static class InstagramPermissions
    {
        /// <summary>
        /// Read the account's profile and posts: the only permission the feed needs, and the one a token must carry
        /// to be refreshed
        /// </summary>
        public const string Basic = "instagram_business_basic";
    }
}
