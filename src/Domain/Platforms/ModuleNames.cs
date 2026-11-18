namespace Elysian.Domain.Platforms
{
    /// <summary>
    /// A module is a group of features a tenant either has or doesn't. Core features (site content, contact,
    /// users, products) belong to no module and are always available.
    /// </summary>
    public static class ModuleNames
    {
        public const string Photos = "photos";
        public const string Booking = "booking";
        public const string Instagram = "instagram";
        public const string Finance = "finance";
        public const string Assistant = "assistant";
        public const string Congress = "congress";
        public const string Code = "code";

        public static readonly IReadOnlyList<string> All = [Photos, Booking, Instagram, Finance, Assistant, Congress, Code];
    }
}
