using Elysian.Application.Exceptions;
using Elysian.Application.Features.Stories.Commands;
using Elysian.Application.Features.Stories.Queries;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Services;
using Elysian.Infrastructure.Settings;
using Microsoft.Extensions.Options;
using Moq;

namespace Elysian.Tests.Stories
{
    public sealed class StoryPreviewTests : IDisposable
    {
        private static readonly string Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
        private static readonly string AnotherKey = Convert.ToBase64String(Enumerable.Range(2, 32).Select(i => (byte)i).ToArray());

        private readonly TestHost host = new();
        private readonly Mock<IPhotoStorage> storage = new();
        private readonly Clock clock = new() { Now = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero) };

        public StoryPreviewTests()
        {
            storage.Setup(s => s.GetPublicUrl(It.IsAny<string>())).Returns<string>(path => $"https://cdn.example.com/{path}");
        }

        [Fact]
        public async Task A_link_opens_a_draft_in_the_public_shape_for_an_hour()
        {
            var story = AddStory("emily-and-james", StoryStatus.Draft);
            await host.Context.SaveChangesAsync();

            var link = await new CreateStoryPreviewLinkCommandHandler(host.Context, Tokens(), host.TenantAccessor)
                .Handle(new CreateStoryPreviewLinkCommand(story.StoryId), default);

            Assert.Equal(clock.Now.AddMinutes(60), link.ExpiresAt);

            var preview = await Preview(link.Token);
            Assert.Equal(story.StoryId, preview.Id);
            Assert.Equal(nameof(StoryStatus.Draft), preview.Status);

            clock.Now = link.ExpiresAt.AddSeconds(-1);
            Assert.Equal(story.StoryId, (await Preview(link.Token)).Id);

            clock.Now = link.ExpiresAt;
            await Assert.ThrowsAsync<NotFoundException>(() => Preview(link.Token));
        }

        [Fact]
        public async Task A_link_is_only_made_for_a_story_that_exists()
        {
            await Assert.ThrowsAsync<NotFoundException>(() =>
                new CreateStoryPreviewLinkCommandHandler(host.Context, Tokens(), host.TenantAccessor)
                    .Handle(new CreateStoryPreviewLinkCommand(Guid.NewGuid()), default));
        }

        [Fact]
        public async Task A_link_stops_working_when_its_story_is_deleted()
        {
            var story = AddStory("emily-and-james", StoryStatus.Draft);
            await host.Context.SaveChangesAsync();
            var (token, _) = Tokens().Issue(TestHost.Tenant.Identifier!, story.StoryId);

            host.Context.Stories.Remove(story);
            await host.Context.SaveChangesAsync();

            await Assert.ThrowsAsync<NotFoundException>(() => Preview(token));
        }

        [Fact]
        public void A_token_is_good_only_for_its_own_tenant_story_and_key()
        {
            var storyId = Guid.NewGuid();
            var tokens = Tokens();
            var (token, _) = tokens.Issue("aspen", storyId);

            Assert.Equal(storyId, tokens.Read("aspen", token));
            Assert.Null(tokens.Read("another-tenant", token));
            Assert.Null(Tokens(AnotherKey).Read("aspen", token));

            // The payload of one token with the signature of another
            var (other, _) = tokens.Issue("aspen", Guid.NewGuid());
            Assert.Null(tokens.Read("aspen", $"{other.Split('.')[0]}.{token.Split('.')[1]}"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-a-token")]
        [InlineData("a.b")]
        [InlineData("a.b.c")]
        [InlineData("!!!.???")]
        public void Anything_that_is_not_a_token_is_refused(string? token)
        {
            Assert.Null(Tokens().Read("aspen", token));
        }

        [Theory]
        [InlineData(null, "is not configured")]
        [InlineData("not base64!", "must be base64")]
        [InlineData("c2hvcnQ=", "at least 32 bytes")]
        public void A_missing_or_weak_key_fails_with_the_setting_named(string? key, string expected)
        {
            var issue = Assert.Throws<InvalidOperationException>(() => Tokens(key).Issue("aspen", Guid.NewGuid()));
            var read = Assert.Throws<InvalidOperationException>(() => Tokens(key).Read("aspen", "a.b"));

            Assert.Contains("StoryPreview:SigningKey", issue.Message);
            Assert.Contains(expected, issue.Message);
            Assert.Contains(expected, read.Message);
        }

        private Task<Application.Features.Stories.Models.StoryModel> Preview(string token) =>
            new GetStoryPreviewQueryHandler(host.Context, storage.Object, Tokens(), host.TenantAccessor)
                .Handle(new GetStoryPreviewQuery(token), default);

        private HmacStoryPreviewTokens Tokens(string? key = "default") =>
            new(Options.Create(new StoryPreviewSettings { SigningKey = key == "default" ? Key : key }), clock);

        private Story AddStory(string slug, StoryStatus status)
        {
            var story = new Story { StoryId = Guid.NewGuid(), Slug = slug, Category = "weddings", Title = slug, Status = status };
            host.Context.Stories.Add(story);
            return story;
        }

        private sealed class Clock : TimeProvider
        {
            public DateTimeOffset Now { get; set; }
            public override DateTimeOffset GetUtcNow() => Now;
        }

        public void Dispose() => host.Dispose();
    }
}
