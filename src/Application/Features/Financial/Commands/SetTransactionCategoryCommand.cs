using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using MediatR;

namespace Elysian.Application.Features.Financial.Commands
{
    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record SetTransactionCategoryCommand(string TransactionId, int CategoryId, int BudgetId) : IRequest<TransactionCategoryModel>;

    public class SetTransactionCategoryCommandHandler(ICategoryService categoryService, IBudgetService budgetService, IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<SetTransactionCategoryCommand, TransactionCategoryModel>
    {
        public async Task<TransactionCategoryModel> Handle(SetTransactionCategoryCommand request, CancellationToken cancellationToken)
        {
            var budgetCategories = await budgetService.GetBudgetCategoriesAsync(claimsPrincipalAccessor.UserId, request.BudgetId);

            if (!budgetCategories.Any(c => c.FinancialCategoryId == request.CategoryId))
            {
                await budgetService.AddBudgetCategoryAsync(claimsPrincipalAccessor.UserId, request.BudgetId, request.CategoryId);
            }

            return await categoryService.SetTransactionCategoryAsync(claimsPrincipalAccessor.UserId, request.TransactionId, request.CategoryId, request.BudgetId);
        }
    }
}
