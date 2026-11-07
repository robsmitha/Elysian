using System.Text.Json.Serialization;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Security;
using FluentValidation;
using MediatR;

namespace Elysian.Application.Features.Financial.Commands
{
    /// <summary>The accounts page: banks, accounts, balances, health and recent syncs, from SQL.</summary>
    [Authorize(Policy = PolicyNames.BudgetRead)]
    public record GetConnectionsQuery : IRequest<ConnectionsResponse>;

    public class GetConnectionsQueryHandler(IConnectionService connections, IClaimsPrincipalAccessor principal)
        : IRequestHandler<GetConnectionsQuery, ConnectionsResponse>
    {
        public Task<ConnectionsResponse> Handle(GetConnectionsQuery request, CancellationToken cancellationToken) =>
            connections.GetConnectionsAsync(principal.UserId!, cancellationToken);
    }

    /// <summary>Finishes linking a bank from Plaid Link's onSuccess. Refuses a bank that's already linked (409).</summary>
    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record ExchangePlaidPublicTokenCommand(string PublicToken, string? InstitutionId, List<LinkedAccountInput>? Accounts) : IRequest<ExchangePlaidPublicTokenResult>;

    public record ExchangePlaidPublicTokenResult(int InstitutionAccessItemId);

    public class ExchangePlaidPublicTokenCommandValidator : AbstractValidator<ExchangePlaidPublicTokenCommand>
    {
        public ExchangePlaidPublicTokenCommandValidator()
        {
            RuleFor(c => c.PublicToken).NotEmpty().MaximumLength(500);
            RuleFor(c => c.InstitutionId).MaximumLength(100);
            RuleFor(c => c.Accounts).Must(a => a == null || a.Count <= 50).WithMessage("Too many accounts.");
        }
    }

    public class ExchangePlaidPublicTokenCommandHandler(IConnectionService connections, IClaimsPrincipalAccessor principal)
        : IRequestHandler<ExchangePlaidPublicTokenCommand, ExchangePlaidPublicTokenResult>
    {
        public async Task<ExchangePlaidPublicTokenResult> Handle(ExchangePlaidPublicTokenCommand request, CancellationToken cancellationToken)
        {
            var id = await connections.ExchangePublicTokenAsync(principal.UserId!,
                new ExchangePublicTokenRequest(request.PublicToken, request.InstitutionId, request.Accounts), cancellationToken);
            return new ExchangePlaidPublicTokenResult(id);
        }
    }

    /// <summary>"Sync now": queues a sync, limited per bank and per person (429 when hit).</summary>
    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record SyncConnectionCommand(int InstitutionAccessItemId) : IRequest<SyncRequestResult>;

    public class SyncConnectionCommandHandler(IConnectionService connections, IClaimsPrincipalAccessor principal)
        : IRequestHandler<SyncConnectionCommand, SyncRequestResult>
    {
        public Task<SyncRequestResult> Handle(SyncConnectionCommand request, CancellationToken cancellationToken) =>
            connections.RequestSyncAsync(principal.UserId!, request.InstitutionAccessItemId, cancellationToken);
    }

    /// <summary>Called after Plaid Link update mode succeeds (sign in again, renew consent, or new accounts).</summary>
    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record CompleteReconnectCommand(int InstitutionAccessItemId) : IRequest<Unit>;

    public class CompleteReconnectCommandHandler(IConnectionService connections, IClaimsPrincipalAccessor principal)
        : IRequestHandler<CompleteReconnectCommand, Unit>
    {
        public async Task<Unit> Handle(CompleteReconnectCommand request, CancellationToken cancellationToken)
        {
            await connections.CompleteReconnectAsync(principal.UserId!, request.InstitutionAccessItemId, cancellationToken);
            return Unit.Value;
        }
    }

    /// <summary>Removes a bank: Plaid access is revoked first. Keeping history (the default, D5) leaves past transactions.</summary>
    [Authorize(Policy = PolicyNames.BudgetDelete)]
    public record RemoveConnectionCommand(int InstitutionAccessItemId, bool KeepHistory = true) : IRequest<Unit>;

    public class RemoveConnectionCommandHandler(IConnectionService connections, IClaimsPrincipalAccessor principal)
        : IRequestHandler<RemoveConnectionCommand, Unit>
    {
        public async Task<Unit> Handle(RemoveConnectionCommand request, CancellationToken cancellationToken)
        {
            await connections.RemoveAsync(principal.UserId!, request.InstitutionAccessItemId, request.KeepHistory, cancellationToken);
            return Unit.Value;
        }
    }

    /// <summary>How one account counts toward budgets (spending, income only, savings, ignore).</summary>
    [Authorize(Policy = PolicyNames.BudgetWrite)]
    public record UpdateAccountSettingsCommand(int FinancialAccountId, [property: JsonConverter(typeof(JsonStringEnumConverter))] AccountBudgetRole BudgetRole) : IRequest<Unit>;

    public class UpdateAccountSettingsCommandValidator : AbstractValidator<UpdateAccountSettingsCommand>
    {
        public UpdateAccountSettingsCommandValidator()
        {
            RuleFor(c => c.BudgetRole).IsInEnum();
        }
    }

    public class UpdateAccountSettingsCommandHandler(IConnectionService connections, IClaimsPrincipalAccessor principal)
        : IRequestHandler<UpdateAccountSettingsCommand, Unit>
    {
        public async Task<Unit> Handle(UpdateAccountSettingsCommand request, CancellationToken cancellationToken)
        {
            await connections.UpdateAccountRoleAsync(principal.UserId!, request.FinancialAccountId, request.BudgetRole, cancellationToken);
            return Unit.Value;
        }
    }

    /// <summary>A webhook from Plaid. Not signed in: it's trusted only after its signature checks out.</summary>
    public record HandlePlaidWebhookCommand(byte[] Body, string? VerificationJwt) : IRequest<WebhookResult>;

    public class HandlePlaidWebhookCommandHandler(IPlaidWebhookService webhooks) : IRequestHandler<HandlePlaidWebhookCommand, WebhookResult>
    {
        public Task<WebhookResult> Handle(HandlePlaidWebhookCommand request, CancellationToken cancellationToken) =>
            webhooks.HandleAsync(request.Body, request.VerificationJwt, cancellationToken);
    }
}
