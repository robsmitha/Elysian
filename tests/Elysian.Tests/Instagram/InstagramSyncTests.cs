using Azure;
using Elysian.Application.Features.Instagram.Commands;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Elysian.Tests.Instagram
{
    public sealed class InstagramSyncTests : IDisposable
    {
        private const string MediaId = "17970841014043744";
        private static readonly string Tenant = TestHost.Tenant.Identifier!;

        private readonly TestHost host = new();
        private readonly Mock<IInstagramService> instagram = new();
        private readonly Mock<IInstagramTokenStore> tokens = new();
        private readonly Mock<IPhotoStorage> storage = new();
        private readonly Mock<IPhotoProcessor> processor = new();
        private readonly MemoryCache cache = new(new MemoryCacheOptions());

        public InstagramSyncTests()
        {
            tokens.Setup(t => t.GetAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new InstagramToken("token", "user-1", DateTimeOffset.UtcNow, null));
            instagram.Setup(i => i.GetRecentMediaAsync("token", It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([new InstagramMedia(MediaId, "https://cdn.instagram.example/a.jpg", "https://instagram.com/p/a", "New caption", "IMAGE", DateTimeOffset.UtcNow)]);
            instagram.Setup(i => i.DownloadImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MemoryStream([1, 2, 3]));
            processor.Setup(p => p.ProcessAsync(It.IsAny<Stream>(), It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessedPhoto(1080, 1350, "data:image/webp;base64,new",
                    [new PhotoVariant(320, 400, [1], "image/webp"), new PhotoVariant(640, 800, [2], "image/webp")]));
        }

        [Fact]
        public async Task Existing_post_with_its_files_is_only_updated()
        {
            await AddPostAsync();
            StoredFiles($"{Tenant}/instagram/{MediaId}/320.webp", $"{Tenant}/instagram/{MediaId}/640.webp");

            var result = await Sync();

            Assert.Equal(InstagramSyncStatus.Synced, result.Status);
            Assert.Equal(1, result.Updated);
            Assert.Equal(0, result.Repaired);
            instagram.Verify(i => i.DownloadImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.Equal("New caption", (await host.Context.InstagramPosts.SingleAsync()).Caption);
        }

        [Fact]
        public async Task Existing_post_with_missing_files_gets_them_rebuilt()
        {
            await AddPostAsync();
            StoredFiles($"{Tenant}/instagram/{MediaId}/320.webp"); // 640 is gone

            var result = await Sync();

            Assert.Equal(1, result.Repaired);
            Assert.Equal(0, result.Failed);
            storage.Verify(s => s.UploadVariantAsync($"{Tenant}/instagram/{MediaId}/320.webp", It.IsAny<byte[]>(), "image/webp", It.IsAny<CancellationToken>()));
            storage.Verify(s => s.UploadVariantAsync($"{Tenant}/instagram/{MediaId}/640.webp", It.IsAny<byte[]>(), "image/webp", It.IsAny<CancellationToken>()));

            var post = await host.Context.InstagramPosts.SingleAsync();
            Assert.Equal([320, 640], post.VariantWidths);
            Assert.Equal("data:image/webp;base64,new", post.Placeholder);
            Assert.Equal(1080, post.Width);
        }

        [Fact]
        public async Task Nothing_is_rebuilt_when_storage_cannot_be_listed()
        {
            await AddPostAsync();
            storage.Setup(s => s.ListVariantsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(403, "denied"));

            var result = await Sync();

            Assert.Equal(InstagramSyncStatus.Synced, result.Status);
            Assert.Equal(0, result.Repaired);
            instagram.Verify(i => i.DownloadImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task A_failed_rebuild_keeps_the_post_and_is_retried_next_run()
        {
            await AddPostAsync();
            StoredFiles();
            storage.Setup(s => s.UploadVariantAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestFailedException(500, "storage down"));

            var result = await Sync();

            Assert.Equal(InstagramSyncStatus.Synced, result.Status);
            Assert.Equal(0, result.Repaired);
            Assert.Equal(1, result.Failed);
            var post = await host.Context.InstagramPosts.SingleAsync();
            Assert.Equal("data:image/webp;base64,old", post.Placeholder);
        }

        private void StoredFiles(params string[] names) =>
            storage.Setup(s => s.ListVariantsAsync($"{Tenant}/instagram/", It.IsAny<CancellationToken>()))
                .ReturnsAsync(names.ToHashSet());

        private async Task AddPostAsync()
        {
            host.Context.InstagramPosts.Add(new InstagramPost
            {
                MediaId = MediaId,
                Permalink = "https://instagram.com/p/a",
                Caption = "Old caption",
                MediaType = "IMAGE",
                Width = 1080,
                Height = 1080,
                Placeholder = "data:image/webp;base64,old",
                VariantWidths = [320, 640],
                SyncedAt = DateTimeOffset.UtcNow.AddDays(-1),
            });
            await host.Context.SaveChangesAsync();
        }

        private Task<InstagramSyncResult> Sync() =>
            new SyncInstagramPostsCommandHandler(host.Context, instagram.Object, tokens.Object, storage.Object, processor.Object,
                    cache, Options.Create(new InstagramSettings()), host.TenantAccessor, TimeProvider.System,
                    NullLogger<SyncInstagramPostsCommandHandler>.Instance)
                .Handle(new SyncInstagramPostsCommand(), default);

        public void Dispose()
        {
            cache.Dispose();
            host.Dispose();
        }
    }
}
