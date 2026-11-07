using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using MediatR;

namespace Elysian.Application.Features.Financial.Queries
{
    /// <summary>The signed-in person's categories (created from the defaults the first time).</summary>
    [Authorize(Policy = PolicyNames.BudgetRead)]
    public record GetCategoriesQuery(bool IncludeArchived = false) : IRequest<List<FinancialCategoryModel>>;

    public class Handler(ICategoryService categoryService, IClaimsPrincipalAccessor claimsPrincipalAccessor)
        : IRequestHandler<GetCategoriesQuery, List<FinancialCategoryModel>>
    {
        public async Task<List<FinancialCategoryModel>> Handle(GetCategoriesQuery request, CancellationToken cancellationToken) =>
            await categoryService.GetCategoriesAsync(claimsPrincipalAccessor.UserId!, request.IncludeArchived);
    }
}
