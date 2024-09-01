using AutoMapper;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Financial.Commands
{
    [Authorize]
    public record SaveCategoryCommand(FinancialCategoryModel Category, int BudgetId, decimal? Estimate = null) : IRequest<FinancialCategoryModel>;

    public class SaveCategoryCommandValidator : AbstractValidator<SaveCategoryCommand>
    {
        private readonly ElysianContext _context;
        public SaveCategoryCommandValidator(ElysianContext context)
        {
            _context = context;

            RuleFor(v => v.Category)
                .NotEmpty()
                .MustAsync(HaveUniqueNameAsync)
                    .WithMessage("Category name already exists.");
        }

        public async Task<bool> HaveUniqueNameAsync(FinancialCategoryModel category,
            CancellationToken cancellationToken)
        {
            return await _context.FinancialCategories.AllAsync(c => !string.Equals(c.Name, category.Name)
                || (string.Equals(c.Name, category.Name) && c.FinancialCategoryId == category.FinancialCategoryId));
        }
    }

    public class SaveCategoryCommandHandler(ICategoryService categoryService, IBudgetService budgetService, IMapper mapper,
        IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<SaveCategoryCommand, FinancialCategoryModel>
    {
        public async Task<FinancialCategoryModel> Handle(SaveCategoryCommand request, CancellationToken cancellationToken)
        {
            FinancialCategoryModel model;
            if (request.Category.IsExisting)
            {
                model = await categoryService.UpdateCategoryAsync(request.Category);
            }
            else
            {
                model = await categoryService.AddCategoryAsync(request.Category);
            }

            await budgetService.AddBudgetCategoryAsync(claimsPrincipalAccessor.UserId, request.BudgetId, model.FinancialCategoryId, request.Estimate);

            return model;
        }

    }
}
