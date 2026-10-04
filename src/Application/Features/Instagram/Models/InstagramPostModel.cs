namespace Elysian.Application.Features.Instagram.Models
{
    /// <summary>
    /// Public shape of an Instagram post. Thumbnails follow the portfolio's convention: the browser builds
    /// URLs as <c>{SrcBase}/{width}.webp</c> for each entry in <see cref="Widths"/>.
    /// </summary>
    public record InstagramPostModel(
        string Id,
        string Url,
        string? Caption,
        string MediaType,
        DateTimeOffset? PostedAt,
        int Width,
        int Height,
        string? Placeholder,
        string SrcBase,
        List<int> Widths);

    /// <summary>
    /// Admin view of the tenant's Instagram connection. Never includes the token.
    /// </summary>
    /// <param name="Error">Why the connection isn't working (e.g. an expired token), if Instagram said so</param>
    public record InstagramConnectionModel(
        bool Connected,
        string? Username,
        string? UserId,
        DateTimeOffset? LastRefreshedUtc,
        DateTimeOffset? ExpiresUtc,
        DateTimeOffset? LastSyncedUtc,
        int PostCount,
        string? Error);
}
