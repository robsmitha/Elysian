using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Elysian.Application.Features.Financial.Commands
{
    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record UpdateBudgetCategoryCommand(int BudgetId, string CategoryName, decimal Estimate) : IRequest<BudgetCategoryModel>;

    public class UpdateBudgetCategoryCommandHandler(ILogger<UpdateBudgetCategoryCommand> logger, IBudgetService budgetService, ICategoryService categoryService,
            IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<UpdateBudgetCategoryCommand, BudgetCategoryModel>
    {
        public async Task<BudgetCategoryModel> Handle(UpdateBudgetCategoryCommand request, CancellationToken cancellationToken)
        {
            var userId = claimsPrincipalAccessor.UserId!;
            // Looked up among the person's own categories (names are unique per person)
            var category = await categoryService.GetCategoryByNameAsync(userId, request.CategoryName)
                ?? throw new Exceptions.NotFoundException("Category", request.CategoryName);
            return await budgetService.UpdateBudgetCategoryEstimateAsync(userId, request.BudgetId, category.FinancialCategoryId, request.Estimate);
        }
    }
}
