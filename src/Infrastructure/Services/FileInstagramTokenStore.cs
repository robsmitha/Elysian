using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elysian.Application.Interfaces;
using Elysian.Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Keeps the current Instagram token in a local JSON file (outside any repository by default), seeded from
    /// Instagram:AccessToken. If the configured token changes (e.g. regenerated in the Meta dashboard), the store
    /// reseeds from it instead of holding on to the old one.
    /// Only suitable for a single instance: hosts that scale out should register a shared implementation of
    /// <see cref="IInstagramTokenStore"/> (e.g. Azure Blob Storage) in place of this one.
    /// </summary>
    public class FileInstagramTokenStore(IOptions<InstagramSettings> options, TimeProvider timeProvider,
        ILogger<FileInstagramTokenStore> logger) : IInstagramTokenStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly SemaphoreSlim _lock = new(1, 1);

        private StoredToken? _current;

        private string FilePath => string.IsNullOrWhiteSpace(options.Value.TokenFilePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Elysian", "instagram-token.json")
            : options.Value.TokenFilePath;

        public async Task<InstagramToken?> GetAsync(CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                _current ??= await ReadFileAsync(cancellationToken);

                var seed = options.Value.AccessToken;
                if (!string.IsNullOrWhiteSpace(seed) && _current?.SeedFingerprint != Fingerprint(seed))
                {
                    // Age of a configured token is unknown, so count it from now; it's refreshed once that's due
                    logger.LogInformation("Seeding the Instagram token store from {Setting}",
                        $"{InstagramSettings.SectionName}:{nameof(InstagramSettings.AccessToken)}");
                    _current = new StoredToken(seed, timeProvider.GetUtcNow(), Fingerprint(seed));
                    await WriteFileAsync(_current, cancellationToken);
                }

                return _current == null ? null : new InstagramToken(_current.AccessToken, _current.LastRefreshedUtc);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task SaveAsync(InstagramToken token, CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                _current ??= await ReadFileAsync(cancellationToken);

                // Keep the seed fingerprint so a refreshed token isn't mistaken for a changed configuration
                var seedFingerprint = _current?.SeedFingerprint ?? Fingerprint(options.Value.AccessToken);
                _current = new StoredToken(token.AccessToken, token.LastRefreshedUtc, seedFingerprint);
                await WriteFileAsync(_current, cancellationToken);
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task<StoredToken?> ReadFileAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            try
            {
                await using var stream = File.OpenRead(FilePath);
                var stored = await JsonSerializer.DeserializeAsync<StoredToken>(stream, JsonOptions, cancellationToken);
                return string.IsNullOrWhiteSpace(stored?.AccessToken) ? null : stored;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("Instagram token file {Path} could not be read ({Error}); falling back to configuration",
                    FilePath, ex.GetType().Name);
                return null;
            }
        }

        private async Task WriteFileAsync(StoredToken token, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            // Write then swap, so a crash mid-write never leaves a truncated token behind
            var tempPath = FilePath + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, token, JsonOptions, cancellationToken);
            }
            File.Move(tempPath, FilePath, overwrite: true);
        }

        /// <summary>
        /// Identifies a configured token without storing it twice
        /// </summary>
        private static string Fingerprint(string token) =>
            string.IsNullOrEmpty(token) ? string.Empty : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)))[..16];

        private record StoredToken(string AccessToken, DateTimeOffset LastRefreshedUtc, string SeedFingerprint)
        {
            public override string ToString() => $"{nameof(StoredToken)} {{ LastRefreshedUtc = {LastRefreshedUtc:O} }}";
        }
    }
}
