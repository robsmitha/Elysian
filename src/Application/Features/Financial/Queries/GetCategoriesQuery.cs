using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using MediatR;

namespace Elysian.Application.Features.Financial.Queries
{
    public record GetCategoriesQuery : IRequest<List<FinancialCategoryModel>>;
    public class Handler(ICategoryService categoryService) : IRequestHandler<GetCategoriesQuery, List<FinancialCategoryModel>>
    {
        public async Task<List<FinancialCategoryModel>> Handle(GetCategoriesQuery request, CancellationToken cancellationToken)
        {
            return await categoryService.GetCategoriesAsync();
        }
    }
}
