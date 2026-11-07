using Elysian.Application.Features.Assistant.Models;

namespace Elysian.Application.Interfaces
{
    /// <summary>The budget assistant (docs/assistant-plan.md): plain questions about the person's own money.</summary>
    public interface IAssistantService
    {
        /// <summary>Answers one question. Over a limit throws TooManyRequestsException; switched off throws NotFoundException.</summary>
        Task<AssistantReply> AskAsync(string userId, AskAssistantRequest request, CancellationToken cancellationToken = default);
        Task<AssistantUsageModel> GetUsageAsync(string userId, DateOnly? today, CancellationToken cancellationToken = default);
    }
}
