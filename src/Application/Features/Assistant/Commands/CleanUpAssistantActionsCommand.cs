using Elysian.Application.Interfaces;
using MediatR;

namespace Elysian.Application.Features.Assistant.Commands
{
    /// <summary>
    /// Removes the current tenant's old assistant proposals and finished changes, which are kept a week.
    /// Run as a tenant job, so it is only ever queued for tenants with the assistant module.
    /// </summary>
    /// <returns>What was done, in a line for the log</returns>
    public record CleanUpAssistantActionsCommand : IRequest<string>;

    public class CleanUpAssistantActionsCommandHandler(IAssistantActionService actions) : IRequestHandler<CleanUpAssistantActionsCommand, string>
    {
        public const int KeepDays = 7;

        public async Task<string> Handle(CleanUpAssistantActionsCommand request, CancellationToken cancellationToken) =>
            $"Removed {await actions.CleanupAsync(KeepDays, cancellationToken)} old assistant action(s)";
    }
}
