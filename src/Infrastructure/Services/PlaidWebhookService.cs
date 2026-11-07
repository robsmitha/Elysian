using System.Text;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Handles a Plaid webhook quickly: verify it, record it once, update the bank's status, and queue a sync when there's
    /// new data. The sync itself runs from the queue, so Plaid gets its 200 within its timeout.
    /// </summary>
    public class PlaidWebhookService(ElysianContext context, PlaidWebhookVerifier verifier, IPlaidSyncQueue syncQueue,
        TimeProvider timeProvider, ILogger<PlaidWebhookService> logger) : IPlaidWebhookService
    {
        public async Task<WebhookResult> HandleAsync(byte[] body, string? verificationJwt, CancellationToken cancellationToken = default)
        {
            if (!await verifier.VerifyAsync(verificationJwt, body))
            {
                return WebhookResult.Rejected;
            }

            var hash = PlaidWebhookVerifier.Sha256Hex(body);
            if (await context.PlaidWebhookEvents.AnyAsync(e => e.BodySha256 == hash, cancellationToken))
            {
                return WebhookResult.Duplicate;
            }

            var json = Encoding.UTF8.GetString(body);
            JObject payload;
            try
            {
                payload = JObject.Parse(json);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                logger.LogWarning("Webhook body isn't JSON");
                return WebhookResult.Rejected;
            }

            var type = payload["webhook_type"]?.Value<string>() ?? "UNKNOWN";
            var code = payload["webhook_code"]?.Value<string>() ?? "UNKNOWN";
            var itemId = payload["item_id"]?.Value<string>();
            var record = new PlaidWebhookEvent
            {
                BodySha256 = hash,
                WebhookType = Clip(type, 50),
                WebhookCode = Clip(code, 50),
                ItemId = itemId,
                PayloadJson = json,
                ReceivedAt = timeProvider.GetUtcNow()
            };
            context.PlaidWebhookEvents.Add(record);

            try
            {
                record.Error = await RouteAsync(type, code, itemId, payload, cancellationToken);
                record.ProcessedAt = timeProvider.GetUtcNow();
            }
            catch (Exception ex)
            {
                // Recorded and acknowledged anyway: a retry from Plaid wouldn't go better, and the nightly sync catches up
                logger.LogError(ex, "Webhook {Type}/{Code} for item {Item} failed", type, code, itemId);
                record.Error = Clip(ex.Message, 500);
            }

            await context.SaveChangesAsync(cancellationToken);
            return WebhookResult.Accepted;
        }

        /// <summary>Acts on one webhook. Returns a note to store when it couldn't be applied.</summary>
        private async Task<string?> RouteAsync(string type, string code, string? itemId, JObject payload, CancellationToken cancellationToken)
        {
            var item = itemId == null ? null : await context.InstitutionAccessItems.FirstOrDefaultAsync(i => i.ItemId == itemId && i.RemovedAt == null, cancellationToken);
            if (item == null)
            {
                return "No linked bank with this item id";
            }

            switch (type, code)
            {
                case ("TRANSACTIONS", "SYNC_UPDATES_AVAILABLE"):
                    await syncQueue.EnqueueAsync(item.InstitutionAccessItemId, SyncTrigger.Webhook, cancellationToken);
                    break;

                case ("TRANSACTIONS", _):
                    // INITIAL_UPDATE, HISTORICAL_UPDATE, DEFAULT_UPDATE: SYNC_UPDATES_AVAILABLE covers them
                    break;

                case ("ITEM", "ERROR"):
                    var errorCode = payload["error"]?["error_code"]?.Value<string>();
                    item.ErrorCode = errorCode;
                    item.Status = errorCode is "ITEM_NOT_FOUND" or "USER_ACCOUNT_REVOKED" ? ItemStatus.Disconnected
                        : Application.Features.Financial.AccessItemLoader.IsLoginRequired(errorCode) ? ItemStatus.LoginRequired
                        : ItemStatus.Error;
                    break;

                case ("ITEM", "LOGIN_REPAIRED"):
                    item.Status = ItemStatus.Healthy;
                    item.ErrorCode = null;
                    await syncQueue.EnqueueAsync(item.InstitutionAccessItemId, SyncTrigger.Webhook, cancellationToken);
                    break;

                case ("ITEM", "PENDING_EXPIRATION"):
                case ("ITEM", "PENDING_DISCONNECT"):
                    item.Status = ItemStatus.PendingExpiration;
                    item.ErrorCode = code;
                    if (DateTimeOffset.TryParse(payload["consent_expiration_time"]?.Value<string>(), out var end))
                    {
                        item.ConsentExpiresAt = end;
                    }
                    break;

                case ("ITEM", "USER_PERMISSION_REVOKED"):
                case ("ITEM", "USER_ACCOUNT_REVOKED"):
                    item.Status = ItemStatus.Disconnected;
                    item.ErrorCode = code;
                    break;

                case ("ITEM", "NEW_ACCOUNTS_AVAILABLE"):
                    item.NewAccountsAvailable = true;
                    break;

                case ("ITEM", "WEBHOOK_UPDATE_ACKNOWLEDGED"):
                    item.WebhookUrl = payload["new_webhook_url"]?.Value<string>() ?? item.WebhookUrl;
                    break;

                default:
                    logger.LogInformation("Webhook {Type}/{Code} needs no action", type, code);
                    break;
            }
            return null;
        }

        private static string Clip(string value, int max) => value.Length <= max ? value : value[..max];
    }
}
