using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Elysian.Application.Features.Financial.Commands
{
    public class UpdateBudgetCategoryCommand(int budgetId, string categoryName, decimal estimate) : IRequest<BudgetCategoryModel>
    {
        public int BudgetId { get; } = budgetId;
        public string CategoryName { get; } = categoryName;
        public decimal Estimate { get; } = estimate;

        public class Handler(ILogger<UpdateBudgetCategoryCommand> logger, IBudgetService budgetService, ICategoryService categoryService,
            IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<UpdateBudgetCategoryCommand, BudgetCategoryModel>
        {
            public async Task<BudgetCategoryModel> Handle(UpdateBudgetCategoryCommand request, CancellationToken cancellationToken)
            {
                var category = await categoryService.GetCategoryByNameAsync(request.CategoryName);
                return await budgetService.UpdateBudgetCategoryEstimateAsync(claimsPrincipalAccessor.UserId, request.BudgetId, category.FinancialCategoryId, request.Estimate);
            }
        }
    }
}
