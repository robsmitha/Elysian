using Elysian.Application.Exceptions;
using Elysian.Application.Features.Assistant.Models;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Elysian.Infrastructure.Assistant
{
    /// <summary>
    /// The assistant's limits (docs/assistant-plan.md §7): per person per minute and per day, per tenant per day, and a
    /// monthly token cap. Taking a question is atomic per person, so two tabs can't both take the last one.
    /// </summary>
    public class AssistantLimiter(ElysianContext context, IOptions<AssistantSettings> options, TimeProvider clock)
    {
        private AssistantSettings Settings => options.Value;

        /// <summary>Counts toward the daily limits: answered, or still running. Fallbacks and refusals give the question back.</summary>
        private static readonly AssistantOutcome[] Counted = [AssistantOutcome.Started, AssistantOutcome.Answered];

        public async Task<long> TakeAsync(string userId, DateOnly localDate, CancellationToken cancellationToken)
        {
            var now = clock.GetUtcNow();
            var relational = context.Database.IsRelational();
            await using var tx = relational ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
            if (context.Database.IsSqlServer())
            {
                var resource = $"assistant:{userId}";
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"EXEC sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 5000",
                    cancellationToken);
            }

            var mine = context.AssistantUsages.Where(u => u.UserId == userId);

            var minuteAgo = now.AddMinutes(-1);
            var lastMinute = await mine.Where(u => u.StartedAt >= minuteAgo).OrderBy(u => u.StartedAt).Select(u => u.StartedAt).ToListAsync(cancellationToken);
            if (lastMinute.Count >= Settings.PerMinute)
            {
                throw new TooManyRequestsException("That's a lot of questions at once. Try again in a minute.", lastMinute[0].AddMinutes(1));
            }

            var today = await mine.CountAsync(u => u.LocalDate == localDate && Counted.Contains(u.Outcome), cancellationToken);
            if (today >= Settings.PerDay)
            {
                throw new TooManyRequestsException($"You've used today's {Settings.PerDay} questions. You can ask again after midnight.", NextUtcDay(now));
            }

            var everyone = await context.AssistantUsages.CountAsync(u => u.LocalDate == localDate && Counted.Contains(u.Outcome), cancellationToken);
            if (everyone >= Settings.TenantPerDay)
            {
                throw new TooManyRequestsException("The assistant has answered all it can for today. Try again tomorrow.", NextUtcDay(now));
            }

            var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
            var tokens = await mine.Where(u => u.StartedAt >= monthStart).SumAsync(u => (long)u.InputTokens + u.OutputTokens, cancellationToken);
            if (tokens >= Settings.MonthlyTokens)
            {
                throw new TooManyRequestsException("You've reached this month's assistant limit. It resets on the 1st.", monthStart.AddMonths(1));
            }

            var row = new AssistantUsage { UserId = userId, LocalDate = localDate, StartedAt = now, Outcome = AssistantOutcome.Started };
            context.AssistantUsages.Add(row);
            await context.SaveChangesAsync(cancellationToken);
            if (tx != null) await tx.CommitAsync(cancellationToken);
            return row.AssistantUsageId;
        }

        public async Task FinishAsync(long usageId, AssistantOutcome outcome, ModelUsage usage, string? model, int toolCalls, string promptVersion,
            CancellationToken cancellationToken)
        {
            var row = await context.AssistantUsages.FirstOrDefaultAsync(u => u.AssistantUsageId == usageId, cancellationToken);
            if (row == null) return;
            var now = clock.GetUtcNow();
            row.Outcome = outcome;
            row.FinishedAt = now;
            row.Model = model;
            row.PromptVersion = promptVersion;
            row.InputTokens = usage.InputTokens + usage.CacheWriteTokens;
            row.OutputTokens = usage.OutputTokens;
            row.CacheReadTokens = usage.CacheReadTokens;
            row.ToolCalls = toolCalls;
            row.DurationMs = (int)Math.Min(int.MaxValue, (now - row.StartedAt).TotalMilliseconds);
            await context.SaveChangesAsync(cancellationToken);
        }

        public async Task<AssistantUsageModel> UsageAsync(string userId, DateOnly localDate, CancellationToken cancellationToken)
        {
            var used = await context.AssistantUsages.CountAsync(u => u.UserId == userId && u.LocalDate == localDate && Counted.Contains(u.Outcome), cancellationToken);
            return new AssistantUsageModel(Settings.Enabled, Settings.PerDay, used, Math.Max(0, Settings.PerDay - used), localDate.AddDays(1));
        }

        private static DateTimeOffset NextUtcDay(DateTimeOffset now) => new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero).AddDays(1);
    }
}
