using Elysian.Application.Features.Assistant.Models;

namespace Elysian.Application.Interfaces
{
    /// <summary>
    /// Confirming, cancelling and undoing the assistant's proposed changes (docs/assistant-actions-plan.md §4). Only the
    /// person's own; someone else's id is not found.
    /// </summary>
    public interface IAssistantActionService
    {
        /// <summary>
        /// Applies the proposal in one database transaction. <paramref name="includeRule"/> is the card's "also do this for
        /// future charges" tick box. Expired or cancelled throws ConflictException; changed since it was proposed returns
        /// outcome "stale" with the fresh proposal; confirming twice returns the first result.
        /// </summary>
        Task<AssistantActionResult> ConfirmAsync(string userId, Guid actionId, bool? includeRule, CancellationToken cancellationToken = default);
        Task<AssistantActionResult> CancelAsync(string userId, Guid actionId, CancellationToken cancellationToken = default);
        /// <summary>Puts things back, skipping anything the person changed since. Only within the undo window.</summary>
        Task<AssistantActionResult> UndoAsync(string userId, Guid actionId, CancellationToken cancellationToken = default);
        /// <summary>Removes proposals and finished actions older than <paramref name="days"/> days. Returns how many.</summary>
        Task<int> CleanupAsync(int days = 7, CancellationToken cancellationToken = default);
    }
}
