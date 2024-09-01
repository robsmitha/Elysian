using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Elysian.Application.Features.Financial.Commands
{

    [Authorize]
    public record UpdateBudgetCategoryCommand(int BudgetId, string CategoryName, decimal Estimate) : IRequest<BudgetCategoryModel>;

    public class UpdateBudgetCategoryCommandHandler(ILogger<UpdateBudgetCategoryCommand> logger, IBudgetService budgetService, ICategoryService categoryService,
            IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<UpdateBudgetCategoryCommand, BudgetCategoryModel>
    {
        public async Task<BudgetCategoryModel> Handle(UpdateBudgetCategoryCommand request, CancellationToken cancellationToken)
        {
            var category = await categoryService.GetCategoryByNameAsync(request.CategoryName);
            return await budgetService.UpdateBudgetCategoryEstimateAsync(claimsPrincipalAccessor.UserId, request.BudgetId, category.FinancialCategoryId, request.Estimate);
        }
    }
}
