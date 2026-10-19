namespace Elysian.Application.Features.Stories
{
    public static class StoryValidation
    {
        /// <summary>
        /// Lowercase words joined by single hyphens, e.g. "emily-and-james-goodwood-museum"
        /// </summary>
        public const string SlugPattern = "^[a-z0-9]+(?:-[a-z0-9]+)*$";

        public const int SlugMaxLength = 128;
        public const int TitleMaxLength = 160;
        public const int DescriptionMaxLength = 2000;
        public const int VenueNameMaxLength = 160;
        public const int CityMaxLength = 100;
        public const int StateMaxLength = 64;
        public const int SeoTitleMaxLength = 70;
        public const int SeoDescriptionMaxLength = 160;
    }
}
