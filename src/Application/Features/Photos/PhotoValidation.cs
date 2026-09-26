namespace Elysian.Application.Features.Photos
{
    public static class PhotoValidation
    {
        /// <summary>
        /// URL slug, e.g. "weddings"
        /// </summary>
        public const string CategoryPattern = "^[a-z0-9-]{1,64}$";

        /// <summary>
        /// Spot key defined by the site, e.g. "home-intro" or "portfolio-cover-weddings"
        /// </summary>
        public const string PlacementKeyPattern = "^[a-z0-9-]{1,64}$";
    }
}
