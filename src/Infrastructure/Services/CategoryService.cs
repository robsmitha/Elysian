using AutoMapper;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Financial.Categories;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Infrastructure.Services
{
    /// <summary>Each person's own categories. Every method only sees the given person's.</summary>
    public class CategoryService(ElysianContext context, IMapper mapper) : ICategoryService
    {
        private IQueryable<FinancialCategory> Mine(string userId) => context.FinancialCategories.Where(c => c.UserId == userId);

        public async Task<List<FinancialCategoryModel>> GetCategoriesAsync(string userId, bool includeArchived = false)
        {
            await EnsureDefaultsAsync(userId);
            var categories = await Mine(userId)
                .Where(c => includeArchived || !c.IsArchived)
                .OrderBy(c => c.Group).ThenBy(c => c.SortOrder).ThenBy(c => c.Name)
                .ToListAsync();
            return mapper.Map<List<FinancialCategoryModel>>(categories);
        }

        public async Task<FinancialCategoryModel?> GetCategoryAsync(string userId, int categoryId)
        {
            var category = await Mine(userId).FirstOrDefaultAsync(c => c.FinancialCategoryId == categoryId);
            return category == null ? null : mapper.Map<FinancialCategoryModel>(category);
        }

        public async Task<FinancialCategoryModel?> GetCategoryByNameAsync(string userId, string categoryName)
        {
            var category = await Mine(userId).FirstOrDefaultAsync(c => c.Name == categoryName);
            return category == null ? null : mapper.Map<FinancialCategoryModel>(category);
        }

        public async Task<FinancialCategoryModel> AddCategoryAsync(string userId, FinancialCategoryModel model)
        {
            var category = new FinancialCategory
            {
                UserId = userId,
                Name = model.Name.Trim(),
                Icon = model.Icon,
                Group = model.Group,
                SortOrder = 1000
            };
            await context.AddAsync(category);
            await context.SaveChangesAsync();
            return mapper.Map<FinancialCategoryModel>(category);
        }

        public async Task<FinancialCategoryModel> UpdateCategoryAsync(string userId, FinancialCategoryModel model)
        {
            var category = await Mine(userId).FirstOrDefaultAsync(c => c.FinancialCategoryId == model.FinancialCategoryId)
                ?? throw new NotFoundException("Category", model.FinancialCategoryId);
            category.Name = model.Name.Trim();
            category.Icon = model.Icon ?? category.Icon;
            category.Group = model.Group;
            await context.SaveChangesAsync();
            return mapper.Map<FinancialCategoryModel>(category);
        }

        public async Task ArchiveCategoryAsync(string userId, int categoryId, int? mergeIntoCategoryId)
        {
            var category = await Mine(userId).FirstOrDefaultAsync(c => c.FinancialCategoryId == categoryId)
                ?? throw new NotFoundException("Category", categoryId);

            if (mergeIntoCategoryId is int targetId && targetId != categoryId)
            {
                if (!await Mine(userId).AnyAsync(c => c.FinancialCategoryId == targetId && !c.IsArchived))
                {
                    throw new NotFoundException("Category", targetId);
                }

                // Transactions and splits keep who set them; only the category changes
                foreach (var t in await context.BankTransactions.Where(t => t.CategoryId == categoryId).ToListAsync())
                {
                    t.CategoryId = targetId;
                }
                foreach (var s in await context.TransactionSplits.Where(s => s.CategoryId == categoryId).ToListAsync())
                {
                    s.CategoryId = targetId;
                }
                foreach (var r in await context.CategoryRules.Where(r => r.UserId == userId && r.SetCategoryId == categoryId).ToListAsync())
                {
                    r.SetCategoryId = targetId;
                }

                // Budget lines: add the estimate to the target's line in the same budget, or move the line
                var lines = await context.BudgetCategories.Where(b => b.FinancialCategoryId == categoryId).ToListAsync();
                foreach (var line in lines)
                {
                    var existing = await context.BudgetCategories.FirstOrDefaultAsync(b => b.BudgetId == line.BudgetId && b.FinancialCategoryId == targetId);
                    if (existing != null)
                    {
                        existing.Estimate += line.Estimate;
                        context.BudgetCategories.Remove(line);
                    }
                    else
                    {
                        line.FinancialCategoryId = targetId;
                    }
                }

                // Monthly targets: same idea, month by month
                var targets = await context.BudgetTargets.Where(t => t.CategoryId == categoryId).ToListAsync();
                foreach (var t in targets)
                {
                    var existing = await context.BudgetTargets.FirstOrDefaultAsync(b => b.BudgetMonthId == t.BudgetMonthId && b.CategoryId == targetId);
                    if (existing != null)
                    {
                        existing.Amount += t.Amount;
                        context.BudgetTargets.Remove(t);
                    }
                    else
                    {
                        t.CategoryId = targetId;
                    }
                }
            }

            category.IsArchived = true;
            await context.SaveChangesAsync();
        }

        /// <summary>
        /// Pages that load at the same moment can each add the defaults, leaving two of each. The extras are merged into
        /// the oldest copy (their transactions, splits, rules and targets move) and archived.
        /// </summary>
        private async Task MergeDuplicateDefaultsAsync(string userId, List<FinancialCategory> mine)
        {
            var duplicates = mine.Where(c => c.SystemKey != null && !c.IsArchived)
                .GroupBy(c => c.SystemKey!)
                .Where(g => g.Count() > 1)
                .ToList();
            foreach (var g in duplicates)
            {
                var keep = g.Min(c => c.FinancialCategoryId);
                foreach (var extra in g.Where(c => c.FinancialCategoryId != keep))
                {
                    await ArchiveCategoryAsync(userId, extra.FinancialCategoryId, keep);
                    // Archived and no longer a default, so it isn't picked again
                    extra.SystemKey = null;
                }
            }
            if (duplicates.Count > 0)
            {
                await context.SaveChangesAsync();
            }
        }

        public async Task EnsureDefaultsAsync(string userId)
        {
            var mine = await Mine(userId).ToListAsync();
            // Done once: after that the person decides (an archived default stays archived)
            if (mine.Any(c => c.SystemKey != null))
            {
                await MergeDuplicateDefaultsAsync(userId, mine);
                return;
            }

            // Existing categories whose names match a default take its key, so Plaid's categories land in them
            var taken = new HashSet<string>();
            foreach (var category in mine.OrderBy(c => c.FinancialCategoryId))
            {
                var match = CategoryCatalog.ForName(category.Name);
                if (match != null && taken.Add(match.Key))
                {
                    category.SystemKey = match.Key;
                    category.Icon ??= match.Icon;
                    category.Group = match.Group;
                }
            }

            var order = 0;
            foreach (var d in CategoryCatalog.Defaults)
            {
                order++;
                var keyed = mine.FirstOrDefault(c => c.SystemKey == d.Key);
                if (keyed != null)
                {
                    keyed.SortOrder = order;
                    continue;
                }
                context.FinancialCategories.Add(new FinancialCategory
                {
                    UserId = userId,
                    Name = d.Name,
                    SystemKey = d.Key,
                    Icon = d.Icon,
                    Group = d.Group,
                    SortOrder = order
                });
            }
            await context.SaveChangesAsync();
        }
    }
}
