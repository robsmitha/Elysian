using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using Elysian.Infrastructure.Context;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Elysian.Application.Features.Financial.Commands
{
    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record SaveBudgetCommand(BudgetModel Budget) : IRequest<BudgetModel>;

    public class SaveBudgetCommandValidator : AbstractValidator<SaveBudgetCommand>
    {
        private readonly ElysianContext _context;
        public SaveBudgetCommandValidator(ElysianContext context)
        {
            _context = context;

            RuleFor(v => v.Budget)
                .NotEmpty()
                .MustAsync(HaveUniqueNameAsync)
                    .WithMessage("Budget name already exists.");
        }

        public async Task<bool> HaveUniqueNameAsync(BudgetModel budget,
            CancellationToken cancellationToken)
        {
            return await _context.Budgets.AllAsync(c => !string.Equals(c.Name, budget.Name)
                || (string.Equals(c.Name, budget.Name) && c.BudgetId == budget.BudgetId));
        }
    }

    public class SaveBudgetCommandHandler(IBudgetService budgetService, IClaimsPrincipalAccessor claimsPrincipalAccessor) : IRequestHandler<SaveBudgetCommand, BudgetModel>
    {
        public async Task<BudgetModel> Handle(SaveBudgetCommand request, CancellationToken cancellationToken)
        {

            return request.Budget.IsExisting
                ? await budgetService.UpdateBudgetAsync(claimsPrincipalAccessor.UserId, request.Budget)
                : await budgetService.AddBudgetAsync(claimsPrincipalAccessor.UserId, request.Budget);
        }

    }
}
