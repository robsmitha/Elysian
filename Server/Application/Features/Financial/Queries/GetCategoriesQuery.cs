using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using MediatR;

namespace Elysian.Application.Features.Financial.Queries
{
    public class GetCategoriesQuery : IRequest<List<FinancialCategoryModel>>
    {
        public class Handler : IRequestHandler<GetCategoriesQuery, List<FinancialCategoryModel>>
        {
            private readonly ICategoryService _categoryService;

            public Handler(ICategoryService categoryService)
            {
                _categoryService = categoryService;
            }
            public async Task<List<FinancialCategoryModel>> Handle(GetCategoriesQuery request, CancellationToken cancellationToken)
            {
                return await _categoryService.GetCategoriesAsync();
            }
        }
    }
}
