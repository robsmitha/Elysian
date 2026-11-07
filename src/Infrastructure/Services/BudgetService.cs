using AutoMapper;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Infrastructure.Services
{
    /// <summary>
    /// Budgets belong to the person who made them. Every method that takes a budget id checks that first, and a budget
    /// that doesn't exist or isn't theirs is reported as not found.
    /// </summary>
    public class BudgetService(ElysianContext context, IMapper mapper) : IBudgetService
    {
        public async Task<List<BudgetModel>> GetBudgetsAsync(string userId)
        {
            var budgets = await context.Budgets.Where(b => b.UserId == userId).ToListAsync();
            return mapper.Map<List<BudgetModel>>(budgets);
        }

        /// <summary>The budget, or null when it doesn't exist or belongs to someone else.</summary>
        public async Task<BudgetModel> GetBudgetAsync(string userId, int budgetId)
        {
            var budget = await context.Budgets.FirstOrDefaultAsync(b => b.BudgetId == budgetId && b.UserId == userId);
            return budget == null ? null : mapper.Map<BudgetModel>(budget);
        }

        public async Task<List<BudgetCategoryModel>> GetBudgetCategoriesAsync(string userId, int budgetId)
        {
            await EnsureOwnerAsync(userId, budgetId);
            var budgetCategories = await context.BudgetCategories
                .Include(i => i.FinancialCategory).Where(b => b.BudgetId == budgetId).ToListAsync();
            return mapper.Map<List<BudgetCategoryModel>>(budgetCategories);
        }

        public async Task<BudgetModel> AddBudgetAsync(string userId, BudgetModel model)
        {
            var accessItemIds = await OwnedAccessItemIdsAsync(userId, model.BudgetAccessItems);

            var budget = new Budget
            {
                Name = model.Name,
                StartDate = model.StartDate,
                EndDate = model.EndDate,
                UserId = userId
            };
            await context.AddAsync(budget);
            await context.SaveChangesAsync();

            foreach (var categoryId in CategoryIds(model).Distinct())
            {
                await context.AddAsync(new BudgetCategory { BudgetId = budget.BudgetId, FinancialCategoryId = categoryId });
            }

            foreach (var accessItemId in accessItemIds)
            {
                await context.AddAsync(new BudgetAccessItem { BudgetId = budget.BudgetId, InstitutionAccessItemId = accessItemId });
            }
            await context.SaveChangesAsync();

            return mapper.Map<BudgetModel>(budget);
        }

        public async Task<BudgetModel> UpdateBudgetAsync(string userId, BudgetModel model)
        {
            var budget = await context.Budgets.FirstOrDefaultAsync(b => b.BudgetId == model.BudgetId && b.UserId == userId)
                ?? throw new NotFoundException("Budget", model.BudgetId);
            var newAccessItemIds = await OwnedAccessItemIdsAsync(userId, model.BudgetAccessItems);

            budget.Name = model.Name;
            budget.StartDate = model.StartDate;
            budget.EndDate = model.EndDate;

            // Categories: only rows in this budget are touched
            var newCategoryIds = CategoryIds(model).ToHashSet();
            var oldCategories = await context.BudgetCategories.Where(c => c.BudgetId == budget.BudgetId).ToListAsync();
            context.RemoveRange(oldCategories.Where(c => !newCategoryIds.Contains(c.FinancialCategoryId)));
            foreach (var categoryId in newCategoryIds.Except(oldCategories.Select(c => c.FinancialCategoryId)))
            {
                await context.AddAsync(new BudgetCategory { BudgetId = budget.BudgetId, FinancialCategoryId = categoryId });
            }

            // Banks: only rows in this budget are touched
            var oldAccessItems = await context.BudgetAccessItems.Where(c => c.BudgetId == budget.BudgetId).ToListAsync();
            context.RemoveRange(oldAccessItems.Where(a => !newAccessItemIds.Contains(a.InstitutionAccessItemId)));
            foreach (var accessItemId in newAccessItemIds.Except(oldAccessItems.Select(a => a.InstitutionAccessItemId)))
            {
                await context.AddAsync(new BudgetAccessItem { BudgetId = budget.BudgetId, InstitutionAccessItemId = accessItemId });
            }

            await context.SaveChangesAsync();
            return mapper.Map<BudgetModel>(budget);
        }

        public async Task<BudgetCategoryModel> UpdateBudgetCategoryEstimateAsync(string userId, int budgetId, int categoryId, decimal estimate)
        {
            await EnsureOwnerAsync(userId, budgetId);
            var budgetCategory = await context.BudgetCategories
                .FirstOrDefaultAsync(b => b.BudgetId == budgetId && b.FinancialCategoryId == categoryId);
            if (budgetCategory == null)
            {
                budgetCategory = new BudgetCategory { BudgetId = budgetId, FinancialCategoryId = categoryId };
                await context.AddAsync(budgetCategory);
            }
            budgetCategory.Estimate = estimate;
            await context.SaveChangesAsync();
            return mapper.Map<BudgetCategoryModel>(budgetCategory);
        }

        /// <summary>Adds a category to a budget; if it's already there, only a given estimate is applied.</summary>
        public async Task AddBudgetCategoryAsync(string userId, int budgetId, int categoryId, decimal? estimate = null)
        {
            await EnsureOwnerAsync(userId, budgetId);
            var existing = await context.BudgetCategories.FirstOrDefaultAsync(b => b.BudgetId == budgetId && b.FinancialCategoryId == categoryId);
            if (existing != null)
            {
                if (estimate.HasValue)
                {
                    existing.Estimate = estimate.Value;
                    await context.SaveChangesAsync();
                }
                return;
            }

            await context.BudgetCategories.AddAsync(new BudgetCategory
            {
                BudgetId = budgetId,
                FinancialCategoryId = categoryId,
                Estimate = estimate ?? 0
            });
            await context.SaveChangesAsync();
        }

        /// <summary>Excludes a transaction from a budget. Excluding one that's already excluded changes nothing.</summary>
        public async Task<BudgetExcludedTransactionModel> SetExcludedTransactionAsync(string userId, int budgetId, string transactionId)
        {
            await EnsureOwnerAsync(userId, budgetId);
            var excludedTransaction = await context.BudgetExcludedTransactions.FirstOrDefaultAsync(t => t.BudgetId == budgetId && t.TransactionId == transactionId);
            if (excludedTransaction == null)
            {
                excludedTransaction = new BudgetExcludedTransaction
                {
                    BudgetId = budgetId,
                    TransactionId = transactionId
                };
                await context.AddAsync(excludedTransaction);
                await context.SaveChangesAsync();
            }

            return mapper.Map<BudgetExcludedTransactionModel>(excludedTransaction);
        }

        public async Task<bool> RestoreExcludedTransactionAsync(string userId, int budgetId, string transactionId)
        {
            await EnsureOwnerAsync(userId, budgetId);
            var excludedTransaction = await context.BudgetExcludedTransactions.FirstOrDefaultAsync(t => t.BudgetId == budgetId && t.TransactionId == transactionId);
            if (excludedTransaction == null)
            {
                return false;
            }

            context.BudgetExcludedTransactions.Remove(excludedTransaction);
            await context.SaveChangesAsync();
            return true;
        }

        public async Task<List<BudgetExcludedTransactionModel>> GetExcludedTransactionsAsync(string userId, int budgetId)
        {
            await EnsureOwnerAsync(userId, budgetId);
            var excludedTransactions = await context.BudgetExcludedTransactions.Where(e => e.BudgetId == budgetId).ToListAsync();
            return mapper.Map<List<BudgetExcludedTransactionModel>>(excludedTransactions);
        }

        private async Task EnsureOwnerAsync(string userId, int budgetId)
        {
            if (!await context.Budgets.AnyAsync(b => b.BudgetId == budgetId && b.UserId == userId))
            {
                throw new NotFoundException("Budget", budgetId);
            }
        }

        // Category ids from the request; the "Uncategorized" placeholder (-1) and blanks are left out
        private static IEnumerable<int> CategoryIds(BudgetModel model) =>
            (model.Categories ?? []).Where(c => c != null && c.FinancialCategoryId > 0).Select(c => c.FinancialCategoryId);

        /// <summary>The requested banks, after checking each one was linked by this person.</summary>
        private async Task<HashSet<int>> OwnedAccessItemIdsAsync(string userId, List<InstitutionAccessItemModel> requested)
        {
            var requestedIds = (requested ?? []).Where(a => a != null).Select(a => a.InstitutionAccessItemId).ToHashSet();
            if (requestedIds.Count == 0)
            {
                return requestedIds;
            }

            var ownedIds = await context.InstitutionAccessItemUsers
                .Where(u => u.User.ExternalUserId == userId && requestedIds.Contains(u.InstitutionAccessItemId))
                .Select(u => u.InstitutionAccessItemId)
                .ToListAsync();
            if (ownedIds.Distinct().Count() != requestedIds.Count)
            {
                throw new ForbiddenAccessException();
            }
            return requestedIds;
        }
    }
}
