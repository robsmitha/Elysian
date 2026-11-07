using Elysian.Application.Exceptions;
using Elysian.Application.Features.Assistant.Models;
using Elysian.Application.Features.Financial.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Constants;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Elysian.Infrastructure.Assistant
{
    /// <summary>
    /// Confirming, cancelling and undoing the assistant's proposals (docs/assistant-actions-plan.md §4). Only reachable from
    /// the drawer's buttons, never from Claude. Changes go through the same services the Transactions, Budget and
    /// category pages use, in one database transaction.
    /// </summary>
    public class AssistantActions(
        ElysianContext context,
        AssistantTools tools,
        ITransactionService transactions,
        IBudgetMonthService budgets,
        ICategoryService categories,
        TimeProvider clock,
        ILogger<AssistantActions> logger) : IAssistantActionService
    {
        public const int MaxConfirmsPerDay = 20;
        public static readonly TimeSpan UndoWindow = TimeSpan.FromHours(24);

        public const string Expired = "This change has expired. Ask again to see it fresh.";
        public const string Couldnt = "That change couldn’t be made, so nothing was changed. Try again, or make it on the page.";

        public async Task<AssistantActionResult> ConfirmAsync(string userId, Guid actionId, bool? includeRule, CancellationToken cancellationToken = default)
        {
            var action = await FindAsync(userId, actionId, cancellationToken);
            var now = clock.GetUtcNow();

            switch (action.Status)
            {
                // A double tap, or a second tab: the first confirm's result
                case AssistantActionStatus.Confirmed:
                    return Result("done", action.ResultText ?? "Done.", action);
                case AssistantActionStatus.Undone:
                    throw new ConflictException("This change was undone. Ask again to make it.");
                case AssistantActionStatus.Cancelled:
                    throw new ConflictException("This change was cancelled. Ask again to see it fresh.");
                case AssistantActionStatus.Failed:
                    throw new ConflictException(Couldnt);
                case AssistantActionStatus.Expired:
                    throw new ConflictException(Expired);
            }
            if (now > action.ExpiresAt)
            {
                action.Status = AssistantActionStatus.Expired;
                await context.SaveChangesAsync(cancellationToken);
                throw new ConflictException(Expired);
            }

            var dayAgo = now.AddDays(-1);
            var confirmed = await context.AssistantActions.CountAsync(a => a.UserId == userId && a.ConfirmedAt >= dayAgo, cancellationToken);
            if (confirmed >= MaxConfirmsPerDay)
            {
                var oldest = await context.AssistantActions.Where(a => a.UserId == userId && a.ConfirmedAt >= dayAgo)
                    .MinAsync(a => a.ConfirmedAt!.Value, cancellationToken);
                throw new TooManyRequestsException($"That’s {MaxConfirmsPerDay} changes from the assistant today. You can still make changes on the pages.", oldest.AddDays(1));
            }

            // Nothing moved underneath it? Work it out again from today's data and compare.
            Proposal fresh;
            try
            {
                fresh = await tools.RebuildAsync(userId, action, cancellationToken);
            }
            catch (ToolInputException ex)
            {
                action.Status = AssistantActionStatus.Expired;
                await context.SaveChangesAsync(cancellationToken);
                throw new ConflictException($"This can’t be done any more: {ex.Message}");
            }
            if (fresh.Fingerprint != action.Fingerprint)
            {
                AssistantTools.Fill(action, fresh, now);
                await context.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Assistant action {Kind} was stale at confirm; proposed again", action.Kind);
                return Result("stale", "This changed since I proposed it. Check it and confirm again.", action);
            }

            var change = ActionJson.Read<ActionChange>(action.ChangeJson);
            var withRule = change.Rule != null && (includeRule ?? change.RuleTicked);
            await using (var tx = context.Database.IsRelational() ? await context.Database.BeginTransactionAsync(cancellationToken) : null)
            {
                try
                {
                    var (applied, text) = await ApplyAsync(userId, action.Kind, change, withRule, cancellationToken);
                    action.Status = AssistantActionStatus.Confirmed;
                    action.ConfirmedAt = now;
                    action.UndoUntil = action.CanUndo ? now + UndoWindow : null;
                    action.AppliedJson = ActionJson.Write(applied);
                    action.ResultText = text.Length > 300 ? text[..300] : text;
                    await context.SaveChangesAsync(cancellationToken);
                    if (tx != null) await tx.CommitAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (tx != null) await tx.RollbackAsync(CancellationToken.None);
                    logger.LogError(ex, "Assistant action {Kind} failed to apply", action.Kind);
                    await MarkFailedAsync(actionId);
                    throw new ConflictException(Couldnt);
                }
            }
            logger.LogInformation("Assistant action {Kind} confirmed, {Count} affected, rule={Rule}", action.Kind, action.AffectedCount, withRule);
            return Result("done", action.ResultText!, action);
        }

        public async Task<AssistantActionResult> CancelAsync(string userId, Guid actionId, CancellationToken cancellationToken = default)
        {
            var action = await FindAsync(userId, actionId, cancellationToken);
            if (action.Status == AssistantActionStatus.Proposed)
            {
                action.Status = AssistantActionStatus.Cancelled;
                await context.SaveChangesAsync(cancellationToken);
            }
            else if (action.Status != AssistantActionStatus.Cancelled)
            {
                throw new ConflictException(action.Status == AssistantActionStatus.Confirmed
                    ? "This change was already made. Use Undo to put it back."
                    : "This change can’t be cancelled now.");
            }
            return Result("cancelled", "Cancelled. Nothing was changed.", action);
        }

        public async Task<AssistantActionResult> UndoAsync(string userId, Guid actionId, CancellationToken cancellationToken = default)
        {
            var action = await FindAsync(userId, actionId, cancellationToken);
            if (action.Status == AssistantActionStatus.Undone) return Result("undone", action.ResultText ?? "Undone.", action);
            if (action.Status != AssistantActionStatus.Confirmed) throw new ConflictException("Only a change that was made can be undone.");
            if (!action.CanUndo) throw new ConflictException("This change can’t be undone.");
            var now = clock.GetUtcNow();
            if (action.UndoUntil is not DateTimeOffset until || now > until) throw new ConflictException("The time to undo this change has passed. You can change it back on the page.");

            var change = ActionJson.Read<ActionChange>(action.ChangeJson);
            var before = ActionJson.Read<ActionBefore>(action.BeforeJson ?? "{}");
            var applied = action.AppliedJson != null ? ActionJson.Read<ActionApplied>(action.AppliedJson) : new ActionApplied(null, null);
            string text;
            await using (var tx = context.Database.IsRelational() ? await context.Database.BeginTransactionAsync(cancellationToken) : null)
            {
                try
                {
                    text = await RevertAsync(userId, action.Kind, change, before, applied, cancellationToken);
                    action.Status = AssistantActionStatus.Undone;
                    action.UndoneAt = now;
                    action.UndoUntil = null;
                    action.ResultText = text.Length > 300 ? text[..300] : text;
                    await context.SaveChangesAsync(cancellationToken);
                    if (tx != null) await tx.CommitAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (tx != null) await tx.RollbackAsync(CancellationToken.None);
                    context.ChangeTracker.Clear();
                    logger.LogError(ex, "Assistant action {Kind} failed to undo", action.Kind);
                    throw new ConflictException("That couldn’t be undone, so nothing was changed. Try again, or change it back on the page.");
                }
            }
            logger.LogInformation("Assistant action {Kind} undone", action.Kind);
            return Result("undone", text, action);
        }

        public async Task<int> CleanupAsync(int days = 7, CancellationToken cancellationToken = default)
        {
            var cutoff = clock.GetUtcNow().AddDays(-days);
            var old = await context.AssistantActions.Where(a => a.CreatedAt < cutoff).ToListAsync(cancellationToken);
            context.AssistantActions.RemoveRange(old);
            await context.SaveChangesAsync(cancellationToken);
            return old.Count;
        }

        // ---- applying ----

        private async Task<(ActionApplied, string)> ApplyAsync(string userId, AssistantActionKind kind, ActionChange c, bool withRule, CancellationToken ct)
        {
            switch (kind)
            {
                case AssistantActionKind.Recategorize:
                case AssistantActionKind.SetTreatment:
                {
                    var count = await transactions.BulkUpdateAsync(userId, c.Ids, c.CategoryId, c.Treatment, ct);
                    if (count != c.Ids.Count) throw new InvalidOperationException($"Expected {c.Ids.Count} transactions, found {count}");
                    int? ruleId = null;
                    if (withRule) ruleId = (await transactions.SaveRuleAsync(userId, c.Rule!, applyToPast: false, ct)).Rule.CategoryRuleId;
                    var what = kind == AssistantActionKind.Recategorize
                        ? $"{count} moved to {c.CategoryName}"
                        : c.Treatment == Treatment.Hidden ? $"{count} hidden" : $"{count} now count as {AssistantTools.TreatmentName(c.Treatment!.Value).ToLowerInvariant()}";
                    return (new ActionApplied(ruleId, null), withRule ? what + ". Future ones will too (a rule was added)." : what);
                }
                case AssistantActionKind.Rule:
                {
                    var saved = await transactions.SaveRuleAsync(userId, c.Rule!, c.ApplyToPast, ct);
                    return (new ActionApplied(saved.Rule.CategoryRuleId, null),
                        c.ApplyToPast && saved.Matches > 0 ? $"Rule added. {saved.Matches} past transactions were re-placed." : "Rule added.");
                }
                case AssistantActionKind.Target:
                    await SetTargetAsync(userId, c.Year!.Value, c.Month!.Value, c.CategoryId!.Value, c.Amount!.Value, c.Rollover, ct);
                    return (new ActionApplied(null, null), $"{c.CategoryName} set to {AssistantTools.Money(c.Amount!.Value)}.");
                default:
                    switch (c.Op)
                    {
                        case "add":
                            var added = await categories.AddCategoryAsync(userId, new FinancialCategoryModel { Name = c.Name!, Group = c.Group ?? CategoryGroup.Flexible });
                            return (new ActionApplied(null, added.FinancialCategoryId), $"{added.Name} added.");
                        case "rename":
                            await RenameAsync(userId, c.CategoryId!.Value, c.Name!);
                            return (new ActionApplied(null, null), $"Renamed to {c.Name}.");
                        case "merge":
                            await categories.ArchiveCategoryAsync(userId, c.CategoryId!.Value, c.IntoCategoryId);
                            return (new ActionApplied(null, null), $"{c.CategoryName} merged into {c.Name}.");
                        default:
                            throw new InvalidOperationException($"Unknown category change {c.Op}");
                    }
            }
        }

        private async Task<string> RevertAsync(string userId, AssistantActionKind kind, ActionChange c, ActionBefore before, ActionApplied applied, CancellationToken ct)
        {
            switch (kind)
            {
                case AssistantActionKind.Recategorize:
                case AssistantActionKind.SetTreatment:
                {
                    var ids = before.Rows.Select(r => r.Id).ToList();
                    var rows = await context.BankTransactions
                        .Where(t => ids.Contains(t.BankTransactionId) && t.FinancialAccount.InstitutionAccessItem.AccessUsers.Any(u => u.User.ExternalUserId == userId))
                        .ToDictionaryAsync(t => t.BankTransactionId, ct);
                    int restored = 0, skipped = 0;
                    foreach (var b in before.Rows)
                    {
                        if (!rows.TryGetValue(b.Id, out var t)) { skipped++; continue; }
                        // Only rows still as the change left them; anything the person (or a sync) changed since stays
                        if (kind == AssistantActionKind.Recategorize)
                        {
                            if (t.CategoryId != c.CategoryId || t.CategorySource != ClassificationSource.User) { skipped++; continue; }
                            t.CategoryId = b.CategoryId;
                            t.CategorySource = b.CategorySource;
                        }
                        else
                        {
                            if (t.Treatment != c.Treatment || t.TreatmentSource != ClassificationSource.User) { skipped++; continue; }
                            t.Treatment = b.Treatment;
                            t.TreatmentSource = b.TreatmentSource;
                        }
                        restored++;
                    }
                    await context.SaveChangesAsync(ct);
                    var rule = applied.RuleId is int ruleId && await DeleteRuleAsync(userId, ruleId, ct) ? " The rule was removed." : "";
                    return skipped == 0
                        ? $"Undone: {restored} back as they were.{rule}"
                        : $"Undone for {restored}; {skipped} you changed since {(skipped == 1 ? "was" : "were")} left as {(skipped == 1 ? "it is" : "they are")}.{rule}";
                }
                case AssistantActionKind.Rule:
                    return applied.RuleId is int id && await DeleteRuleAsync(userId, id, ct)
                        ? "Undone: the rule was removed and what it placed went back."
                        : "That rule was already deleted.";
                case AssistantActionKind.Target:
                {
                    var now = await TargetAsync(userId, c.Year!.Value, c.Month!.Value, c.CategoryId!.Value, ct);
                    if (now != c.Amount) return "The target was changed since, so it was left as it is.";
                    await SetTargetAsync(userId, c.Year!.Value, c.Month!.Value, c.CategoryId!.Value, before.TargetAmount ?? 0, before.TargetRollover, ct);
                    return $"Undone: {c.CategoryName} is back to {AssistantTools.Money(before.TargetAmount ?? 0)}.";
                }
                default:
                    switch (c.Op)
                    {
                        case "add":
                            if (applied.CategoryId is int added) await categories.ArchiveCategoryAsync(userId, added, null);
                            return $"Undone: {c.Name} was removed.";
                        case "rename":
                            var current = await categories.GetCategoryAsync(userId, c.CategoryId!.Value);
                            if (current == null || current.Name != c.Name) return "It was renamed again since, so it was left as it is.";
                            await RenameAsync(userId, c.CategoryId!.Value, before.CategoryName!);
                            return $"Undone: it’s {before.CategoryName} again.";
                        default:
                            throw new ConflictException("This change can’t be undone.");
                    }
            }
        }

        private async Task<bool> DeleteRuleAsync(string userId, int ruleId, CancellationToken ct)
        {
            if (!await context.CategoryRules.AnyAsync(r => r.CategoryRuleId == ruleId && r.UserId == userId, ct)) return false;
            await transactions.DeleteRuleAsync(userId, ruleId, ct);
            return true;
        }

        private async Task RenameAsync(string userId, int categoryId, string name)
        {
            var category = await categories.GetCategoryAsync(userId, categoryId) ?? throw new NotFoundException("Category", categoryId);
            category.Name = name;
            await categories.UpdateCategoryAsync(userId, category);
        }

        private async Task<decimal> TargetAsync(string userId, int year, int month, int categoryId, CancellationToken ct) =>
            await context.BudgetTargets
                .Where(t => t.CategoryId == categoryId && t.BudgetMonth.UserId == userId && t.BudgetMonth.Year == year && t.BudgetMonth.Month == month)
                .Select(t => (decimal?)t.Amount).FirstOrDefaultAsync(ct) ?? 0;

        /// <summary>The budget page saves a month's whole list of targets, so change one and keep the rest</summary>
        private async Task SetTargetAsync(string userId, int year, int month, int categoryId, decimal amount, bool rollover, CancellationToken ct)
        {
            var existing = await context.BudgetMonths.Where(b => b.UserId == userId && b.Year == year && b.Month == month)
                .SelectMany(b => b.Targets).Select(t => new BudgetTargetSave(t.CategoryId, t.Amount, t.Rollover)).ToListAsync(ct);
            var list = existing.Where(t => t.CategoryId != categoryId).ToList();
            list.Add(new BudgetTargetSave(categoryId, amount, rollover));
            await budgets.SaveTargetsAsync(userId, year, month, list, ct);
        }

        // ---- shared ----

        private async Task<AssistantAction> FindAsync(string userId, Guid actionId, CancellationToken ct) =>
            await context.AssistantActions.FirstOrDefaultAsync(a => a.AssistantActionId == actionId && a.UserId == userId, ct)
                ?? throw new NotFoundException("Change", actionId);

        private async Task MarkFailedAsync(Guid actionId)
        {
            context.ChangeTracker.Clear();
            var action = await context.AssistantActions.FirstOrDefaultAsync(a => a.AssistantActionId == actionId);
            if (action == null) return;
            action.Status = AssistantActionStatus.Failed;
            await context.SaveChangesAsync();
        }

        private AssistantActionResult Result(string outcome, string message, AssistantAction action) =>
            new(outcome, message, Card(action, clock.GetUtcNow(), canConfirm: true));

        /// <summary>The card as saved, with its current status, result and undo window</summary>
        public static AssistantActionModel Card(AssistantAction action, DateTimeOffset now, bool canConfirm)
        {
            var card = ActionJson.Read<AssistantActionModel>(action.CardJson);
            var undoable = action.Status == AssistantActionStatus.Confirmed && action.CanUndo && action.UndoUntil > now;
            return card with
            {
                Id = action.AssistantActionId,
                Status = action.Status.ToString().ToLowerInvariant(),
                CanConfirm = canConfirm,
                CanUndo = undoable,
                UndoUntil = undoable ? action.UndoUntil : null,
                ExpiresAt = action.ExpiresAt,
                Result = action.Status is AssistantActionStatus.Confirmed or AssistantActionStatus.Undone ? action.ResultText : null,
            };
        }
    }
}
