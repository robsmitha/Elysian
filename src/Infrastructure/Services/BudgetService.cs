using AutoMapper;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Infrastructure.Services
{
    public class BudgetService(ElysianContext context, IMapper mapper) : IBudgetService
    {
        public async Task<List<BudgetModel>> GetBudgetsAsync(string userId)
        {
            var budgets = await context.Budgets.Where(b => b.UserId == userId).ToListAsync();
            return mapper.Map<List<BudgetModel>>(budgets);
        }

        public async Task<BudgetModel> GetBudgetAsync(string userId, int budgetId)
        {
            var budget = await context.Budgets.FindAsync(budgetId);
            var model = mapper.Map<BudgetModel>(budget);
            return model;
        }

        public async Task<List<BudgetCategoryModel>> GetBudgetCategoriesAsync(string userId, int budgetId)
        {
            var budgetCategories = await context.BudgetCategories
                .Include(i => i.FinancialCategory).Where(b => b.BudgetId == budgetId).ToListAsync();
            var model = mapper.Map<List<BudgetCategoryModel>>(budgetCategories);
            return model;
        }

        public async Task<BudgetModel> AddBudgetAsync(string userId, BudgetModel model)
        {
            var budget = new Budget
            {
                Name = model.Name,
                StartDate = model.StartDate,
                EndDate = model.EndDate,
                UserId = userId
            };
            await context.AddAsync(budget);
            await context.SaveChangesAsync();

            if (model.Categories?.Any() == true)
            {
                foreach (var category in model.Categories)
                {
                    await context.AddAsync(new BudgetCategory
                    {
                        BudgetId = budget.BudgetId,
                        FinancialCategoryId = category.FinancialCategoryId
                    });
                }
                await context.SaveChangesAsync();
            }

            if (model.BudgetAccessItems?.Any() == true)
            {
                foreach (var accessItem in model.BudgetAccessItems)
                {
                    await context.AddAsync(new BudgetAccessItem
                    {
                        BudgetId = budget.BudgetId,
                        InstitutionAccessItemId = accessItem.InstitutionAccessItemId
                    });
                }
                await context.SaveChangesAsync();
            }

            return mapper.Map<BudgetModel>(budget);
        }

        public async Task<BudgetModel> UpdateBudgetAsync(string userId, BudgetModel model)
        {
            var budget = await context.Budgets.FindAsync(model.BudgetId);

            budget.Name = model.Name;
            budget.StartDate = model.StartDate;
            budget.EndDate = model.EndDate;

            var newCategoryIds = model.Categories.Select(c => c.FinancialCategoryId);
            var oldCategories = await context.BudgetCategories.Where(c => c.BudgetId == model.BudgetId).ToListAsync();
            var oldCategoryIds = oldCategories.Select(c => c.FinancialCategoryId).ToList();

            if (model.Categories?.Count > 0)
            {
                var removeCategoryIds = oldCategoryIds.Except(newCategoryIds).ToList();
                foreach (var categoryId in removeCategoryIds)
                {
                    var budgetCategory = await context.BudgetCategories.FirstOrDefaultAsync(c => c.FinancialCategoryId == categoryId);
                    context.Remove(budgetCategory);
                }

                var addCategoryIds = newCategoryIds.Except(oldCategoryIds).ToList();
                foreach (var categoryId in addCategoryIds)
                {
                    await context.AddAsync(new BudgetCategory
                    {
                        BudgetId = budget.BudgetId,
                        FinancialCategoryId = categoryId
                    });
                }
            }
            else if (oldCategories.Any())
            {
                context.RemoveRange(oldCategories);
            }

            await context.SaveChangesAsync();
            return mapper.Map<BudgetModel>(budget);
        }

        public async Task<BudgetCategoryModel> UpdateBudgetCategoryEstimateAsync(string userId, int budgetId, int categoryId, decimal estimate)
        {
            var budgetCategory = await context.BudgetCategories
                .FirstOrDefaultAsync(b => b.BudgetId == budgetId && b.FinancialCategoryId == categoryId);
            budgetCategory.Estimate = estimate;
            await context.SaveChangesAsync();
            return mapper.Map<BudgetCategoryModel>(budgetCategory);
        }

        public async Task AddBudgetCategoryAsync(string userId, int budgetId, int categoryId, decimal? estimate = null)
        {
            await context.BudgetCategories.AddAsync(new BudgetCategory
            {
                BudgetId = budgetId,
                FinancialCategoryId = categoryId,
                Estimate = estimate ?? 0
            });
            await context.SaveChangesAsync();
        }

        public async Task<BudgetExcludedTransactionModel> SetExcludedTransactionAsync(string userId, int budgetId, string transactionId)
        {
            var excludedTransaction = await context.BudgetExcludedTransactions.FirstOrDefaultAsync(t => t.BudgetId == budgetId && t.TransactionId == transactionId);
            if (excludedTransaction == null)
            {
                excludedTransaction = new BudgetExcludedTransaction
                {
                    BudgetId = budgetId,
                    TransactionId = transactionId
                };
            }

            await context.AddAsync(excludedTransaction);
            await context.SaveChangesAsync();
            return mapper.Map<BudgetExcludedTransactionModel>(excludedTransaction);
        }

        public async Task<bool> RestoreExcludedTransactionAsync(string userId, int budgetId, string transactionId)
        {
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
            var excludedTransactions = await context.BudgetExcludedTransactions.Where(e => e.BudgetId == budgetId).ToListAsync();
            return mapper.Map<List<BudgetExcludedTransactionModel>>(excludedTransactions);
        }
    }
}
