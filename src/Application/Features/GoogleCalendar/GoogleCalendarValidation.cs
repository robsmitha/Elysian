using Elysian.Application.Interfaces;

namespace Elysian.Application.Features.GoogleCalendar
{
    public static class GoogleCalendarValidation
    {
        /// <summary>
        /// Records that the stored token just worked. Re-reads first, since checking may have refreshed or rotated it.
        /// </summary>
        public static async Task<GoogleToken?> MarkValidatedAsync(this IGoogleTokenStore tokenStore, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var token = await tokenStore.GetAsync(cancellationToken);
            if (token == null)
            {
                return null;
            }

            token = token with { LastValidatedUtc = now };
            await tokenStore.SaveAsync(token, cancellationToken);
            return token;
        }
    }
}
