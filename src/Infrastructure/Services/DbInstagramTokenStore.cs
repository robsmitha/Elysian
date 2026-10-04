using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Keeps each tenant's Instagram token in the OAuthToken table (tenant-filtered by ElysianContext).
    /// <see cref="OAuthToken.CreatedAt"/> holds when the token was connected or last refreshed, and
    /// <see cref="OAuthToken.UserId"/> holds the Instagram account id.
    /// </summary>
    public class DbInstagramTokenStore(ElysianContext context) : IInstagramTokenStore
    {
        private const string TokenType = "bearer";

        /// <summary>
        /// The only permission the feed needs (Instagram API with Instagram Login)
        /// </summary>
        private const string Scope = "instagram_business_basic";

        public async Task<InstagramToken?> GetAsync(CancellationToken cancellationToken = default)
        {
            var row = await context.OAuthTokens.AsNoTracking()
                .Where(t => t.OAuthProvider == OAuthProviders.Instagram)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            return row == null
                ? null
                : new InstagramToken(row.AccessToken, row.UserId, AsUtc(row.CreatedAt), row.ExpiresAt is DateTime expires ? AsUtc(expires) : null);
        }

        public async Task SaveAsync(InstagramToken token, CancellationToken cancellationToken = default)
        {
            var rows = await context.OAuthTokens
                .Where(t => t.OAuthProvider == OAuthProviders.Instagram)
                .ToListAsync(cancellationToken);

            // One connection per tenant: connecting a different account replaces the old one
            var row = rows.FirstOrDefault(t => t.UserId == token.UserId);
            context.OAuthTokens.RemoveRange(rows.Where(t => t != row));

            if (row == null)
            {
                row = new OAuthToken { OAuthProvider = OAuthProviders.Instagram, UserId = token.UserId };
                context.OAuthTokens.Add(row);
            }

            row.AccessToken = token.AccessToken;
            row.TokenType = TokenType;
            row.Scope = Scope;
            row.CreatedAt = token.LastRefreshedUtc.UtcDateTime;
            row.ExpiresAt = token.ExpiresUtc?.UtcDateTime;

            await context.SaveChangesAsync(cancellationToken);
        }

        private static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
