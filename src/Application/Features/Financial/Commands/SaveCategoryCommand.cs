using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Financial.Commands
{
    /// <summary>Adds or renames one of the person's categories; with a budget id, also adds it to that budget.</summary>
    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record SaveCategoryCommand(FinancialCategoryModel Category, int BudgetId, decimal? Estimate = null) : IRequest<FinancialCategoryModel>;

    public class SaveCategoryCommandValidator : AbstractValidator<SaveCategoryCommand>
    {
        private readonly ElysianContext _context;
        private readonly IClaimsPrincipalAccessor _principal;

        public SaveCategoryCommandValidator(ElysianContext context, IClaimsPrincipalAccessor principal)
        {
            _context = context;
            _principal = principal;

            RuleFor(v => v.Category).NotEmpty();
            RuleFor(v => v.Category.Name).NotEmpty().MaximumLength(100).When(v => v.Category != null);
            RuleFor(v => v.Category)
                .MustAsync(HaveUniqueNameAsync)
                    .WithMessage("You already have a category with that name.")
                .When(v => v.Category != null && !string.IsNullOrWhiteSpace(v.Category.Name));
        }

        // Names only need to be unique within one person's own categories
        public async Task<bool> HaveUniqueNameAsync(FinancialCategoryModel category, CancellationToken cancellationToken)
        {
            var name = category.Name.Trim();
            return !await _context.FinancialCategories.AnyAsync(c => c.UserId == _principal.UserId
                && c.Name == name && c.FinancialCategoryId != category.FinancialCategoryId && !c.IsArchived, cancellationToken);
        }
    }

    public class SaveCategoryCommandHandler(ICategoryService categoryService, IBudgetService budgetService,
        IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<SaveCategoryCommand, FinancialCategoryModel>
    {
        public async Task<FinancialCategoryModel> Handle(SaveCategoryCommand request, CancellationToken cancellationToken)
        {
            var userId = claimsPrincipalAccessor.UserId!;
            var model = request.Category.IsExisting
                ? await categoryService.UpdateCategoryAsync(userId, request.Category)
                : await categoryService.AddCategoryAsync(userId, request.Category);

            if (request.BudgetId > 0)
            {
                await budgetService.AddBudgetCategoryAsync(userId, request.BudgetId, model.FinancialCategoryId, request.Estimate);
            }
            return model;
        }
    }
}
