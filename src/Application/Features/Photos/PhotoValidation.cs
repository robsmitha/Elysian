namespace Elysian.Application.Features.Photos
{
    public static class PhotoValidation
    {
        /// <summary>
        /// URL slug, e.g. "weddings"
        /// </summary>
        public const string CategoryPattern = "^[a-z0-9-]{1,64}$";

        /// <summary>
        /// Placement key used by the frontend, e.g. "aspenPortrait" or "hero-1"
        /// </summary>
        public const string SlotPattern = "^[A-Za-z0-9-]{1,64}$";
    }
}
