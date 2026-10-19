using Elysian.Application.Exceptions;
using Elysian.Application.Features.Photos.Commands;
using Elysian.Application.Features.Photos.Queries;
using Elysian.Application.Features.Stories;
using Elysian.Application.Features.Stories.Commands;
using Elysian.Application.Features.Stories.Queries;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Elysian.Tests.Stories
{
    public sealed class StoryTests : IDisposable
    {
        private readonly TestHost host = new();
        private readonly Mock<IPhotoStorage> storage = new();

        public StoryTests()
        {
            storage.Setup(s => s.GetPublicUrl(It.IsAny<string>())).Returns<string>(path => $"https://cdn.example.com/{path}");
            storage.Setup(s => s.CreateOriginalUploadUriAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Uri("https://storage.example.com/upload"));
        }

        [Theory]
        [InlineData("Emily & James", "Goodwood Museum", "emily-and-james-goodwood-museum")]
        [InlineData("Zoë's Wedding", null, "zoes-wedding")]
        [InlineData("  The  Rivers   Gala!  ", "", "the-rivers-gala")]
        [InlineData("!!!", null, "story")]
        public void Slug_is_made_from_title_and_venue(string title, string? venue, string expected)
        {
            Assert.Equal(expected, StorySlugs.FromTitle(title, venue));
        }

        [Fact]
        public async Task Slug_gets_a_number_when_taken()
        {
            AddStory("emily-and-james");
            AddStory("emily-and-james-2");
            await host.Context.SaveChangesAsync();

            Assert.Equal("emily-and-james-3", await host.Context.MakeUniqueAsync("emily-and-james", null, default));
            Assert.Equal("maya-and-theo", await host.Context.MakeUniqueAsync("maya-and-theo", null, default));
        }

        [Fact]
        public async Task Create_starts_a_draft_with_a_unique_slug()
        {
            AddStory("emily-and-james");
            await host.Context.SaveChangesAsync();

            var created = await new CreateStoryCommandHandler(host.Context, storage.Object, host.TenantAccessor)
                .Handle(new CreateStoryCommand("weddings", " Emily & James "), default);

            Assert.Equal("Emily & James", created.Title);
            Assert.Equal("emily-and-james-2", created.Slug);
            Assert.Equal(nameof(StoryStatus.Draft), created.Status);
        }

        [Fact]
        public async Task Portfolio_leaves_out_story_photos_unless_they_fill_a_spot()
        {
            var story = AddStory("emily-and-james");
            var gallery = AddPhoto("weddings");
            var inStory = AddPhoto("weddings", story);
            var placed = AddPhoto("weddings", story);
            host.Context.Placements.Add(new Placement { Key = "home-intro", PhotoId = placed.PhotoId });
            await host.Context.SaveChangesAsync();

            var photos = await new GetPortfolioQueryHandler(host.Context, storage.Object, host.TenantAccessor)
                .Handle(new GetPortfolioQuery(), default);

            Assert.Equal(new HashSet<Guid> { gallery.PhotoId, placed.PhotoId }, photos.Select(p => p.Id).ToHashSet());
            Assert.DoesNotContain(photos, p => p.Id == inStory.PhotoId);
            Assert.Equal(story.StoryId, photos.Single(p => p.Id == placed.PhotoId).StoryId);
        }

        [Fact]
        public async Task Public_list_has_only_published_stories_newest_first()
        {
            var older = AddStory("older", StoryStatus.Published, new DateOnly(2025, 6, 7));
            var newer = AddStory("newer", StoryStatus.Published, new DateOnly(2026, 10, 18));
            var undated = AddStory("undated", StoryStatus.Published);
            undated.PublishedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            AddStory("draft", StoryStatus.Draft, new DateOnly(2026, 12, 1));
            AddStory("unlisted", StoryStatus.Unlisted, new DateOnly(2026, 12, 1));
            AddStory("event", StoryStatus.Published, new DateOnly(2026, 12, 1), category: "events");
            await host.Context.SaveChangesAsync();

            var stories = await new GetStoriesQueryHandler(host.Context, storage.Object, host.TenantAccessor)
                .Handle(new GetStoriesQuery("weddings"), default);

            Assert.Equal(["newer", "undated", "older"], stories.Select(s => s.Slug).ToList());
        }

        [Fact]
        public async Task Cover_is_the_hero_else_the_first_ready_photo()
        {
            var story = AddStory("emily-and-james", StoryStatus.Published);
            var pending = AddPhoto("weddings", story, sortOrder: 0, status: PhotoStatus.Processing);
            var first = AddPhoto("weddings", story, sortOrder: 1);
            var hero = AddPhoto("weddings", story, sortOrder: 2);
            await host.Context.SaveChangesAsync();

            var handler = new GetStoriesQueryHandler(host.Context, storage.Object, host.TenantAccessor);
            Assert.Equal(first.PhotoId, (await handler.Handle(new GetStoriesQuery(), default)).Single().Cover!.Id);

            story.HeroPhotoId = hero.PhotoId;
            await host.Context.SaveChangesAsync();
            var summary = (await handler.Handle(new GetStoriesQuery(), default)).Single();
            Assert.Equal(hero.PhotoId, summary.Cover!.Id);
            Assert.Equal(2, summary.PhotoCount);
            Assert.NotEqual(pending.PhotoId, summary.Cover.Id);
        }

        [Fact]
        public async Task Public_page_hides_drafts_but_opens_unlisted_stories()
        {
            AddStory("draft", StoryStatus.Draft);
            AddStory("unlisted", StoryStatus.Unlisted);
            await host.Context.SaveChangesAsync();

            var handler = new GetStoryQueryHandler(host.Context, storage.Object, host.TenantAccessor);
            await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(new GetStoryQuery("draft"), default));
            Assert.Equal("unlisted", (await handler.Handle(new GetStoryQuery("unlisted"), default)).Slug);
        }

        [Fact]
        public async Task Update_clears_blank_fields_and_moves_photos_with_the_category()
        {
            var story = AddStory("rivers-gala");
            story.VenueName = "Hotel Duval";
            var photo = AddPhoto("weddings", story);
            await host.Context.SaveChangesAsync();

            var updated = await new UpdateStoryCommandHandler(host.Context, storage.Object, host.TenantAccessor)
                .Handle(new UpdateStoryCommand(story.StoryId, "rivers-gala", "events", "The Rivers Gala", "  ",
                    new DateOnly(2026, 5, 2), "", "Tallahassee", null, photo.PhotoId, null, null), default);

            Assert.Null(updated.Description);
            Assert.Null(updated.VenueName);
            Assert.Equal("Tallahassee", updated.City);
            Assert.Equal(photo.PhotoId, updated.HeroPhotoId);
            Assert.Equal("events", (await host.Context.Photos.SingleAsync(p => p.PhotoId == photo.PhotoId)).Category);
        }

        [Fact]
        public async Task Update_rejects_a_hero_from_another_story_and_a_taken_slug()
        {
            var story = AddStory("emily-and-james");
            AddStory("maya-and-theo");
            var otherPhoto = AddPhoto("weddings", AddStory("other"));
            await host.Context.SaveChangesAsync();

            var result = await new UpdateStoryCommandValidator(host.Context).ValidateAsync(new UpdateStoryCommand(
                story.StoryId, "maya-and-theo", "weddings", "Emily & James", null, null, null, null, null,
                otherPhoto.PhotoId, null, null));

            Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateStoryCommand.Slug));
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateStoryCommand.HeroPhotoId));
        }

        [Fact]
        public async Task First_publish_sets_published_at_once()
        {
            var story = AddStory("emily-and-james");
            await host.Context.SaveChangesAsync();
            var handler = new SetStoryStatusCommandHandler(host.Context, storage.Object, host.TenantAccessor, TimeProvider.System);

            var published = await handler.Handle(new SetStoryStatusCommand(story.StoryId, "published"), default);
            await handler.Handle(new SetStoryStatusCommand(story.StoryId, "Draft"), default);
            var republished = await handler.Handle(new SetStoryStatusCommand(story.StoryId, "Published"), default);

            Assert.NotNull(published.PublishedAt);
            Assert.Equal(published.PublishedAt, republished.PublishedAt);
        }

        [Fact]
        public async Task Upload_into_a_story_takes_its_category_and_goes_last()
        {
            var story = AddStory("emily-and-james", category: "events");
            AddPhoto("events", story, sortOrder: 4);
            AddPhoto("events", sortOrder: 9);
            await host.Context.SaveChangesAsync();

            var response = await new CreatePhotoUploadCommandHandler(host.Context, storage.Object, host.TenantAccessor)
                .Handle(new CreatePhotoUploadCommand("arrival.jpg", 1000, "", story.StoryId), default);

            var photo = await host.Context.Photos.SingleAsync(p => p.PhotoId == response.PhotoId);
            Assert.Equal("events", photo.Category);
            Assert.Equal(story.StoryId, photo.StoryId);
            Assert.Equal(5, photo.SortOrder);
        }

        [Fact]
        public async Task Deleting_the_hero_photo_clears_the_hero()
        {
            var story = AddStory("emily-and-james");
            var hero = AddPhoto("weddings", story);
            story.HeroPhotoId = hero.PhotoId;
            await host.Context.SaveChangesAsync();

            await new DeletePhotoCommandHandler(host.Context, storage.Object, host.TenantAccessor)
                .Handle(new DeletePhotoCommand(hero.PhotoId), default);

            Assert.Null((await host.Context.Stories.SingleAsync()).HeroPhotoId);
        }

        [Fact]
        public async Task Deleting_a_story_deletes_its_photos_and_their_files()
        {
            var story = AddStory("emily-and-james");
            var photo = AddPhoto("weddings", story);
            var kept = AddPhoto("weddings");
            await host.Context.SaveChangesAsync();

            await new DeleteStoryCommandHandler(host.Context, storage.Object, host.TenantAccessor)
                .Handle(new DeleteStoryCommand(story.StoryId), default);

            Assert.False(await host.Context.Stories.AnyAsync());
            Assert.Equal([kept.PhotoId], await host.Context.Photos.Select(p => p.PhotoId).ToListAsync());
            storage.Verify(s => s.DeleteOriginalAsync(photo.OriginalBlobName, It.IsAny<CancellationToken>()), Times.Once);
            storage.Verify(s => s.DeleteOriginalAsync(kept.OriginalBlobName, It.IsAny<CancellationToken>()), Times.Never);
        }

        private Story AddStory(string slug, StoryStatus status = StoryStatus.Draft, DateOnly? eventDate = null,
            string category = "weddings")
        {
            var story = new Story
            {
                StoryId = Guid.NewGuid(),
                Slug = slug,
                Category = category,
                Title = slug,
                Status = status,
                EventDate = eventDate,
            };
            host.Context.Stories.Add(story);
            return story;
        }

        private Photo AddPhoto(string category, Story? story = null, int sortOrder = 0, PhotoStatus status = PhotoStatus.Ready)
        {
            var photoId = Guid.NewGuid();
            var photo = new Photo
            {
                PhotoId = photoId,
                Category = category,
                StoryId = story?.StoryId,
                SortOrder = sortOrder,
                OriginalFileName = "photo.jpg",
                OriginalBlobName = $"test/{photoId}/original.jpg",
                OriginalFileSize = 1000,
                Width = 4000,
                Height = 6000,
                Version = 1,
                VariantWidths = [400, 800],
                Status = status,
            };
            host.Context.Photos.Add(photo);
            return photo;
        }

        public void Dispose() => host.Dispose();
    }
}
