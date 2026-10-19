using Elysian.Application.Features.Photos.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;

namespace Elysian.Application.Features.Stories.Models
{
    /// <summary>
    /// A story in its category's public list. <see cref="Cover"/> is the hero photo, else the first ready photo.
    /// </summary>
    public record StorySummaryModel(
        Guid Id,
        string Slug,
        string Category,
        string Title,
        string? Description,
        DateOnly? EventDate,
        string? VenueName,
        string? City,
        string? State,
        PortfolioPhotoModel? Cover,
        int PhotoCount);

    /// <summary>
    /// A story's public page: hero details plus every ready photo in order
    /// </summary>
    public record StoryModel(
        Guid Id,
        string Slug,
        string Category,
        string Title,
        string? Description,
        DateOnly? EventDate,
        string? VenueName,
        string? City,
        string? State,
        string Status,
        string? SeoTitle,
        string? SeoDescription,
        PortfolioPhotoModel? Hero,
        List<PortfolioPhotoModel> Photos);

    public record ManagedStorySummaryModel(
        Guid Id,
        string Slug,
        string Category,
        string Title,
        DateOnly? EventDate,
        string? VenueName,
        string? City,
        string? State,
        string Status,
        DateTimeOffset? PublishedAt,
        DateTimeOffset ModifiedAt,
        PhotoModel? Cover,
        int PhotoCount);

    /// <summary>
    /// Everything the admin editor needs, photos in any processing state included
    /// </summary>
    public record ManagedStoryModel(
        Guid Id,
        string Slug,
        string Category,
        string Title,
        string? Description,
        DateOnly? EventDate,
        string? VenueName,
        string? City,
        string? State,
        Guid? HeroPhotoId,
        string Status,
        DateTimeOffset? PublishedAt,
        string? SeoTitle,
        string? SeoDescription,
        List<PhotoModel> Photos);

    public static class StoryModelMappings
    {
        /// <summary>
        /// The story's hero, else its first photo; <paramref name="photos"/> must already be in story order
        /// </summary>
        public static Photo? CoverFrom(this Story story, IReadOnlyList<Photo> photos) =>
            photos.FirstOrDefault(p => p.PhotoId == story.HeroPhotoId) ?? photos.FirstOrDefault();

        public static IEnumerable<Photo> InStoryOrder(this IEnumerable<Photo> photos) =>
            photos.OrderBy(p => p.SortOrder).ThenBy(p => p.CreatedAt);

        public static List<Photo> ReadyPhotos(this Story story) =>
            story.Photos.Where(p => p.Status == PhotoStatus.Ready).InStoryOrder().ToList();

        public static StorySummaryModel ToSummaryModel(this Story story, IPhotoStorage photoStorage, string tenantIdentifier,
            ILookup<Guid, string> placements)
        {
            var photos = story.ReadyPhotos();
            return new(story.StoryId, story.Slug, story.Category, story.Title, story.Description, story.EventDate,
                story.VenueName, story.City, story.State,
                story.CoverFrom(photos)?.ToPortfolioModel(photoStorage, tenantIdentifier, placements), photos.Count);
        }

        public static StoryModel ToModel(this Story story, IPhotoStorage photoStorage, string tenantIdentifier,
            ILookup<Guid, string> placements)
        {
            var photos = story.ReadyPhotos();
            return new(story.StoryId, story.Slug, story.Category, story.Title, story.Description, story.EventDate,
                story.VenueName, story.City, story.State, story.Status.ToString(), story.SeoTitle, story.SeoDescription,
                story.CoverFrom(photos)?.ToPortfolioModel(photoStorage, tenantIdentifier, placements),
                photos.Select(p => p.ToPortfolioModel(photoStorage, tenantIdentifier, placements)).ToList());
        }

        public static ManagedStorySummaryModel ToManagedSummaryModel(this Story story, IPhotoStorage photoStorage,
            string tenantIdentifier, ILookup<Guid, string> placements)
        {
            var photos = story.Photos.InStoryOrder().ToList();
            var cover = story.CoverFrom(photos.Where(p => p.Status == PhotoStatus.Ready).ToList());
            return new(story.StoryId, story.Slug, story.Category, story.Title, story.EventDate, story.VenueName,
                story.City, story.State, story.Status.ToString(), story.PublishedAt, story.ModifiedAt,
                cover?.ToModel(photoStorage, tenantIdentifier, placements), photos.Count);
        }

        public static ManagedStoryModel ToManagedModel(this Story story, IPhotoStorage photoStorage, string tenantIdentifier,
            ILookup<Guid, string> placements) =>
            new(story.StoryId, story.Slug, story.Category, story.Title, story.Description, story.EventDate,
                story.VenueName, story.City, story.State, story.HeroPhotoId, story.Status.ToString(), story.PublishedAt,
                story.SeoTitle, story.SeoDescription,
                story.Photos.InStoryOrder().Select(p => p.ToModel(photoStorage, tenantIdentifier, placements)).ToList());
    }
}
