using Elysian.Application.Features.Assistant.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Security;
using FluentValidation;
using MediatR;

namespace Elysian.Application.Features.Assistant.Commands
{
    /// <summary>
    /// The person tapped Confirm on a proposal card (docs/assistant-actions-plan.md §4). Needs budget write. Claude never
    /// calls this; only the drawer does.
    /// </summary>
    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record ConfirmAssistantActionCommand(Guid ActionId, bool? IncludeRule) : IRequest<AssistantActionResult>;

    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record CancelAssistantActionCommand(Guid ActionId) : IRequest<AssistantActionResult>;

    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record UndoAssistantActionCommand(Guid ActionId) : IRequest<AssistantActionResult>;

    public class ConfirmAssistantActionCommandValidator : AbstractValidator<ConfirmAssistantActionCommand>
    {
        public ConfirmAssistantActionCommandValidator() => RuleFor(c => c.ActionId).NotEmpty();
    }

    public class CancelAssistantActionCommandValidator : AbstractValidator<CancelAssistantActionCommand>
    {
        public CancelAssistantActionCommandValidator() => RuleFor(c => c.ActionId).NotEmpty();
    }

    public class UndoAssistantActionCommandValidator : AbstractValidator<UndoAssistantActionCommand>
    {
        public UndoAssistantActionCommandValidator() => RuleFor(c => c.ActionId).NotEmpty();
    }

    public class ConfirmAssistantActionCommandHandler(IAssistantActionService actions, IClaimsPrincipalAccessor principal)
        : IRequestHandler<ConfirmAssistantActionCommand, AssistantActionResult>
    {
        public Task<AssistantActionResult> Handle(ConfirmAssistantActionCommand request, CancellationToken cancellationToken) =>
            actions.ConfirmAsync(principal.UserId!, request.ActionId, request.IncludeRule, cancellationToken);
    }

    public class CancelAssistantActionCommandHandler(IAssistantActionService actions, IClaimsPrincipalAccessor principal)
        : IRequestHandler<CancelAssistantActionCommand, AssistantActionResult>
    {
        public Task<AssistantActionResult> Handle(CancelAssistantActionCommand request, CancellationToken cancellationToken) =>
            actions.CancelAsync(principal.UserId!, request.ActionId, cancellationToken);
    }

    public class UndoAssistantActionCommandHandler(IAssistantActionService actions, IClaimsPrincipalAccessor principal)
        : IRequestHandler<UndoAssistantActionCommand, AssistantActionResult>
    {
        public Task<AssistantActionResult> Handle(UndoAssistantActionCommand request, CancellationToken cancellationToken) =>
            actions.UndoAsync(principal.UserId!, request.ActionId, cancellationToken);
    }
}
