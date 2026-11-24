namespace Elysian.Infrastructure.Settings
{
    /// <summary>
    /// How deleting a site behaves. Section "PlatformDeletion".
    /// </summary>
    public class PlatformDeletionSettings
    {
        public const string SectionName = "PlatformDeletion";

        /// <summary>The longest a deleted site is kept. Its storage is paid for all the while.</summary>
        public const int MostDays = 90;

        /// <summary>
        /// How many days a deleted site's photos and data are kept, during which it can be brought back.
        /// 0 removes everything as soon as a site is deleted.
        /// </summary>
        public int GraceDays { get; set; } = 14;

        /// <summary><see cref="GraceDays"/>, kept between 0 and <see cref="MostDays"/></summary>
        public int Days => Math.Clamp(GraceDays, 0, MostDays);
    }
}
