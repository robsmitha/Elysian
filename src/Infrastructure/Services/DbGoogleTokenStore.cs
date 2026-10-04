using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Keeps each tenant's Google tokens in the OAuthToken table (tenant-filtered by ElysianContext).
    /// <see cref="OAuthToken.RefreshToken"/> holds the long-lived token, <see cref="OAuthToken.AccessToken"/> and
    /// <see cref="OAuthToken.ExpiresAt"/> the cached access token, <see cref="OAuthToken.CreatedAt"/> when the refresh token
    /// was stored or rotated, and <see cref="OAuthToken.UserId"/> the calendar account.
    /// </summary>
    public class DbGoogleTokenStore(ElysianContext context) : IGoogleTokenStore
    {
        private const string TokenType = "Bearer";

        public async Task<GoogleToken?> GetAsync(CancellationToken cancellationToken = default)
        {
            var row = await context.OAuthTokens.AsNoTracking()
                .Where(t => t.OAuthProvider == OAuthProviders.Google && t.RefreshToken != null)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            return row == null
                ? null
                : new GoogleToken(row.RefreshToken!, row.UserId, row.Scope, string.IsNullOrEmpty(row.AccessToken) ? null : row.AccessToken,
                    AsUtc(row.ExpiresAt), AsUtc(row.CreatedAt), AsUtc(row.LastValidatedAt));
        }

        public async Task SaveAsync(GoogleToken token, CancellationToken cancellationToken = default)
        {
            var rows = await context.OAuthTokens
                .Where(t => t.OAuthProvider == OAuthProviders.Google)
                .ToListAsync(cancellationToken);

            // One connection per tenant: connecting a different account replaces the old one
            var row = rows.FirstOrDefault(t => t.UserId == token.AccountId);
            context.OAuthTokens.RemoveRange(rows.Where(t => t != row));

            if (row == null)
            {
                row = new OAuthToken { OAuthProvider = OAuthProviders.Google, UserId = token.AccountId };
                context.OAuthTokens.Add(row);
            }

            row.RefreshToken = token.RefreshToken;
            // AccessToken is a required column; empty means nothing cached yet
            row.AccessToken = token.AccessToken ?? string.Empty;
            row.ExpiresAt = token.AccessTokenExpiresUtc?.UtcDateTime;
            row.TokenType = TokenType;
            row.Scope = token.Scope ?? row.Scope;
            row.CreatedAt = token.ConnectedUtc.UtcDateTime;
            row.LastValidatedAt = token.LastValidatedUtc?.UtcDateTime;

            await context.SaveChangesAsync(cancellationToken);
        }

        private static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

        private static DateTimeOffset? AsUtc(DateTime? value) => value is DateTime v ? AsUtc(v) : null;
    }
}
