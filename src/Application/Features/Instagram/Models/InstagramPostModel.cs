namespace Elysian.Application.Features.Instagram.Models
{
    /// <summary>
    /// Public shape of an Instagram post. <see cref="ImageUrl"/> is the video thumbnail for VIDEO posts
    /// and the media itself for IMAGE and CAROUSEL_ALBUM (the album's first image).
    /// </summary>
    public record InstagramPostModel(
        string Id,
        string ImageUrl,
        string Url,
        string? Caption,
        string MediaType,
        DateTimeOffset? Timestamp);
}
