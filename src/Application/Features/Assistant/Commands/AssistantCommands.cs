using Elysian.Application.Features.Assistant.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using FluentValidation;
using MediatR;

namespace Elysian.Application.Features.Assistant.Commands
{
    /// <summary>One question to the budget assistant. Only people who can read budgets; only their own data.</summary>
    [Authorize(Policy = PolicyNames.BudgetRead)]
    public record AskAssistantCommand(string Question, List<AssistantTurn>? History, DateOnly? Today, string? Page, string? PageMonth)
        : IRequest<AssistantReply>;

    public class AskAssistantCommandValidator : AbstractValidator<AskAssistantCommand>
    {
        public const int MaxQuestion = 500;
        public const int MaxHistory = 12;
        public const int MaxHistoryText = 1500;

        public AskAssistantCommandValidator()
        {
            RuleFor(c => c.Question).NotEmpty().WithMessage("Ask a question.")
                .MaximumLength(MaxQuestion).WithMessage($"Keep questions under {MaxQuestion} characters.");
            RuleFor(c => c.History).Must(h => h == null || h.Count <= MaxHistory).WithMessage("The conversation is too long; start a new one.");
            RuleForEach(c => c.History).ChildRules(turn =>
            {
                turn.RuleFor(t => t.Role).Must(r => r is "user" or "assistant").WithMessage("Unknown role.");
                turn.RuleFor(t => t.Text).NotNull().MaximumLength(MaxHistoryText);
            });
            RuleFor(c => c.Page).MaximumLength(40);
            RuleFor(c => c.PageMonth).Matches(@"^\d{4}-\d{2}$").When(c => c.PageMonth != null);
        }
    }

    public class AskAssistantCommandHandler(IAssistantService assistant, IClaimsPrincipalAccessor principal) : IRequestHandler<AskAssistantCommand, AssistantReply>
    {
        public Task<AssistantReply> Handle(AskAssistantCommand request, CancellationToken cancellationToken) =>
            assistant.AskAsync(principal.UserId!, new AskAssistantRequest(request.Question, request.History, request.Today, request.Page, request.PageMonth), cancellationToken);
    }

    [Authorize(Policy = PolicyNames.BudgetRead)]
    public record GetAssistantUsageQuery(DateOnly? Today) : IRequest<AssistantUsageModel>;

    public class GetAssistantUsageQueryHandler(IAssistantService assistant, IClaimsPrincipalAccessor principal) : IRequestHandler<GetAssistantUsageQuery, AssistantUsageModel>
    {
        public Task<AssistantUsageModel> Handle(GetAssistantUsageQuery request, CancellationToken cancellationToken) =>
            assistant.GetUsageAsync(principal.UserId!, request.Today, cancellationToken);
    }
}
