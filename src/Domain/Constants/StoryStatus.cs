namespace Elysian.Domain.Constants
{
    public enum StoryStatus
    {
        /// <summary>
        /// Only admins can see it
        /// </summary>
        Draft,

        /// <summary>
        /// Opens by direct link, but isn't listed or indexed
        /// </summary>
        Unlisted,

        /// <summary>
        /// Listed in its category and indexed
        /// </summary>
        Published
    }
}
