using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Linked banks for the pages. Reads come from SQL (what the sync stored), so a page load never waits on Plaid.
    /// Every method that takes a userId only touches banks that person linked.
    /// </summary>
    public class ConnectionService(ElysianContext context, IFinancialService plaid, IAccessTokenProtector protector,
        IAccessTokenService accessTokenService, IPlaidSyncQueue syncQueue, IOptions<PlaidSettings> options,
        TimeProvider timeProvider, ILogger<ConnectionService> logger) : IConnectionService
    {
        private readonly PlaidSettings settings = options.Value;

        private IQueryable<InstitutionAccessItem> Owned(string userId) =>
            context.InstitutionAccessItems.Where(i => i.AccessUsers.Any(u => u.User.ExternalUserId == userId));

        public async Task<ConnectionsResponse> GetConnectionsAsync(string userId, CancellationToken cancellationToken = default)
        {
            var now = timeProvider.GetUtcNow();
            var items = await Owned(userId)
                .Where(i => i.RemovedAt == null)
                .Include(i => i.Accounts)
                .OrderBy(i => i.CreatedAt)
                .ToListAsync(cancellationToken);

            await QueueFirstSyncsAsync(items, now, cancellationToken);

            var connections = items.Select(i => new ConnectionModel
            {
                InstitutionAccessItemId = i.InstitutionAccessItemId,
                InstitutionName = i.InstitutionName ?? "Your bank",
                InstitutionLogo = i.InstitutionLogo,
                InstitutionColor = i.InstitutionColor,
                Status = ConnectionStatus.For(i.Status, neverSynced: i.LastSyncedAt == null),
                StatusMessage = ConnectionStatus.MessageFor(i.Status, i.ConsentExpiresAt),
                LastSyncedAt = i.LastSyncedAt,
                ConsentExpiresAt = i.ConsentExpiresAt,
                NewAccountsAvailable = i.NewAccountsAvailable,
                SyncAvailableAt = SyncAvailableAt(i, now),
                Accounts = (i.Accounts ?? []).OrderBy(a => a.Type).ThenBy(a => a.Name).Select(a => new ConnectionAccountModel
                {
                    FinancialAccountId = a.FinancialAccountId,
                    Name = a.Name,
                    OfficialName = a.OfficialName,
                    Mask = a.Mask,
                    Type = a.Type,
                    Subtype = a.Subtype,
                    CurrentBalance = a.CurrentBalance,
                    AvailableBalance = a.AvailableBalance,
                    CreditLimit = a.CreditLimit,
                    IsoCurrencyCode = a.IsoCurrencyCode,
                    BudgetRole = a.BudgetRole
                }).ToList()
            }).ToList();

            var itemIds = items.Select(i => i.InstitutionAccessItemId).ToList();
            var names = items.ToDictionary(i => i.InstitutionAccessItemId, i => i.InstitutionName ?? "Your bank");
            var since = now.AddDays(-1);
            var runs = await context.SyncRuns
                .Where(r => itemIds.Contains(r.InstitutionAccessItemId) && r.StartedAt >= since)
                .OrderByDescending(r => r.StartedAt)
                .Take(10)
                .ToListAsync(cancellationToken);
            var recent = runs.Select(r => new SyncActivityModel(r.StartedAt, names[r.InstitutionAccessItemId], r.Trigger, r.Added, r.Modified, r.Removed, r.ErrorCode)).ToList();

            return new ConnectionsResponse(connections, recent);
        }

        public async Task<List<InstitutionAccessItemModel>> GetAccessItemsAsync(string userId, int? institutionAccessItemId = null)
        {
            var query = Owned(userId).Where(i => i.RemovedAt == null);
            if (institutionAccessItemId is int id)
            {
                query = query.Where(i => i.InstitutionAccessItemId == id);
            }
            var items = await query.Include(i => i.Accounts).OrderBy(i => i.CreatedAt).ToListAsync();
            return items.Select(ToAccessItemModel).ToList();
        }

        public async Task<List<InstitutionAccessItemModel>> GetBudgetAccessItemsAsync(string userId, int budgetId)
        {
            var itemIds = context.BudgetAccessItems
                .Where(b => b.BudgetId == budgetId && b.Budget.UserId == userId)
                .Select(b => b.InstitutionAccessItemId);
            var items = await context.InstitutionAccessItems
                .Where(i => itemIds.Contains(i.InstitutionAccessItemId))
                .Include(i => i.Accounts)
                .ToListAsync();
            return items.Select(ToAccessItemModel).ToList();
        }

        public async Task<List<TransactionModel>> GetTransactionsAsync(IReadOnlyCollection<int> institutionAccessItemIds, DateTime start, DateTime end)
        {
            if (institutionAccessItemIds.Count == 0)
            {
                return [];
            }
            var from = DateOnly.FromDateTime(start);
            var to = DateOnly.FromDateTime(end);
            var rows = await context.BankTransactions
                .Include(t => t.FinancialAccount)
                .Include(t => t.Category)
                .Include(t => t.Splits).ThenInclude(sp => sp.Category)
                .Where(t => institutionAccessItemIds.Contains(t.FinancialAccount.InstitutionAccessItemId)
                    && t.RemovedAt == null && t.BudgetDate >= from && t.BudgetDate <= to)
                .OrderByDescending(t => t.BudgetDate).ThenByDescending(t => t.BankTransactionId)
                .ToListAsync();

            return rows.Select(BankTransactionMapping.ToModel).ToList();
        }

        public async Task<int> ExchangePublicTokenAsync(string userId, ExchangePublicTokenRequest request, CancellationToken cancellationToken = default)
        {
            // Checked before the exchange, so a missing key never leaves a linked-but-unsaved bank at Plaid
            if (!protector.CanProtect)
            {
                throw new InvalidOperationException("PlaidSettings:TokenEncryptionKey is not configured, so a bank can't be linked.");
            }

            var user = await context.Users.FirstOrDefaultAsync(u => u.ExternalUserId == userId, cancellationToken)
                ?? throw new InvalidOperationException("The signed-in person has no user record.");

            if (await IsDuplicateAsync(userId, request, cancellationToken))
            {
                throw new ConflictException("This bank is already linked. Reconnect it from the accounts page instead.");
            }

            var exchange = await plaid.ItemPublicTokenExchangeAsync(request.PublicToken);
            InstitutionAccessItem item;
            try
            {
                var plaidItem = await plaid.GetItemAsync(exchange.access_token);
                var accounts = await plaid.GetAccountsAsync(exchange.access_token);
                var institutionId = request.InstitutionId ?? plaidItem.InstitutionId;
                var institution = await AccessItemLoader.GetInstitutionAsync(plaid, institutionId);
                var now = timeProvider.GetUtcNow();

                item = new InstitutionAccessItem
                {
                    AccessTokenCiphertext = protector.Protect(exchange.access_token),
                    ItemId = exchange.item_id,
                    InstitutionId = institutionId,
                    InstitutionName = institution.name,
                    InstitutionLogo = institution.logo,
                    InstitutionColor = institution.primary_color,
                    Status = ItemStatus.Healthy,
                    ConsentExpiresAt = plaidItem.ConsentExpiresAt,
                    // The link token carried the webhook URL, so the new Item already has it
                    WebhookUrl = plaid.WebhookUrl,
                    AccessUsers = [new InstitutionAccessItemUser { UserId = user.UserId }],
                    Accounts = accounts.Select(a => new FinancialAccount
                    {
                        PlaidAccountId = a.account_id,
                        Name = a.name ?? "Account",
                        OfficialName = a.official_name,
                        Mask = a.mask,
                        Type = a.type ?? "other",
                        Subtype = a.subtype,
                        CurrentBalance = a.balances?.current,
                        AvailableBalance = a.balances?.available,
                        CreditLimit = a.balances?.limit,
                        IsoCurrencyCode = a.balances?.iso_currency_code,
                        BalanceAsOf = now,
                        BudgetRole = FinancialAccount.DefaultRole(a.type, a.subtype)
                    }).ToList()
                };
                context.InstitutionAccessItems.Add(item);
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception)
            {
                // Saving failed after Plaid linked the bank: remove it there, so nothing is left behind (and billed)
                try
                {
                    await plaid.RemoveItemAsync(exchange.access_token);
                }
                catch (Exception cleanup)
                {
                    logger.LogError(cleanup, "Couldn't remove Plaid item {Item} after a failed link", exchange.item_id);
                }
                throw;
            }

            await TryEnqueueAsync(item.InstitutionAccessItemId, SyncTrigger.Initial, cancellationToken);
            return item.InstitutionAccessItemId;
        }

        public async Task<SyncRequestResult> RequestSyncAsync(string userId, int institutionAccessItemId, CancellationToken cancellationToken = default)
        {
            var item = await Owned(userId).FirstOrDefaultAsync(i => i.InstitutionAccessItemId == institutionAccessItemId && i.RemovedAt == null, cancellationToken)
                ?? throw new NotFoundException("Linked bank", institutionAccessItemId);
            if (item.Status is ItemStatus.LoginRequired or ItemStatus.Disconnected)
            {
                throw new ConflictException("Reconnect this bank before syncing it.");
            }

            var now = timeProvider.GetUtcNow();
            var cooldown = TimeSpan.FromMinutes(settings.ManualSyncCooldownMinutes);
            if (item.LastSyncRequestedAt is { } last && last + cooldown > now)
            {
                throw new TooManyRequestsException("This bank was synced a moment ago.", last + cooldown);
            }

            var hourAgo = now.AddHours(-1);
            var userItemIds = Owned(userId).Select(i => i.InstitutionAccessItemId);
            var recentManual = await context.SyncRuns
                .Where(r => userItemIds.Contains(r.InstitutionAccessItemId) && r.Trigger == SyncTrigger.Manual && r.StartedAt >= hourAgo)
                .OrderBy(r => r.StartedAt)
                .Select(r => r.StartedAt)
                .ToListAsync(cancellationToken);
            if (recentManual.Count >= settings.ManualSyncsPerHour)
            {
                throw new TooManyRequestsException("Too many manual syncs this hour.", recentManual[0].AddHours(1));
            }

            item.LastSyncRequestedAt = now;
            await context.SaveChangesAsync(cancellationToken);
            await syncQueue.EnqueueAsync(item.InstitutionAccessItemId, SyncTrigger.Manual, cancellationToken);
            return new SyncRequestResult(true, now + cooldown);
        }

        public async Task CompleteReconnectAsync(string userId, int institutionAccessItemId, CancellationToken cancellationToken = default)
        {
            var item = await Owned(userId).FirstOrDefaultAsync(i => i.InstitutionAccessItemId == institutionAccessItemId && i.RemovedAt == null, cancellationToken)
                ?? throw new NotFoundException("Linked bank", institutionAccessItemId);
            var token = await accessTokenService.GetAccessTokenAsync(userId, institutionAccessItemId);

            item.Status = ItemStatus.Healthy;
            item.ErrorCode = null;
            item.NewAccountsAvailable = false;
            if (token?.AccessToken != null)
            {
                try
                {
                    var plaidItem = await plaid.GetItemAsync(token.AccessToken);
                    item.ConsentExpiresAt = plaidItem?.ConsentExpiresAt ?? item.ConsentExpiresAt;
                }
                catch (FinancialServiceException ex)
                {
                    // The sync below records the real status
                    logger.LogWarning("Couldn't read item {Item} after reconnect: {Code}", item.ItemId, ex.Error?.error_code);
                }
            }
            await context.SaveChangesAsync(cancellationToken);
            await TryEnqueueAsync(item.InstitutionAccessItemId, SyncTrigger.Reconnect, cancellationToken);
        }

        public async Task RemoveAsync(string userId, int institutionAccessItemId, bool keepHistory, CancellationToken cancellationToken = default)
        {
            var item = await Owned(userId).FirstOrDefaultAsync(i => i.InstitutionAccessItemId == institutionAccessItemId && i.RemovedAt == null, cancellationToken)
                ?? throw new NotFoundException("Linked bank", institutionAccessItemId);
            var token = await accessTokenService.GetAccessTokenAsync(userId, institutionAccessItemId);

            // Plaid first, so a failure never leaves a connection Plaid still reads and bills that we can't see
            if (token?.AccessToken != null)
            {
                try
                {
                    await plaid.RemoveItemAsync(token.AccessToken);
                }
                catch (FinancialServiceException ex) when (ex.Error?.error_code == "ITEM_NOT_FOUND")
                {
                    // Already gone at Plaid
                }
            }

            if (keepHistory)
            {
                item.RemovedAt = timeProvider.GetUtcNow();
                item.Status = ItemStatus.Disconnected;
                item.AccessToken = null;
                item.AccessTokenCiphertext = null;
                item.TransactionsCursor = null;
            }
            else
            {
                // Tags and exclusions point at Plaid ids, not rows, so they're cleared by hand
                var transactionIds = await context.BankTransactions
                    .Where(t => t.FinancialAccount.InstitutionAccessItemId == item.InstitutionAccessItemId)
                    .Select(t => t.PlaidTransactionId)
                    .ToListAsync(cancellationToken);
                foreach (var batch in transactionIds.Chunk(1000))
                {
                    context.TransactionCategories.RemoveRange(await context.TransactionCategories.Where(c => batch.Contains(c.TransactionId)).ToListAsync(cancellationToken));
                    context.BudgetExcludedTransactions.RemoveRange(await context.BudgetExcludedTransactions.Where(e => batch.Contains(e.TransactionId)).ToListAsync(cancellationToken));
                }
                // Accounts, transactions, sync records and budget links cascade with the bank
                await context.FinancialAccounts.Where(a => a.InstitutionAccessItemId == item.InstitutionAccessItemId)
                    .Include(a => a.Transactions).LoadAsync(cancellationToken);
                await context.BudgetAccessItems.Where(b => b.InstitutionAccessItemId == item.InstitutionAccessItemId).LoadAsync(cancellationToken);
                context.InstitutionAccessItems.Remove(item);
            }
            await context.SaveChangesAsync(cancellationToken);
        }

        public async Task UpdateAccountRoleAsync(string userId, int financialAccountId, AccountBudgetRole role, CancellationToken cancellationToken = default)
        {
            var account = await context.FinancialAccounts
                .FirstOrDefaultAsync(a => a.FinancialAccountId == financialAccountId
                    && a.InstitutionAccessItem.AccessUsers.Any(u => u.User.ExternalUserId == userId), cancellationToken)
                ?? throw new NotFoundException("Account", financialAccountId);
            account.BudgetRole = role;
            await context.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// Same bank and an account with the same name and mask (or name and type when the bank sends no mask) as one
        /// already linked: Plaid's guidance for spotting a bank linked twice.
        /// </summary>
        private async Task<bool> IsDuplicateAsync(string userId, ExchangePublicTokenRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(request.InstitutionId) || request.Accounts is not { Count: > 0 })
            {
                return false;
            }
            var known = await context.FinancialAccounts
                .Where(a => a.InstitutionAccessItem.RemovedAt == null
                    && a.InstitutionAccessItem.InstitutionId == request.InstitutionId
                    && a.InstitutionAccessItem.AccessUsers.Any(u => u.User.ExternalUserId == userId))
                .Select(a => new { a.Name, a.Mask, a.Subtype })
                .ToListAsync(cancellationToken);

            static string Norm(string? s) => (s ?? "").Trim().ToLowerInvariant();
            return request.Accounts.Any(n => known.Any(k =>
                Norm(k.Name) == Norm(n.Name) &&
                (!string.IsNullOrEmpty(k.Mask) && !string.IsNullOrEmpty(n.Mask) ? k.Mask == n.Mask : Norm(k.Subtype) == Norm(n.Subtype))));
        }

        // Banks that have never synced (linked before stored transactions, or just now) get their first sync queued once
        private async Task QueueFirstSyncsAsync(List<InstitutionAccessItem> items, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var neverSynced = items.Where(i => i.LastSyncedAt == null && i.LastSyncRequestedAt == null
                && i.Status is not (ItemStatus.Disconnected or ItemStatus.LoginRequired)).ToList();
            foreach (var item in neverSynced)
            {
                item.LastSyncRequestedAt = now;
                await TryEnqueueAsync(item.InstitutionAccessItemId, SyncTrigger.Initial, cancellationToken);
            }
            if (neverSynced.Count > 0)
            {
                await context.SaveChangesAsync(cancellationToken);
            }
        }

        private async Task TryEnqueueAsync(int institutionAccessItemId, SyncTrigger trigger, CancellationToken cancellationToken)
        {
            try
            {
                await syncQueue.EnqueueAsync(institutionAccessItemId, trigger, cancellationToken);
            }
            catch (Exception ex)
            {
                // The nightly sync picks it up
                logger.LogError(ex, "Couldn't queue a {Trigger} sync for item {Item}", trigger, institutionAccessItemId);
            }
        }

        private DateTimeOffset? SyncAvailableAt(InstitutionAccessItem item, DateTimeOffset now)
        {
            if (item.LastSyncRequestedAt is not { } last)
            {
                return null;
            }
            var available = last + TimeSpan.FromMinutes(settings.ManualSyncCooldownMinutes);
            return available > now ? available : null;
        }

        private static InstitutionAccessItemModel ToAccessItemModel(InstitutionAccessItem i)
        {
            var institution = new InstitutionModel
            {
                institution_id = i.InstitutionId,
                name = i.InstitutionName ?? "Your bank",
                logo = i.InstitutionLogo,
                primary_color = i.InstitutionColor
            };
            var status = i.RemovedAt != null ? ItemStatus.Disconnected : i.Status;
            var model = new InstitutionAccessItemModel
            {
                InstitutionAccessItemId = i.InstitutionAccessItemId,
                Institution = institution,
                Item = new ItemModel
                {
                    ItemId = i.ItemId,
                    InstitutionId = i.InstitutionId,
                    LastSuccessfulUpdate = i.LastSyncedAt?.UtcDateTime,
                    ErrorCode = i.ErrorCode,
                    ConsentExpiresAt = i.ConsentExpiresAt
                },
                Accounts = (i.Accounts ?? []).Select(ToAccountModel).ToList(),
                Status = ConnectionStatus.For(status, neverSynced: i.LastSyncedAt == null),
                StatusMessage = ConnectionStatus.MessageFor(status, i.ConsentExpiresAt)
            };
            model.Item.ErrorDisplayMessage = model.StatusMessage;
            if (status == ItemStatus.LoginRequired)
            {
                model.ExpiredAccessItem = new ExpiredAccessItem(i.InstitutionAccessItemId, model.StatusMessage!, institution);
            }
            return model;
        }

        private static AccountModel ToAccountModel(FinancialAccount a) => new()
        {
            account_id = a.PlaidAccountId,
            name = a.Name,
            official_name = a.OfficialName,
            mask = a.Mask,
            type = a.Type,
            subtype = a.Subtype,
            balances = new BalancesModel
            {
                current = a.CurrentBalance,
                available = a.AvailableBalance,
                limit = a.CreditLimit,
                iso_currency_code = a.IsoCurrencyCode
            }
        };
    }
}
