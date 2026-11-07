using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Elysian.Infrastructure.Settings;
using Microsoft.Extensions.Options;

namespace Elysian.Infrastructure.Assistant
{
    /// <summary>
    /// Claude through the official SDK (docs/assistant-plan.md §3): the beta messages endpoint for server-side refusal
    /// fallback, effort from settings, adaptive thinking (the default; it can't be turned off on Opus 5.5), the answer
    /// constrained by a JSON schema, and the prompt and tools cached. Responses are replayed unchanged within a question.
    /// </summary>
    public class AnthropicAssistantModel : IAssistantModel
    {
        public const string FallbackBeta = "server-side-fallback-2026-07-01";

        private readonly AnthropicClient client;
        private readonly IOptionsMonitor<AssistantSettings> options;

        public AnthropicAssistantModel(IOptionsMonitor<AssistantSettings> assistant, IOptions<AnthropicSettings> anthropic)
        {
            options = assistant;
            var settings = assistant.CurrentValue;
            var key = anthropic.Value.ApiKey;
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Anthropic__ApiKey isn't set; set it or use Assistant__Provider=fake.");
            client = new AnthropicClient
            {
                ApiKey = key,
                MaxRetries = 1,
                Timeout = TimeSpan.FromSeconds(settings.CallSeconds),
            };
        }

        public async Task<ModelResponse> CreateAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            var settings = options.CurrentValue;
            var parameters = Build(request, settings);

            BetaMessage message;
            try
            {
                message = await client.Beta.Messages.Create(parameters, cancellationToken);
            }
            catch (AnthropicRateLimitException ex)
            {
                throw new AssistantModelUnavailableException("Claude is busy (rate limited).", ex);
            }
            catch (Anthropic5xxException ex)
            {
                throw new AssistantModelUnavailableException("Claude had a server error.", ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new AssistantModelUnavailableException("Claude took too long.", ex);
            }

            var text = string.Concat(message.Content.Select(b => b.TryPickText(out var t) ? t.Text : ""));
            var calls = new List<ModelToolCall>();
            foreach (var block in message.Content)
            {
                if (block.TryPickToolUse(out var use))
                {
                    calls.Add(new ModelToolCall(use.ID, use.Name, JsonSerializer.SerializeToElement(use.Input)));
                }
            }
            // "end_turn" on the wire; compared loosely so the SDK's own spelling of the value doesn't matter
            var stop = (message.StopReason?.ToString() ?? "").Replace("_", "").ToLowerInvariant() switch
            {
                "endturn" => ModelStop.EndTurn,
                "tooluse" => ModelStop.ToolUse,
                "refusal" => ModelStop.Refusal,
                "maxtokens" => ModelStop.MaxTokens,
                _ => calls.Count > 0 ? ModelStop.ToolUse : ModelStop.Other,
            };
            var usage = new ModelUsage(
                (int)message.Usage.InputTokens,
                (int)message.Usage.OutputTokens,
                (int)(message.Usage.CacheReadInputTokens ?? 0),
                (int)(message.Usage.CacheCreationInputTokens ?? 0));
            return new ModelResponse(stop, text, calls, usage, message.Model.ToString() ?? settings.Model, message);
        }

        /// <summary>The request, kept separate so tests can check what's sent without calling the API</summary>
        public static MessageCreateParams Build(ModelRequest request, AssistantSettings settings) => new()
        {
            Model = settings.Model,
            MaxTokens = 16000,
            Betas = [FallbackBeta],
            Fallbacks = new Default(),
            System = new List<BetaTextBlockParam>
            {
                new() { Text = request.System, CacheControl = new BetaCacheControlEphemeral() },
            },
            Tools = request.Tools.Select(ToTool).ToList(),
            Messages = request.Messages.Select(ToParam).ToList(),
            OutputConfig = new BetaOutputConfig
            {
                Effort = EffortFrom(settings.Effort),
                Format = new BetaJsonOutputFormat { Schema = ToDictionary(request.AnswerSchema) },
            },
        };

        private static BetaToolUnion ToTool(ToolDefinition tool)
        {
            var schema = tool.InputSchema;
            var properties = schema.TryGetProperty("properties", out var p)
                ? p.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone())
                : new Dictionary<string, JsonElement>();
            var required = schema.TryGetProperty("required", out var r)
                ? r.EnumerateArray().Select(x => x.GetString()!).ToList()
                : [];
            return new BetaTool
            {
                Name = tool.Name,
                Description = tool.Description,
                InputSchema = new InputSchema { Properties = properties, Required = required },
            };
        }

        private static BetaMessageParam ToParam(ModelMessage message) => message switch
        {
            UserText u => new BetaUserMessageParam { Content = u.Text },
            AssistantText a => new BetaAssistantMessageParam { Content = a.Text },
            AssistantResponse r => new BetaAssistantMessageParam { Content = Replay(r.Response) },
            ToolResults t => new BetaUserMessageParam
            {
                Content = t.Results.Select(x => (BetaContentBlockParam)new BetaToolResultBlockParam
                {
                    ToolUseID = x.ToolCallId,
                    Content = x.Content,
                    IsError = x.IsError,
                }).ToList(),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(message)),
        };

        /// <summary>Every block of an earlier response, rebuilt as its param type and in order (thinking signatures kept)</summary>
        private static List<BetaContentBlockParam> Replay(ModelResponse response)
        {
            if (response.Raw is not BetaMessage message)
            {
                return [new BetaTextBlockParam { Text = response.Text }];
            }
            var blocks = new List<BetaContentBlockParam>();
            foreach (var b in message.Content)
            {
                if (b.TryPickText(out var text)) blocks.Add(new BetaTextBlockParam { Text = text.Text });
                else if (b.TryPickThinking(out var thinking)) blocks.Add(new BetaThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
                else if (b.TryPickRedactedThinking(out var redacted)) blocks.Add(new BetaRedactedThinkingBlockParam { Data = redacted.Data });
                else if (b.TryPickToolUse(out var use)) blocks.Add(new BetaToolUseBlockParam { ID = use.ID, Name = use.Name, Input = use.Input });
                else if (b.TryPickFallback(out var fallback)) blocks.Add(new BetaFallbackBlockParam
                {
                    From = new BetaFallbackInfoParam { Model = fallback.From.Model },
                    To = new BetaFallbackInfoParam { Model = fallback.To.Model },
                    Trigger = JsonSerializer.SerializeToElement(fallback.Trigger),
                });
            }
            return blocks;
        }

        private static Effort EffortFrom(string value) => value switch
        {
            "medium" => Effort.Medium,
            "high" => Effort.High,
            "xhigh" => Effort.Xhigh,
            "max" => Effort.Max,
            _ => Effort.Low,
        };

        private static Dictionary<string, JsonElement> ToDictionary(JsonElement schema) =>
            schema.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }
}
