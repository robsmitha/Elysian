using System.Globalization;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Infrastructure.Services
{
    /// <summary>The Transactions page: search, edit, split, and "always do this" rules. Only the person's own transactions.</summary>
    public class TransactionService(ElysianContext context, ITransactionClassifier classifier) : ITransactionService
    {
        public const int MaxPageSize = 200;

        private IQueryable<BankTransaction> Mine(string userId) =>
            context.BankTransactions.Where(t => t.RemovedAt == null
                && t.FinancialAccount.InstitutionAccessItem.AccessUsers.Any(u => u.User.ExternalUserId == userId));

        public async Task<TransactionSearchResult> SearchAsync(string userId, TransactionSearch search, CancellationToken cancellationToken = default)
        {
            await classifier.ClassifyAsync(userId, cancellationToken: cancellationToken);

            var query = Mine(userId);
            if (search.From is DateOnly from) query = query.Where(t => t.BudgetDate >= from);
            if (search.To is DateOnly to) query = query.Where(t => t.BudgetDate <= to);
            if (search.FinancialAccountId is int accountId) query = query.Where(t => t.FinancialAccountId == accountId);
            if (search.CategoryId is int categoryId)
            {
                query = categoryId == 0
                    ? query.Where(t => t.CategoryId == null && !t.Splits.Any())
                    : query.Where(t => t.CategoryId == categoryId || t.Splits.Any(s => s.CategoryId == categoryId));
            }
            if (!string.IsNullOrWhiteSpace(search.Query))
            {
                var text = search.Query.Trim();
                // LIKE is case-insensitive whatever the column's collation; wildcards in the text are matched literally
                var pattern = $"%{text.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]")}%";
                // A number also finds that amount, in or out
                if (decimal.TryParse(text.TrimStart('$'), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
                {
                    query = query.Where(t => t.Amount == amount || t.Amount == -amount || EF.Functions.Like(t.Name, pattern));
                }
                else
                {
                    query = query.Where(t => EF.Functions.Like(t.Name, pattern)
                        || (t.MerchantName != null && EF.Functions.Like(t.MerchantName, pattern))
                        || (t.Notes != null && EF.Functions.Like(t.Notes, pattern)));
                }
            }

            // Counted: spending or a refund, on an account that counts as spending
            var counted = query.Where(t => t.FinancialAccount.BudgetRole == AccountBudgetRole.Spending
                && (t.Treatment == Treatment.Spending || t.Treatment == Treatment.Refund));
            var counts = new TransactionViewCounts(
                All: await query.CountAsync(cancellationToken),
                Spending: await counted.CountAsync(cancellationToken),
                NotCounted: await query.CountAsync(t => !(t.FinancialAccount.BudgetRole == AccountBudgetRole.Spending
                    && (t.Treatment == Treatment.Spending || t.Treatment == Treatment.Refund)), cancellationToken),
                Review: await counted.CountAsync(t => t.CategoryId == null && !t.Splits.Any(), cancellationToken),
                Pending: await query.CountAsync(t => t.Pending, cancellationToken));
            var countedTotal = await counted.SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0;

            var view = search.View switch
            {
                TransactionView.Spending => counted,
                TransactionView.NotCounted => query.Where(t => !(t.FinancialAccount.BudgetRole == AccountBudgetRole.Spending
                    && (t.Treatment == Treatment.Spending || t.Treatment == Treatment.Refund))),
                TransactionView.Review => counted.Where(t => t.CategoryId == null && !t.Splits.Any()),
                TransactionView.Pending => query.Where(t => t.Pending),
                _ => query
            };

            var pageSize = Math.Clamp(search.PageSize, 1, MaxPageSize);
            var page = Math.Max(1, search.Page);
            var total = await view.CountAsync(cancellationToken);
            var rows = await WithDetails(view)
                .OrderByDescending(t => t.BudgetDate).ThenByDescending(t => t.BankTransactionId)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToListAsync(cancellationToken);

            return new TransactionSearchResult(rows.Select(BankTransactionMapping.ToModel).ToList(), total, counts, countedTotal);
        }

        public async Task<TransactionModel> UpdateAsync(string userId, TransactionUpdate update, CancellationToken cancellationToken = default)
        {
            var t = await WithDetails(Mine(userId)).FirstOrDefaultAsync(t => t.BankTransactionId == update.Id, cancellationToken)
                ?? throw new NotFoundException("Transaction", update.Id);

            if (update.CategoryId is int categoryId)
            {
                await EnsureCategoryAsync(userId, categoryId, cancellationToken);
                t.CategoryId = categoryId;
                t.CategorySource = ClassificationSource.User;
            }
            else if (update.ClearCategory)
            {
                t.CategoryId = null;
                t.CategorySource = ClassificationSource.User;
            }
            else if (update.ResetCategory)
            {
                t.CategorySource = ClassificationSource.Auto;
                t.ClassifiedAt = null;
            }

            if (update.Treatment is Treatment treatment)
            {
                t.Treatment = treatment;
                t.TreatmentSource = ClassificationSource.User;
            }
            else if (update.ResetTreatment)
            {
                // Back to the automatic rules on the next classification
                t.TreatmentSource = ClassificationSource.Auto;
                t.ClassifiedAt = null;
            }

            if (update.Notes != null)
            {
                t.Notes = string.IsNullOrWhiteSpace(update.Notes) ? null : update.Notes.Trim();
            }

            if (update.Splits != null)
            {
                await ReplaceSplitsAsync(userId, t, update.Splits, cancellationToken);
            }

            await context.SaveChangesAsync(cancellationToken);

            if (update.CreateRule is RuleFromTransaction r && !string.IsNullOrWhiteSpace(r.Value))
            {
                await SaveRuleAsync(userId, new CategoryRuleModel
                {
                    Field = r.Field,
                    Match = r.Field == RuleField.Description ? RuleMatch.Contains : RuleMatch.Equals,
                    Value = r.Value,
                    Direction = t.Amount >= 0 ? RuleDirection.Out : RuleDirection.In,
                    SetCategoryId = t.CategoryId,
                    SetTreatment = update.Treatment
                }, r.ApplyToPast, cancellationToken);
            }

            if (t.ClassifiedAt == null)
            {
                await classifier.ClassifyAsync(userId, cancellationToken: cancellationToken);
            }
            return BankTransactionMapping.ToModel(await WithDetails(Mine(userId)).FirstAsync(x => x.BankTransactionId == t.BankTransactionId, cancellationToken));
        }

        public async Task<int> BulkUpdateAsync(string userId, IReadOnlyCollection<long> ids, int? categoryId, Treatment? treatment, CancellationToken cancellationToken = default)
        {
            if (categoryId is int id)
            {
                await EnsureCategoryAsync(userId, id, cancellationToken);
            }
            var rows = await Mine(userId).Where(t => ids.Contains(t.BankTransactionId)).ToListAsync(cancellationToken);
            foreach (var t in rows)
            {
                if (categoryId != null)
                {
                    t.CategoryId = categoryId;
                    t.CategorySource = ClassificationSource.User;
                }
                if (treatment is Treatment value)
                {
                    t.Treatment = value;
                    t.TreatmentSource = ClassificationSource.User;
                }
            }
            await context.SaveChangesAsync(cancellationToken);
            return rows.Count;
        }

        public async Task SetCategoryByPlaidIdAsync(string userId, string plaidTransactionId, int categoryId, CancellationToken cancellationToken = default)
        {
            await EnsureCategoryAsync(userId, categoryId, cancellationToken);
            var t = await Mine(userId).FirstOrDefaultAsync(t => t.PlaidTransactionId == plaidTransactionId, cancellationToken)
                ?? throw new NotFoundException("Transaction", plaidTransactionId);
            t.CategoryId = categoryId;
            t.CategorySource = ClassificationSource.User;
            // Setting a category on something that wasn't counted means "count it"
            if (t.Treatment is not (Treatment.Spending or Treatment.Refund))
            {
                t.Treatment = t.Amount >= 0 ? Treatment.Spending : Treatment.Refund;
                t.TreatmentSource = ClassificationSource.User;
            }
            await context.SaveChangesAsync(cancellationToken);
        }

        public async Task SetTreatmentByPlaidIdAsync(string userId, string plaidTransactionId, Treatment treatment, CancellationToken cancellationToken = default)
        {
            var t = await Mine(userId).FirstOrDefaultAsync(t => t.PlaidTransactionId == plaidTransactionId, cancellationToken)
                ?? throw new NotFoundException("Transaction", plaidTransactionId);
            t.Treatment = treatment;
            t.TreatmentSource = ClassificationSource.User;
            await context.SaveChangesAsync(cancellationToken);
        }

        public async Task<List<CategoryRuleModel>> GetRulesAsync(string userId, CancellationToken cancellationToken = default)
        {
            var rules = await context.CategoryRules.Include(r => r.SetCategory)
                .Where(r => r.UserId == userId)
                .OrderBy(r => r.Priority).ThenBy(r => r.CategoryRuleId)
                .ToListAsync(cancellationToken);
            return rules.Select(ToModel).ToList();
        }

        public async Task<RuleSaveResult> SaveRuleAsync(string userId, CategoryRuleModel model, bool applyToPast, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(model.Value))
            {
                throw Invalid("Value", "A rule needs something to match.");
            }
            if (model.SetCategoryId == null && model.SetTreatment == null)
            {
                throw Invalid("SetCategoryId", "A rule needs a category or a treatment to set.");
            }
            if (model.SetCategoryId is int categoryId)
            {
                await EnsureCategoryAsync(userId, categoryId, cancellationToken);
            }

            CategoryRule rule;
            if (model.CategoryRuleId > 0)
            {
                rule = await context.CategoryRules.FirstOrDefaultAsync(r => r.CategoryRuleId == model.CategoryRuleId && r.UserId == userId, cancellationToken)
                    ?? throw new NotFoundException("Rule", model.CategoryRuleId);
            }
            else
            {
                var last = await context.CategoryRules.Where(r => r.UserId == userId).MaxAsync(r => (int?)r.Priority, cancellationToken) ?? 0;
                rule = new CategoryRule { UserId = userId, Priority = last + 1 };
                context.CategoryRules.Add(rule);
            }
            rule.Field = model.Field;
            rule.Match = model.Match;
            rule.Value = model.Value.Trim();
            rule.Direction = model.Direction;
            rule.SetCategoryId = model.SetCategoryId;
            rule.SetTreatment = model.SetTreatment;
            await context.SaveChangesAsync(cancellationToken);

            var matches = await PreviewRuleAsync(userId, ToModel(rule), cancellationToken);
            if (applyToPast)
            {
                await classifier.ClassifyAsync(userId, reclassifyAll: true, cancellationToken);
            }
            await context.Entry(rule).Reference(r => r.SetCategory).LoadAsync(cancellationToken);
            return new RuleSaveResult(ToModel(rule), matches);
        }

        public async Task DeleteRuleAsync(string userId, int ruleId, CancellationToken cancellationToken = default)
        {
            var rule = await context.CategoryRules.FirstOrDefaultAsync(r => r.CategoryRuleId == ruleId && r.UserId == userId, cancellationToken)
                ?? throw new NotFoundException("Rule", ruleId);
            context.CategoryRules.Remove(rule);
            await context.SaveChangesAsync(cancellationToken);
            // What the rule placed goes back to the automatic rules
            await classifier.ClassifyAsync(userId, reclassifyAll: true, cancellationToken);
        }

        public async Task<int> PreviewRuleAsync(string userId, CategoryRuleModel model, CancellationToken cancellationToken = default)
        {
            var rule = new CategoryRule
            {
                Field = model.Field,
                Match = model.Match,
                Value = model.Value ?? "",
                Direction = model.Direction,
                SetCategoryId = model.SetCategoryId,
                SetTreatment = model.SetTreatment
            };
            var rows = await Mine(userId)
                .Select(t => new BankTransaction
                {
                    Amount = t.Amount,
                    MerchantKey = t.MerchantKey,
                    Name = t.Name,
                    PfcDetailed = t.PfcDetailed,
                    CategorySource = t.CategorySource,
                    TreatmentSource = t.TreatmentSource
                })
                .ToListAsync(cancellationToken);
            // Transactions set by hand keep the person's choice, so they don't count
            return rows.Count(t => TransactionClassifier.Matches(rule, t)
                && ((rule.SetCategoryId != null && t.CategorySource != ClassificationSource.User)
                    || (rule.SetTreatment != null && t.TreatmentSource != ClassificationSource.User)));
        }

        private static IQueryable<BankTransaction> WithDetails(IQueryable<BankTransaction> query) =>
            query.Include(t => t.FinancialAccount).Include(t => t.Category).Include(t => t.Splits).ThenInclude(s => s.Category);

        private async Task EnsureCategoryAsync(string userId, int categoryId, CancellationToken cancellationToken)
        {
            if (!await context.FinancialCategories.AnyAsync(c => c.FinancialCategoryId == categoryId && c.UserId == userId, cancellationToken))
            {
                throw new NotFoundException("Category", categoryId);
            }
        }

        private async Task ReplaceSplitsAsync(string userId, BankTransaction t, List<SplitInput> splits, CancellationToken cancellationToken)
        {
            context.TransactionSplits.RemoveRange(t.Splits);
            t.Splits.Clear();
            if (splits.Count == 0)
            {
                return;
            }
            if (splits.Count < 2)
            {
                throw Invalid("Splits", "A split needs at least two lines.");
            }
            var sum = splits.Sum(s => s.Amount);
            if (Math.Abs(sum - t.Amount) > 0.005m)
            {
                throw Invalid("Splits", $"The split adds up to {sum:0.00}, not {t.Amount:0.00}.");
            }
            foreach (var s in splits)
            {
                if (s.CategoryId is int id)
                {
                    await EnsureCategoryAsync(userId, id, cancellationToken);
                }
                t.Splits.Add(new TransactionSplit
                {
                    CategoryId = s.CategoryId,
                    Amount = Math.Round(s.Amount, 2),
                    Notes = string.IsNullOrWhiteSpace(s.Notes) ? null : s.Notes.Trim()
                });
            }
        }

        private static CustomValidationException Invalid(string field, string message) =>
            new([new FluentValidation.Results.ValidationFailure(field, message)]);

        private static CategoryRuleModel ToModel(CategoryRule r) => new()
        {
            CategoryRuleId = r.CategoryRuleId,
            Field = r.Field,
            Match = r.Match,
            Value = r.Value,
            Direction = r.Direction,
            SetCategoryId = r.SetCategoryId,
            SetCategoryName = r.SetCategory?.Name,
            SetTreatment = r.SetTreatment,
            Priority = r.Priority
        };
    }
}
