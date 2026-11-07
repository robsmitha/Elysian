using System.Globalization;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial;
using Elysian.Application.Features.Financial.Categories;
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
        ITransactionClassifier classifier, TimeProvider timeProvider, ILogger<TransactionSyncService> logger) : ITransactionSyncService
    {
        // Plaid asks for the whole pass to restart from the first cursor when data changes mid-pagination
        private const int MaxPassAttempts = 3;
        private const int MaxPagesPerPass = 500;
        // SQL Server allows about 2,100 parameters per query
        private const int IdBatchSize = 1000;

        private static readonly HashSet<string> TemporaryTypes = new(StringComparer.OrdinalIgnoreCase) { "API_ERROR", "INSTITUTION_ERROR", "RATE_LIMIT_EXCEEDED" };
        /// <summary>How long a sync holds its bank; a crashed sync lets go when this runs out</summary>
        private static readonly TimeSpan LeaseLength = TimeSpan.FromMinutes(10);
        public const string SyncInProgress = "SYNC_IN_PROGRESS";
        /// <summary>A failure that isn't Plaid's (a database error or a bug); details are in the logs</summary>
        public const string InternalError = "INTERNAL_ERROR";

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
            // Banks aren't tenant-scoped but their accounts and transactions are. Under another tenant the stored rows are
            // invisible and the sync would store everything again, so it only runs under a tenant one of the owners is in.
            if (!await OwnedInThisTenant().AnyAsync(i => i.InstitutionAccessItemId == item.InstitutionAccessItemId, cancellationToken))
            {
                return SyncOutcome.Skip("other_tenant");
            }

            var token = await accessTokenService.GetAccessTokenForSyncAsync(institutionAccessItemId);
            if (token?.AccessToken == null)
            {
                return SyncOutcome.Skip("no_token");
            }

            var now = timeProvider.GetUtcNow();
            // One sync per bank at a time; a second one tries again shortly (the queue retries it)
            var holder = Guid.NewGuid();
            if (!await TryTakeLeaseAsync(item.InstitutionAccessItemId, holder, now, cancellationToken))
            {
                logger.LogInformation("Item {Item} is already syncing; {Trigger} sync will try again", item.InstitutionAccessItemId, trigger);
                return new SyncOutcome(false, ErrorCode: SyncInProgress, Retry: true);
            }
            try
            {
                return await SyncWithLeaseAsync(item, token.AccessToken, trigger, now, cancellationToken);
            }
            finally
            {
                await ReleaseLeaseAsync(item.InstitutionAccessItemId, holder);
            }
        }

        private async Task<SyncOutcome> SyncWithLeaseAsync(InstitutionAccessItem item, string accessToken, SyncTrigger trigger, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var run = new SyncRun { InstitutionAccessItemId = item.InstitutionAccessItemId, Trigger = trigger, StartedAt = now };

            try
            {
                await EnsureWebhookAsync(item, accessToken);
                await EnsureInstitutionDetailsAsync(item);

                var pass = await ReadChangesAsync(accessToken, item.TransactionsCursor, cancellationToken);
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

                await ClassifyForOwnersAsync(item.InstitutionAccessItemId, cancellationToken);
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Anything else (a database error, a bug): nothing from this pass is kept, but the failure shows on the
                // accounts page and in Sync activity instead of disappearing into the logs
                logger.LogError(ex, "Sync of item {Item} ({Trigger}) failed unexpectedly", item.InstitutionAccessItemId, trigger);
                context.ChangeTracker.Clear();
                var fresh = await context.InstitutionAccessItems.FirstAsync(i => i.InstitutionAccessItemId == item.InstitutionAccessItemId, cancellationToken);
                fresh.Status = ItemStatus.Error;
                fresh.ErrorCode = InternalError;
                context.SyncRuns.Add(new SyncRun
                {
                    InstitutionAccessItemId = item.InstitutionAccessItemId,
                    Trigger = trigger,
                    StartedAt = now,
                    FinishedAt = timeProvider.GetUtcNow(),
                    ErrorCode = InternalError
                });
                await context.SaveChangesAsync(cancellationToken);
                return new SyncOutcome(false, ErrorCode: InternalError);
            }
        }

        /// <summary>Takes the bank's sync lease if nobody holds it (or the holder's time ran out). Saved straight away so other syncs see it.</summary>
        private async Task<bool> TryTakeLeaseAsync(int itemId, Guid holder, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var lease = await context.SyncLeases.FirstOrDefaultAsync(l => l.InstitutionAccessItemId == itemId, cancellationToken);
            if (lease == null)
            {
                lease = context.SyncLeases.Add(new SyncLease { InstitutionAccessItemId = itemId }).Entity;
            }
            else if (lease.LeasedUntil > now)
            {
                return false;
            }
            lease.LeasedUntil = now + LeaseLength;
            lease.Holder = holder;
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException)
            {
                // Another sync took it first (a duplicate insert, or a concurrency conflict on an expired lease)
                context.Entry(lease).State = EntityState.Detached;
                return false;
            }
        }

        private async Task ReleaseLeaseAsync(int itemId, Guid holder)
        {
            try
            {
                // Whatever this sync left unsaved isn't retried here
                context.ChangeTracker.Clear();
                var lease = await context.SyncLeases.FirstOrDefaultAsync(l => l.InstitutionAccessItemId == itemId && l.Holder == holder);
                if (lease == null) return;
                lease.LeasedUntil = DateTimeOffset.MinValue;
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Not fatal: the lease runs out on its own
                logger.LogWarning(ex, "Couldn't release the sync lease for item {Item}", itemId);
            }
        }

        /// <summary>Banks with an owner in the current tenant (Users is tenant-scoped; banks and their owner links aren't)</summary>
        private IQueryable<InstitutionAccessItem> OwnedInThisTenant()
        {
            var tenantUsers = context.Users.Select(u => u.UserId);
            return context.InstitutionAccessItems.Where(i => i.AccessUsers.Any(au => tenantUsers.Contains(au.UserId)));
        }

        public async Task<List<int>> GetItemsDueForSyncAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
        {
            var now = timeProvider.GetUtcNow();
            var cutoff = now - olderThan;
            // Only this tenant's banks: the nightly job runs once per tenant
            var active = await OwnedInThisTenant()
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
            // Ids are compared as SQL Server does (case-insensitive), or two ids differing only in case would both be inserted
            var groups = pass.Upserts.GroupBy(t => t.transaction_id, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var g in groups.Where(g => g.Select(t => t.transaction_id).Distinct(StringComparer.Ordinal).Count() > 1))
            {
                logger.LogWarning("Plaid sent transaction ids that differ only in case for item {Item}: {Ids}; keeping the last",
                    item.InstitutionAccessItemId, string.Join(", ", g.Select(t => t.transaction_id).Distinct(StringComparer.Ordinal)));
            }
            var upserts = groups.Select(g => g.Last()).ToList();
            var removedIds = pass.Removed.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            // Pending rows that just posted are loaded too, so the person's choices on them can move to the posted row
            var pendingIds = upserts.Where(t => !string.IsNullOrEmpty(t.pending_transaction_id)).Select(t => t.pending_transaction_id!);
            var existing = await LoadExistingAsync(upserts.Select(t => t.transaction_id).Concat(removedIds).Concat(pendingIds), cancellationToken);

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

                if (!t.pending && !string.IsNullOrEmpty(t.pending_transaction_id) && existing.TryGetValue(t.pending_transaction_id, out var pendingRow))
                {
                    CarryOverChoices(pendingRow, row);
                }
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

        /// <summary>A person's own choices on a pending charge move to its posted version (amounts can change, e.g. a tip).</summary>
        private static void CarryOverChoices(BankTransaction pending, BankTransaction posted)
        {
            if (pending.CategorySource == ClassificationSource.User && posted.CategorySource != ClassificationSource.User)
            {
                posted.CategoryId = pending.CategoryId;
                posted.CategorySource = ClassificationSource.User;
            }
            if (pending.TreatmentSource == ClassificationSource.User && posted.TreatmentSource != ClassificationSource.User)
            {
                posted.Treatment = pending.Treatment;
                posted.TreatmentSource = ClassificationSource.User;
            }
            posted.Notes ??= pending.Notes;
        }

        // Classifies the new and changed rows for each person who linked this bank
        private async Task ClassifyForOwnersAsync(int institutionAccessItemId, CancellationToken cancellationToken)
        {
            var owners = await context.InstitutionAccessItemUsers
                .Where(u => u.InstitutionAccessItemId == institutionAccessItemId)
                .Select(u => u.User.ExternalUserId)
                .Distinct()
                .ToListAsync(cancellationToken);
            foreach (var owner in owners)
            {
                try
                {
                    await classifier.ClassifyAsync(owner, cancellationToken: cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The data is saved; pages classify anything left over when they load
                    logger.LogError(ex, "Classifying after the sync of item {Item} failed", institutionAccessItemId);
                }
            }
        }

        private async Task<Dictionary<string, BankTransaction>> LoadExistingAsync(IEnumerable<string> ids, CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, BankTransaction>(StringComparer.OrdinalIgnoreCase);
            foreach (var batch in ids.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(IdBatchSize))
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
            row.MerchantKey = CategoryCatalog.MerchantKey(t.merchant_name ?? t.name);
            // New or changed by Plaid: the classifier looks at it again (a person's own choices are kept)
            row.ClassifiedAt = null;
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
