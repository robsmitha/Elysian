using System.Text.Json;
using Elysian.Application.Exceptions;
using Elysian.Application.Features.Assistant;
using Elysian.Application.Features.Assistant.Models;
using Elysian.Application.Interfaces;
using Elysian.Domain.Data;
using Elysian.Infrastructure.Context;
using Elysian.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elysian.Infrastructure.Assistant
{
    /// <summary>
    /// One question, start to finish (docs/assistant-plan.md §2): the limit, the context, the tool loop, the answer checks
    /// with one retry, and the usage record. Logs say what ran and how it went, never the question or the answer.
    /// </summary>
    public class AssistantService(
        ElysianContext context,
        IOptionsMonitor<AssistantSettings> options,
        AssistantLimiter limiter,
        AssistantTools tools,
        IAssistantModel model,
        ITransactionClassifier classifier,
        TimeProvider clock,
        ILogger<AssistantService> logger) : IAssistantService
    {
        public const string Fallback = "I couldn’t answer that reliably. Try a narrower question, like one month, one category or one merchant.";
        public const string TooSlow = "That took too long to work out. Try a narrower question, like one month or one category.";
        public const string Busy = "The assistant is busy right now. Try again in a minute.";
        public const string Refused = "I can’t help with that one. I can answer questions about your spending, budget, bills, goals and debt.";

        /// <summary>Read on every request, so an instance never keeps settings it loaded before the app settings applied</summary>
        private AssistantSettings Settings => options.CurrentValue;

        public async Task<AssistantUsageModel> GetUsageAsync(string userId, DateOnly? today, CancellationToken cancellationToken = default) =>
            await limiter.UsageAsync(userId, Today(today), cancellationToken);

        public async Task<AssistantReply> AskAsync(string userId, AskAssistantRequest request, CancellationToken cancellationToken = default)
        {
            if (!Settings.Enabled)
            {
                logger.LogWarning("Assistant refused: switched off (Assistant__Enabled) on instance {Instance}",
                    Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID") ?? Environment.MachineName);
                throw new NotFoundException("The assistant is switched off.");
            }

            var today = Today(request.Today);
            var usageId = await limiter.TakeAsync(userId, today, cancellationToken);

            var run = new Run();
            var outcome = AssistantOutcome.Error;
            AssistantReply? reply = null;
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(TimeSpan.FromSeconds(Settings.TotalSeconds));
            try
            {
                (outcome, reply) = await AnswerAsync(userId, today, request, run, budget.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                outcome = AssistantOutcome.Fallback;
                reply = Short("fallback", TooSlow, run);
            }
            catch (AssistantModelUnavailableException ex)
            {
                logger.LogWarning(ex, "Assistant model unavailable");
                outcome = AssistantOutcome.Error;
                reply = Short("unavailable", Busy, run);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Assistant failed");
                outcome = AssistantOutcome.Error;
                reply = Short("fallback", Fallback, run);
                context.ChangeTracker.Clear();
            }
            finally
            {
                await limiter.FinishAsync(usageId, outcome, run.Usage, run.Model, run.ToolCalls, AssistantPrompt.Version, CancellationToken.None);
                logger.LogInformation(
                    "Assistant {Outcome} model={Model} prompt={Prompt} rounds={Rounds} toolCalls={ToolCalls} tools={Tools} retried={Retried} in={In} out={Out} cacheRead={CacheRead} cacheWrite={CacheWrite}",
                    outcome, run.Model, AssistantPrompt.Version, run.Rounds, run.ToolCalls, string.Join(",", run.ToolNames), run.Retried,
                    run.Usage.InputTokens, run.Usage.OutputTokens, run.Usage.CacheReadTokens, run.Usage.CacheWriteTokens);
            }

            var usage = await limiter.UsageAsync(userId, today, CancellationToken.None);
            return reply! with { Usage = usage };
        }

        /// <summary>What one question accumulates across rounds</summary>
        private sealed class Run
        {
            public ModelUsage Usage = ModelUsage.None;
            public string? Model;
            public int Rounds;
            public int ToolCalls;
            public bool Retried;
            public readonly List<string> ToolNames = [];
            public readonly List<string> Steps = [];
            public readonly List<decimal> Numbers = [];
            public readonly HashSet<string> Links = [];
            public readonly Dictionary<string, AssistantChart> Charts = [];
            /// <summary>Proposals this question made, by action id</summary>
            public readonly Dictionary<string, AssistantActionModel> Actions = [];
        }

        /// <summary>Said instead when the answer keeps claiming a proposed change was already made</summary>
        public const string ProposedOnly = "Here’s what I’d change. Nothing changes until you confirm it below.";

        private async Task<(AssistantOutcome, AssistantReply)> AnswerAsync(string userId, DateOnly today, AskAssistantRequest request, Run run, CancellationToken ct)
        {
            await classifier.ClassifyAsync(userId, cancellationToken: ct);

            var accounts = await context.FinancialAccounts
                .Where(a => a.InstitutionAccessItem.AccessUsers.Any(u => u.User.ExternalUserId == userId) && a.BudgetRole != Domain.Constants.AccountBudgetRole.Ignore)
                .OrderBy(a => a.Name)
                .Select(a => new AssistantPrompt.ContextAccount(a.Name, a.Mask, a.Type, a.BudgetRole.ToString()))
                .ToListAsync(ct);
            var categories = await context.FinancialCategories.Where(c => c.UserId == userId && !c.IsArchived)
                .OrderBy(c => c.Group).ThenBy(c => c.SortOrder).Take(80)
                .Select(c => new AssistantPrompt.ContextCategory(c.FinancialCategoryId, c.Name, c.Group.ToString()))
                .ToListAsync(ct);

            var messages = History(request.History);
            messages.Add(new UserText(AssistantPrompt.Context(today, request.Page, request.PageMonth, accounts, categories, request.Question)));

            for (var round = 0; round <= Settings.MaxRounds; round++)
            {
                run.Rounds++;
                var response = await model.CreateAsync(new ModelRequest(AssistantPrompt.System, AssistantTools.Definitions, messages, AssistantPrompt.AnswerSchema), ct);
                run.Usage = run.Usage.Add(response.Usage);
                run.Model = response.Model;

                if (response.Stop == ModelStop.Refusal) return (AssistantOutcome.Refused, Short("out_of_scope", Refused, run));

                if (response.ToolCalls.Count > 0)
                {
                    if (round == Settings.MaxRounds) break;
                    messages.Add(new AssistantResponse(response));
                    var results = new List<ToolResult>();
                    // One at a time: the tools share this request's DbContext
                    foreach (var call in response.ToolCalls)
                    {
                        var result = await tools.RunAsync(userId, today, call, ct);
                        run.ToolCalls++;
                        run.ToolNames.Add(call.Name);
                        if (!run.Steps.Contains(result.Step)) run.Steps.Add(result.Step);
                        if (!result.IsError) Remember(run, call.Id, result);
                        results.Add(new ToolResult(call.Id, result.Json, result.IsError));
                    }
                    messages.Add(new ToolResults(results));
                    continue;
                }

                var draft = AnswerChecks.Parse(response.Text);
                if (draft == null)
                {
                    if (run.Retried) break;
                    run.Retried = true;
                    messages.Add(new AssistantResponse(response));
                    messages.Add(new UserText("That reply wasn't the required JSON object. Reply again with only the JSON object."));
                    continue;
                }

                var check = AnswerChecks.Check(draft, run.Numbers, request.Question, run.Links, run.Charts.Keys.ToHashSet(), run.Actions.Keys.ToHashSet());
                if (!check.Passed)
                {
                    if (run.Retried)
                    {
                        // The numbers were fine and a proposal is waiting: show the card with plain words rather than nothing
                        if (check.UnsupportedNumbers.Count == 0) return (AssistantOutcome.Answered, Build(check.Draft with { Answer = ProposedOnly, Highlights = [] }, run, request.CanChange));
                        break;
                    }
                    run.Retried = true;
                    messages.Add(new AssistantResponse(response));
                    var problems = new List<string>();
                    if (check.UnsupportedNumbers.Count > 0)
                    {
                        problems.Add($"these numbers don't appear in any tool result: {string.Join(", ", check.UnsupportedNumbers)}. Call a tool that returns them, or leave them out");
                    }
                    if (check.ClaimsDone)
                    {
                        problems.Add("it says the change was made, but nothing changes until the person taps Confirm on the card. Describe it as a proposal, e.g. \"Here's what I'd change. Confirm below to apply it.\"");
                    }
                    messages.Add(new UserText($"Your answer wasn't shown: {string.Join("; and ", problems)}. Then reply again with only the JSON object."));
                    continue;
                }

                return (AssistantOutcome.Answered, Build(check.Draft, run, request.CanChange));
            }
            return (AssistantOutcome.Fallback, Short("fallback", Fallback, run));
        }

        /// <summary>What a tool result lets the answer use: its numbers, its links, and its series for a chart</summary>
        private static void Remember(Run run, string callId, ToolOutcome result)
        {
            using var doc = JsonDocument.Parse(result.Json);
            AnswerChecks.CollectNumbers(doc.RootElement, run.Numbers);
            CollectLinks(doc.RootElement, run.Links);
            if (result.Chart != null) run.Charts[callId] = result.Chart;
            if (result.Action != null) run.Actions[result.Action.Id.ToString()] = result.Action;
        }

        private static void CollectLinks(JsonElement e, HashSet<string> into)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Name == "link" && p.Value.ValueKind == JsonValueKind.String) into.Add(p.Value.GetString()!);
                    else CollectLinks(p.Value, into);
                }
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in e.EnumerateArray()) CollectLinks(item, into);
            }
        }

        private static AssistantReply Build(AnswerDraft draft, Run run, bool canChange)
        {
            var chart = draft.ChartKind != "none" && run.Charts.TryGetValue(draft.ChartToolCallId, out var c) ? c with { Kind = draft.ChartKind } : null;
            // The card the answer names; if it forgot to name one and there's exactly one, that one (one proposal per answer)
            var action = run.Actions.TryGetValue(draft.ActionId, out var a) ? a
                : draft.ActionId.Length == 0 && run.Actions.Count == 1 ? run.Actions.Values.First()
                : null;
            return new AssistantReply(draft.Status, draft.Answer, draft.Highlights, chart, draft.Links, draft.FollowUps, [.. run.Steps], Empty,
                action == null ? null : action with { CanConfirm = canChange });
        }

        private static AssistantReply Short(string status, string text, Run run) => new(status, text, [], null, [], [], [.. run.Steps], Empty);

        private static readonly AssistantUsageModel Empty = new(true, 0, 0, 0, DateOnly.MinValue);

        /// <summary>
        /// Earlier exchanges as plain text: user, assistant, user, assistant… Anything out of order is dropped, so the
        /// conversation always alternates and ends with an answer before the new question.
        /// </summary>
        private static List<ModelMessage> History(List<AssistantTurn>? history)
        {
            var list = new List<ModelMessage>();
            var expectUser = true;
            foreach (var turn in history ?? [])
            {
                var text = (turn.Text ?? "").Trim();
                if (text.Length == 0) continue;
                if (expectUser && turn.Role == "user") { list.Add(new UserText(text)); expectUser = false; }
                else if (!expectUser && turn.Role == "assistant") { list.Add(new AssistantText(text)); expectUser = true; }
            }
            if (!expectUser) list.RemoveAt(list.Count - 1);
            return list;
        }

        /// <summary>The browser's date (the person's time zone) when it's within a day of the server's; otherwise the server's</summary>
        private DateOnly Today(DateOnly? fromBrowser)
        {
            var server = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            return fromBrowser is DateOnly d && Math.Abs(d.DayNumber - server.DayNumber) <= 1 ? d : server;
        }
    }
}
