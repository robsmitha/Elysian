using AutoMapper;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Infrastructure.Services
{
    public class CategoryService(ElysianContext context, IMapper mapper) : ICategoryService
    {
        public async Task<List<FinancialCategoryModel>> GetCategoriesAsync()
        {
            var categories = await context.FinancialCategories.ToListAsync();
            return mapper.Map<List<FinancialCategoryModel>>(categories);
        }

        public async Task<FinancialCategoryModel> GetCategoryAsync(int categoryId)
        {
            var category = await context.FinancialCategories.FindAsync(categoryId);
            return mapper.Map<FinancialCategoryModel>(category);
        }

        public async Task<FinancialCategoryModel> GetCategoryByNameAsync(string categoryName)
        {
            var category = await context.FinancialCategories.FirstOrDefaultAsync(c => c.Name == categoryName);
            return mapper.Map<FinancialCategoryModel>(category);
        }

        public async Task<FinancialCategoryModel> AddCategoryAsync(FinancialCategoryModel model)
        {
            var category = new FinancialCategory
            {
                Name = model.Name
            };
            await context.AddAsync(category);
            await context.SaveChangesAsync();
            return mapper.Map<FinancialCategoryModel>(category);
        }

        public async Task<FinancialCategoryModel> UpdateCategoryAsync(FinancialCategoryModel model)
        {
            var category = await context.FinancialCategories.FindAsync(model.FinancialCategoryId);
            category.Name = model.Name;
            await context.SaveChangesAsync();
            return mapper.Map<FinancialCategoryModel>(category);
        }

        public async Task<List<TransactionCategoryModel>> GetTransactionCategoriesAsync(string userid, int budgetId)
        {
            var transactionCategories = await context.TransactionCategories
                .Include(c => c.FinancialCategory)
                .Where(c => c.BudgetId == budgetId)
                .ToListAsync();
            return mapper.Map<List<TransactionCategoryModel>>(transactionCategories);
        }

        public async Task<TransactionCategoryModel> SetTransactionCategoryAsync(string userid, string transactionId, int categoryId, int budgetId)
        {
            TransactionCategory transactionCategory = null;
            try
            {
                // get existing transaction category if any
                transactionCategory = await context.TransactionCategories
                    .SingleOrDefaultAsync(t => t.BudgetId == budgetId && t.TransactionId == transactionId);
            }
            catch (InvalidOperationException)
            {
                // multiple records, delete all and set new transaction category
                var duplicates = await context.TransactionCategories
                    .Where(t => t.BudgetId == budgetId && t.TransactionId == transactionId)
                    .ToListAsync();
                context.RemoveRange(duplicates);
                await context.SaveChangesAsync(); 
            }
            finally
            {
                if (transactionCategory == null)
                {
                    transactionCategory = new TransactionCategory
                    {
                        TransactionId = transactionId,
                        FinancialCategoryId = categoryId,
                        BudgetId = budgetId
                    };
                    await context.AddAsync(transactionCategory);
                }
                else
                {
                    transactionCategory.FinancialCategoryId = categoryId;
                }
                await context.SaveChangesAsync();
            }
            return mapper.Map<TransactionCategoryModel>(transactionCategory);
        }
    }
}
