using System.Globalization;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial;
using Elysian.Application.Features.Financial.Sync;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Domain.Responses.Plaid;
using Elysian.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Brings one bank up to date with Plaid's /transactions/sync and stores the result. Safe to run twice: rows are
    /// matched on Plaid's transaction id, and the cursor only moves after everything from a pass is saved.
    /// </summary>
    public class TransactionSyncService(ElysianContext context, IFinancialService plaid, IAccessTokenService accessTokenService,
        TimeProvider timeProvider, ILogger<TransactionSyncService> logger) : ITransactionSyncService
    {
        // Plaid asks for the whole pass to restart from the first cursor when data changes mid-pagination
        private const int MaxPassAttempts = 3;
        private const int MaxPagesPerPass = 500;
        // SQL Server allows about 2,100 parameters per query
        private const int IdBatchSize = 1000;

        private static readonly HashSet<string> TemporaryTypes = new(StringComparer.OrdinalIgnoreCase) { "API_ERROR", "INSTITUTION_ERROR", "RATE_LIMIT_EXCEEDED" };
        private static readonly HashSet<string> DisconnectedCodes = new(StringComparer.OrdinalIgnoreCase) { "ITEM_NOT_FOUND", "USER_ACCOUNT_REVOKED" };

        public async Task<SyncOutcome> SyncAsync(int institutionAccessItemId, SyncTrigger trigger, CancellationToken cancellationToken = default)
        {
            var item = await context.InstitutionAccessItems
                .Include(i => i.Accounts)
                .FirstOrDefaultAsync(i => i.InstitutionAccessItemId == institutionAccessItemId, cancellationToken);
            if (item == null || item.RemovedAt != null)
            {
                return SyncOutcome.Skip("not_found");
            }
            if (item.Status == ItemStatus.Disconnected)
            {
                return SyncOutcome.Skip("disconnected");
            }

            var token = await accessTokenService.GetAccessTokenForSyncAsync(institutionAccessItemId);
            if (token?.AccessToken == null)
            {
                return SyncOutcome.Skip("no_token");
            }

            var now = timeProvider.GetUtcNow();
            var run = new SyncRun { InstitutionAccessItemId = item.InstitutionAccessItemId, Trigger = trigger, StartedAt = now };

            try
            {
                await EnsureWebhookAsync(item, token.AccessToken);
                await EnsureInstitutionDetailsAsync(item);

                var pass = await ReadChangesAsync(token.AccessToken, item.TransactionsCursor, cancellationToken);
                var counts = await ApplyAsync(item, pass, now, cancellationToken);

                item.TransactionsCursor = pass.Cursor;
                item.LastSyncedAt = now;
                if (item.Status is ItemStatus.LoginRequired or ItemStatus.Error)
                {
                    item.Status = ItemStatus.Healthy;
                }
                if (item.Status == ItemStatus.Healthy)
                {
                    item.ErrorCode = null;
                }

                (run.Added, run.Modified, run.Removed) = counts;
                run.FinishedAt = timeProvider.GetUtcNow();
                context.SyncRuns.Add(run);
                await context.SaveChangesAsync(cancellationToken);

                logger.LogInformation("Synced item {Item} ({Trigger}): +{Added} ~{Modified} -{Removed}",
                    item.InstitutionAccessItemId, trigger, counts.Added, counts.Modified, counts.Removed);
                return new SyncOutcome(true, counts.Added, counts.Modified, counts.Removed);
            }
            catch (FinancialServiceException fex)
            {
                var code = fex.Error?.error_code ?? "UNKNOWN";
                var type = fex.Error?.error_type;
                var retry = code == "PRODUCT_NOT_READY" || (type != null && TemporaryTypes.Contains(type));

                // Nothing from this pass is kept; only the status and the record of the attempt
                DetachPendingChanges(item);
                if (!retry)
                {
                    ApplyItemError(item, code);
                }
                run.ErrorCode = code;
                run.FinishedAt = timeProvider.GetUtcNow();
                context.SyncRuns.Add(run);
                await context.SaveChangesAsync(cancellationToken);

                logger.LogWarning("Sync of item {Item} ({Trigger}) failed: {Type}/{Code}", item.InstitutionAccessItemId, trigger, type, code);
                return new SyncOutcome(false, ErrorCode: code, Retry: retry);
            }
        }

        public async Task<List<int>> GetItemsDueForSyncAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
        {
            var now = timeProvider.GetUtcNow();
            var cutoff = now - olderThan;
            var active = await context.InstitutionAccessItems
                .Where(i => i.RemovedAt == null && i.Status != ItemStatus.Disconnected)
                .ToListAsync(cancellationToken);

            // Banks whose consent runs out within two weeks show a "renew" note
            foreach (var item in active.Where(i => i.Status == ItemStatus.Healthy && i.ConsentExpiresAt is { } end && end <= now.AddDays(14)))
            {
                item.Status = ItemStatus.PendingExpiration;
            }
            await context.SaveChangesAsync(cancellationToken);

            return active
                .Where(i => i.Status != ItemStatus.LoginRequired && (i.LastSyncedAt == null || i.LastSyncedAt < cutoff))
                .Select(i => i.InstitutionAccessItemId)
                .ToList();
        }

        /// <summary>Everything since the stored cursor, read in full before anything is written.</summary>
        internal sealed record SyncPass(string Cursor, List<Account> Accounts, List<TransactionResponse.Transaction> Upserts, List<string> Removed);

        private async Task<SyncPass> ReadChangesAsync(string accessToken, string? startCursor, CancellationToken cancellationToken)
        {
            for (var attempt = 1; ; attempt++)
            {
                var cursor = startCursor;
                var accounts = new List<Account>();
                var upserts = new List<TransactionResponse.Transaction>();
                var removed = new List<string>();
                try
                {
                    for (var page = 0; page < MaxPagesPerPass; page++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var response = await plaid.SyncTransactionsAsync(accessToken, cursor);
                        upserts.AddRange(response.added ?? []);
                        upserts.AddRange(response.modified ?? []);
                        removed.AddRange((response.removed ?? []).Select(r => r.transaction_id));
                        if (response.accounts?.Count > 0)
                        {
                            accounts = response.accounts;
                        }
                        cursor = response.next_cursor;
                        if (!response.has_more)
                        {
                            break;
                        }
                    }
                    return new SyncPass(cursor ?? startCursor ?? "", accounts, upserts, removed);
                }
                catch (FinancialServiceException fex) when (fex.Error?.error_code == "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION" && attempt < MaxPassAttempts)
                {
                    logger.LogInformation("Plaid data changed during sync; restarting the pass ({Attempt})", attempt);
                }
            }
        }

        private async Task<(int Added, int Modified, int Removed)> ApplyAsync(InstitutionAccessItem item, SyncPass pass, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var accounts = UpsertAccounts(item, pass.Accounts, now);

            // A transaction can appear more than once in a pass (added, then modified); the last version wins
            var upserts = pass.Upserts.GroupBy(t => t.transaction_id).Select(g => g.Last()).ToList();
            var removedIds = pass.Removed.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
            var existing = await LoadExistingAsync(upserts.Select(t => t.transaction_id).Concat(removedIds), cancellationToken);

            int added = 0, modified = 0, removed = 0;
            foreach (var t in upserts)
            {
                if (!accounts.TryGetValue(t.account_id, out var account))
                {
                    logger.LogWarning("Skipped transaction {Id}: account {Account} isn't shared with this app", t.transaction_id, t.account_id);
                    continue;
                }

                if (existing.TryGetValue(t.transaction_id, out var row))
                {
                    modified++;
                }
                else
                {
                    row = new BankTransaction { PlaidTransactionId = t.transaction_id };
                    context.BankTransactions.Add(row);
                    existing[t.transaction_id] = row;
                    added++;
                }
                Fill(row, t, account, now);
            }

            foreach (var id in removedIds)
            {
                if (existing.TryGetValue(id, out var row) && row.RemovedAt == null)
                {
                    row.RemovedAt = now;
                    removed++;
                }
            }

            await CarryOverPendingEditsAsync(upserts, cancellationToken);
            return (added, modified, removed);
        }

        private Dictionary<string, FinancialAccount> UpsertAccounts(InstitutionAccessItem item, List<Account> plaidAccounts, DateTimeOffset now)
        {
            item.Accounts ??= [];
            var byPlaidId = item.Accounts.ToDictionary(a => a.PlaidAccountId);
            foreach (var a in plaidAccounts.Where(a => !string.IsNullOrEmpty(a.account_id)))
            {
                if (!byPlaidId.TryGetValue(a.account_id, out var account))
                {
                    account = new FinancialAccount
                    {
                        PlaidAccountId = a.account_id,
                        BudgetRole = FinancialAccount.DefaultRole(a.type, a.subtype),
                        InstitutionAccessItem = item
                    };
                    item.Accounts.Add(account);
                    byPlaidId[a.account_id] = account;
                }
                account.Name = Clip(a.name, 200) ?? "Account";
                account.OfficialName = Clip(a.official_name, 200);
                account.Mask = Clip(a.mask, 10);
                account.Type = Clip(a.type, 40) ?? "other";
                account.Subtype = Clip(a.subtype, 60);
                account.CurrentBalance = Money(a.balances?.current);
                account.AvailableBalance = Money(a.balances?.available);
                account.CreditLimit = Money(a.balances?.limit);
                account.IsoCurrencyCode = Clip(a.balances?.iso_currency_code, 10);
                account.BalanceAsOf = now;
            }
            return byPlaidId;
        }

        private async Task<Dictionary<string, BankTransaction>> LoadExistingAsync(IEnumerable<string> ids, CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, BankTransaction>();
            foreach (var batch in ids.Distinct().Chunk(IdBatchSize))
            {
                var rows = await context.BankTransactions.Where(t => batch.Contains(t.PlaidTransactionId)).ToListAsync(cancellationToken);
                foreach (var row in rows)
                {
                    result[row.PlaidTransactionId] = row;
                }
            }
            return result;
        }

        /// <summary>
        /// When a pending charge posts, Plaid gives it a new id. Categories and exclusions set on the pending one move to
        /// the posted one, so nothing has to be done twice.
        /// </summary>
        private async Task CarryOverPendingEditsAsync(List<TransactionResponse.Transaction> upserts, CancellationToken cancellationToken)
        {
            var moves = upserts
                .Where(t => !t.pending && !string.IsNullOrEmpty(t.pending_transaction_id))
                .GroupBy(t => t.pending_transaction_id!)
                .ToDictionary(g => g.Key, g => g.Last().transaction_id);
            if (moves.Count == 0)
            {
                return;
            }

            foreach (var batch in moves.Keys.Chunk(IdBatchSize))
            {
                var categories = await context.TransactionCategories.Where(c => batch.Contains(c.TransactionId)).ToListAsync(cancellationToken);
                foreach (var category in categories)
                {
                    var postedId = moves[category.TransactionId];
                    var alreadySet = await context.TransactionCategories.AnyAsync(c => c.BudgetId == category.BudgetId && c.TransactionId == postedId, cancellationToken);
                    if (alreadySet)
                    {
                        context.TransactionCategories.Remove(category);
                    }
                    else
                    {
                        category.TransactionId = postedId;
                    }
                }

                var exclusions = await context.BudgetExcludedTransactions.Where(e => batch.Contains(e.TransactionId)).ToListAsync(cancellationToken);
                foreach (var exclusion in exclusions)
                {
                    var postedId = moves[exclusion.TransactionId];
                    var alreadySet = await context.BudgetExcludedTransactions.AnyAsync(e => e.BudgetId == exclusion.BudgetId && e.TransactionId == postedId, cancellationToken);
                    if (alreadySet)
                    {
                        context.BudgetExcludedTransactions.Remove(exclusion);
                    }
                    else
                    {
                        exclusion.TransactionId = postedId;
                    }
                }
            }
        }

        private static void Fill(BankTransaction row, TransactionResponse.Transaction t, FinancialAccount account, DateTimeOffset now)
        {
            var date = ParseDate(t.date) ?? DateOnly.FromDateTime(now.UtcDateTime);
            var authorized = ParseDate(t.authorized_date);

            row.FinancialAccount = account;
            row.PendingTransactionId = Clip(t.pending_transaction_id, 200);
            row.Amount = Money(t.amount) ?? 0;
            row.Date = date;
            row.AuthorizedDate = authorized;
            row.BudgetDate = authorized ?? date;
            row.Name = Clip(t.name, 500) ?? Clip(t.merchant_name, 500) ?? "Transaction";
            row.MerchantName = Clip(t.merchant_name, 200);
            row.MerchantEntityId = Clip(t.merchant_entity_id, 100);
            row.PfcPrimary = Clip(t.personal_finance_category?.primary, 100);
            row.PfcDetailed = Clip(t.personal_finance_category?.detailed, 100);
            row.PfcConfidence = Clip(t.personal_finance_category?.confidence_level, 20);
            row.PaymentChannel = Clip(t.payment_channel, 20);
            row.TransactionType = Clip(t.transaction_type, 20);
            row.IsoCurrencyCode = Clip(t.iso_currency_code, 10);
            row.Pending = t.pending;
            row.SyncedAt = now;
            // Back again after a removal (rare): it counts again
            row.RemovedAt = null;
        }

        private async Task EnsureWebhookAsync(InstitutionAccessItem item, string accessToken)
        {
            var url = plaid.WebhookUrl;
            if (url == null || item.WebhookUrl == url)
            {
                return;
            }
            // Banks linked before webhooks (or before the URL changed) are pointed at the current URL once
            await plaid.UpdateItemWebhookAsync(accessToken, url);
            item.WebhookUrl = url;
        }

        private async Task EnsureInstitutionDetailsAsync(InstitutionAccessItem item)
        {
            if (!string.IsNullOrEmpty(item.InstitutionName))
            {
                return;
            }
            var institution = await AccessItemLoader.GetInstitutionAsync(plaid, item.InstitutionId);
            item.InstitutionName = Clip(institution.name, 200);
            item.InstitutionLogo = institution.logo;
            item.InstitutionColor = institution.primary_color;
        }

        private static void ApplyItemError(InstitutionAccessItem item, string code)
        {
            item.ErrorCode = code;
            if (code == "PENDING_EXPIRATION" || code == "PENDING_DISCONNECT")
            {
                item.Status = ItemStatus.PendingExpiration;
            }
            else if (DisconnectedCodes.Contains(code))
            {
                item.Status = ItemStatus.Disconnected;
            }
            else if (AccessItemLoader.IsLoginRequired(code))
            {
                item.Status = ItemStatus.LoginRequired;
            }
            else
            {
                item.Status = ItemStatus.Error;
            }
        }

        // After a failed pass, drop any half-made row changes so only the item's status is saved
        private void DetachPendingChanges(InstitutionAccessItem item)
        {
            foreach (var entry in context.ChangeTracker.Entries().ToList())
            {
                if (entry.Entity is BankTransaction or FinancialAccount or TransactionCategory or BudgetExcludedTransaction)
                {
                    if (entry.State == EntityState.Added)
                    {
                        entry.State = EntityState.Detached;
                    }
                    else if (entry.State == EntityState.Modified)
                    {
                        entry.CurrentValues.SetValues(entry.OriginalValues);
                        entry.State = EntityState.Unchanged;
                    }
                }
            }
        }

        private static DateOnly? ParseDate(string? value) =>
            DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

        private static decimal? Money(double? value) =>
            value is double v && double.IsFinite(v) && Math.Abs(v) < 1e12 ? Math.Round((decimal)v, 2) : null;

        private static string? Clip(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            var trimmed = value.Trim();
            return trimmed.Length <= max ? trimmed : trimmed[..max];
        }
    }
}
