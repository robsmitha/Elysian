using Elysian.Application.Features.Financial.Categories;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Places each stored transaction (docs/budget-plan.md §4.4). In order, the first that applies wins:
    /// 1. the person's own choice (never overwritten, including choices made on the older budget pages);
    /// 2. their rules, by priority;
    /// 3. a transfer pair: equal and opposite amounts on two of their accounts within 3 days (a card payment when one leg
    ///    is a credit card);
    /// 4. Plaid's category;
    /// 5. a refund: money in from a merchant they bought from nets against that merchant's category;
    /// 6. a default: money out is spending, money in is income (a credit on a card is a refund).
    /// </summary>
    public class TransactionClassifier(ElysianContext context, ICategoryService categoryService, TimeProvider timeProvider) : ITransactionClassifier
    {
        private const int TransferWindowDays = 3;
        private const int IdBatchSize = 1000;

        public async Task<int> ClassifyAsync(string userId, bool reclassifyAll = false, CancellationToken cancellationToken = default)
        {
            var accounts = await context.FinancialAccounts
                .Where(a => a.InstitutionAccessItem.AccessUsers.Any(u => u.User.ExternalUserId == userId))
                .Select(a => new { a.FinancialAccountId, a.Type, a.BudgetRole })
                .ToDictionaryAsync(a => a.FinancialAccountId, cancellationToken);
            if (accounts.Count == 0)
            {
                return 0;
            }
            var accountIds = accounts.Keys.ToList();

            var candidates = await context.BankTransactions
                .Include(t => t.Splits)
                .Where(t => accountIds.Contains(t.FinancialAccountId) && t.RemovedAt == null && (reclassifyAll || t.ClassifiedAt == null))
                .ToListAsync(cancellationToken);
            if (candidates.Count == 0)
            {
                return 0;
            }

            // Rows stored before merchant keys existed get one now, before refunds and rules look for it
            var unkeyed = candidates.Where(t => t.MerchantKey == "").ToList();
            if (unkeyed.Count > 0)
            {
                foreach (var t in unkeyed)
                {
                    t.MerchantKey = CategoryCatalog.MerchantKey(t.MerchantName ?? t.Name);
                }
                await context.SaveChangesAsync(cancellationToken);
            }

            await categoryService.EnsureDefaultsAsync(userId);
            var categoryByKey = await context.FinancialCategories
                .Where(c => c.UserId == userId && c.SystemKey != null && !c.IsArchived)
                .ToDictionaryAsync(c => c.SystemKey!, c => c.FinancialCategoryId, cancellationToken);
            var rules = await context.CategoryRules
                .Where(r => r.UserId == userId)
                .OrderBy(r => r.Priority).ThenBy(r => r.CategoryRuleId)
                .ToListAsync(cancellationToken);

            var legacy = await LoadLegacyChoicesAsync(candidates, cancellationToken);
            var neighbors = await LoadNeighborsAsync(accountIds, candidates, cancellationToken);
            var merchantCategories = await LoadMerchantCategoriesAsync(accountIds, candidates, cancellationToken);
            var paired = new HashSet<long>();
            // A pair found from one side is remembered for the other side, which may come later in this run
            var pairedTreatment = new Dictionary<long, Treatment>();
            var now = timeProvider.GetUtcNow();

            foreach (var t in candidates.OrderBy(t => t.BudgetDate).ThenBy(t => t.BankTransactionId))
            {
                var account = accounts[t.FinancialAccountId];
                var isCredit = account.Type == "credit";

                // 1. Choices made on the older budget pages count as the person's own
                if (legacy.Categories.TryGetValue(t.PlaidTransactionId, out var legacyCategory) && t.CategorySource != ClassificationSource.User)
                {
                    t.CategoryId = legacyCategory;
                    t.CategorySource = ClassificationSource.User;
                }
                if (legacy.Excluded.Contains(t.PlaidTransactionId) && t.TreatmentSource != ClassificationSource.User)
                {
                    t.Treatment = Treatment.Hidden;
                    t.TreatmentSource = ClassificationSource.User;
                }

                var treatment = (Value: (Treatment?)null, Source: ClassificationSource.Auto);
                var category = (Value: (int?)null, Source: ClassificationSource.Auto, Set: false);

                // 2. Rules: the first rule that sets a field wins that field
                foreach (var rule in rules.Where(r => Matches(r, t)))
                {
                    if (treatment.Value == null && rule.SetTreatment is Treatment ruleTreatment)
                    {
                        treatment = (ruleTreatment, ClassificationSource.Rule);
                    }
                    if (!category.Set && rule.SetCategoryId is int ruleCategory)
                    {
                        category = (ruleCategory, ClassificationSource.Rule, true);
                    }
                }

                // 3. Transfer pair
                if (treatment.Value == null && pairedTreatment.TryGetValue(t.BankTransactionId, out var fromPair))
                {
                    treatment = (fromPair, ClassificationSource.Auto);
                }
                else if (treatment.Value == null && CategoryCatalog.IsTransferLike(t.PfcDetailed))
                {
                    var other = FindPair(t, neighbors, paired);
                    if (other != null)
                    {
                        paired.Add(t.BankTransactionId);
                        paired.Add(other.BankTransactionId);
                        var cardInvolved = isCredit || (accounts.TryGetValue(other.FinancialAccountId, out var o) && o.Type == "credit");
                        var pairTreatment = cardInvolved ? Treatment.CardPayment : Treatment.Transfer;
                        treatment = (pairTreatment, ClassificationSource.Auto);
                        pairedTreatment[other.BankTransactionId] = pairTreatment;
                        // The other leg is placed the same way when nobody decided it by hand
                        if (other.TreatmentSource == ClassificationSource.Auto)
                        {
                            other.Treatment = pairTreatment;
                            other.CategoryId = other.CategorySource == ClassificationSource.User ? other.CategoryId : null;
                            other.ClassifiedAt = now;
                        }
                    }
                }

                // 4. Plaid's category
                var mapping = CategoryCatalog.ForPlaid(t.PfcDetailed);
                if (treatment.Value == null && mapping != null)
                {
                    // A card's own payment credit is a card payment even when Plaid calls it a transfer
                    var mapped = mapping.Treatment == Treatment.Transfer && isCredit && t.Amount < 0 ? Treatment.CardPayment : mapping.Treatment;
                    treatment = (mapped, ClassificationSource.Auto);
                }
                if (!category.Set && mapping?.CategoryKey != null && categoryByKey.TryGetValue(mapping.CategoryKey, out var mappedCategory))
                {
                    category = (mappedCategory, ClassificationSource.Auto, true);
                }

                // 5. Refund from a merchant they bought from
                if (t.Amount < 0 && (treatment.Value == null || treatment.Value == Treatment.Spending)
                    && merchantCategories.TryGetValue(t.MerchantKey, out var boughtCategory))
                {
                    treatment = (Treatment.Refund, ClassificationSource.Auto);
                    if (!category.Set)
                    {
                        category = (boughtCategory, ClassificationSource.Auto, true);
                    }
                }

                // 6. Defaults
                treatment.Value ??= t.Amount >= 0 ? Treatment.Spending : isCredit ? Treatment.Refund : Treatment.Income;

                if (t.TreatmentSource != ClassificationSource.User)
                {
                    t.Treatment = treatment.Value.Value;
                    t.TreatmentSource = treatment.Source;
                }
                if (t.CategorySource != ClassificationSource.User)
                {
                    // Only spending and refunds have a spending category
                    var counted = t.Treatment is Treatment.Spending or Treatment.Refund;
                    t.CategoryId = counted && category.Set ? category.Value : null;
                    t.CategorySource = counted && category.Set ? category.Source : ClassificationSource.Auto;
                }
                t.ClassifiedAt = now;
            }

            await context.SaveChangesAsync(cancellationToken);
            return candidates.Count;
        }

        /// <summary>Whether a rule applies to a transaction (direction, then field).</summary>
        public static bool Matches(CategoryRule rule, BankTransaction t)
        {
            if (rule.Direction == RuleDirection.Out && t.Amount < 0 || rule.Direction == RuleDirection.In && t.Amount >= 0)
            {
                return false;
            }
            var value = rule.Value.Trim().ToLowerInvariant();
            var subject = rule.Field switch
            {
                RuleField.Merchant => t.MerchantKey,
                RuleField.PlaidCategory => (t.PfcDetailed ?? "").ToLowerInvariant(),
                _ => (t.Name ?? "").ToLowerInvariant()
            };
            if (rule.Field == RuleField.Merchant)
            {
                value = CategoryCatalog.MerchantKey(value);
            }
            return rule.Match == RuleMatch.Equals ? subject == value : subject.Contains(value, StringComparison.Ordinal);
        }

        private static BankTransaction? FindPair(BankTransaction t, List<BankTransaction> neighbors, HashSet<long> paired)
        {
            if (paired.Contains(t.BankTransactionId))
            {
                return null;
            }
            return neighbors
                .Where(o => o.BankTransactionId != t.BankTransactionId
                    && !paired.Contains(o.BankTransactionId)
                    && o.FinancialAccountId != t.FinancialAccountId
                    && o.Amount == -t.Amount
                    && t.Amount != 0
                    && Math.Abs(o.BudgetDate.DayNumber - t.BudgetDate.DayNumber) <= TransferWindowDays
                    && CategoryCatalog.IsTransferLike(o.PfcDetailed)
                    && o.TreatmentSource != ClassificationSource.User)
                .OrderBy(o => Math.Abs(o.BudgetDate.DayNumber - t.BudgetDate.DayNumber))
                .FirstOrDefault();
        }

        // Every stored transaction near the candidates' dates, for pairing transfers across accounts
        private async Task<List<BankTransaction>> LoadNeighborsAsync(List<int> accountIds, List<BankTransaction> candidates, CancellationToken cancellationToken)
        {
            var from = candidates.Min(t => t.BudgetDate).AddDays(-TransferWindowDays);
            var to = candidates.Max(t => t.BudgetDate).AddDays(TransferWindowDays);
            return await context.BankTransactions
                .Where(t => accountIds.Contains(t.FinancialAccountId) && t.RemovedAt == null && t.BudgetDate >= from && t.BudgetDate <= to)
                .ToListAsync(cancellationToken);
        }

        // Merchants the person has spent with, and the category of their latest purchase there (for refunds)
        private async Task<Dictionary<string, int>> LoadMerchantCategoriesAsync(List<int> accountIds, List<BankTransaction> candidates, CancellationToken cancellationToken)
        {
            var keys = candidates.Where(t => t.Amount < 0 && t.MerchantKey != "").Select(t => t.MerchantKey).Distinct().ToList();
            var result = new Dictionary<string, int>();
            foreach (var batch in keys.Chunk(IdBatchSize))
            {
                var purchases = await context.BankTransactions
                    .Where(t => accountIds.Contains(t.FinancialAccountId) && t.RemovedAt == null && t.Amount > 0
                        && t.Treatment == Treatment.Spending && t.CategoryId != null && batch.Contains(t.MerchantKey))
                    .Select(t => new { t.MerchantKey, t.CategoryId, t.BudgetDate })
                    .ToListAsync(cancellationToken);
                foreach (var g in purchases.GroupBy(p => p.MerchantKey))
                {
                    result[g.Key] = g.OrderByDescending(p => p.BudgetDate).First().CategoryId!.Value;
                }
            }
            return result;
        }

        private sealed record LegacyChoices(Dictionary<string, int> Categories, HashSet<string> Excluded);

        // Categories and exclusions set per budget before Phase 2, by Plaid transaction id (latest wins)
        private async Task<LegacyChoices> LoadLegacyChoicesAsync(List<BankTransaction> candidates, CancellationToken cancellationToken)
        {
            var categories = new Dictionary<string, int>();
            var excluded = new HashSet<string>();
            foreach (var batch in candidates.Select(t => t.PlaidTransactionId).Distinct().Chunk(IdBatchSize))
            {
                var tags = await context.TransactionCategories
                    .Where(c => batch.Contains(c.TransactionId))
                    .OrderBy(c => c.TransactionCategoryId)
                    .Select(c => new { c.TransactionId, c.FinancialCategoryId })
                    .ToListAsync(cancellationToken);
                foreach (var tag in tags)
                {
                    categories[tag.TransactionId] = tag.FinancialCategoryId;
                }
                excluded.UnionWith(await context.BudgetExcludedTransactions
                    .Where(e => batch.Contains(e.TransactionId))
                    .Select(e => e.TransactionId)
                    .ToListAsync(cancellationToken));
            }
            return new LegacyChoices(categories, excluded);
        }
    }
}
