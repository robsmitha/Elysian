using System.Text.Json;

namespace Elysian.Infrastructure.Assistant
{
    /// <summary>A tool Claude can call: name, description written for the model, and a strict JSON schema</summary>
    public record ToolDefinition(string Name, string Description, JsonElement InputSchema);

    public record ModelToolCall(string Id, string Name, JsonElement Input);

    public enum ModelStop { EndTurn, ToolUse, Refusal, MaxTokens, Other }

    public record ModelUsage(int InputTokens, int OutputTokens, int CacheReadTokens, int CacheWriteTokens)
    {
        public static readonly ModelUsage None = new(0, 0, 0, 0);
        public ModelUsage Add(ModelUsage other) => new(InputTokens + other.InputTokens, OutputTokens + other.OutputTokens,
            CacheReadTokens + other.CacheReadTokens, CacheWriteTokens + other.CacheWriteTokens);
    }

    /// <summary>
    /// One model response. <see cref="Raw"/> is the provider's own content, sent back unchanged in the next round
    /// (thinking blocks and their signatures included).
    /// </summary>
    public record ModelResponse(ModelStop Stop, string Text, List<ModelToolCall> ToolCalls, ModelUsage Usage, string Model, object? Raw);

    /// <summary>A message in the conversation sent to the model</summary>
    public abstract record ModelMessage;
    public record UserText(string Text) : ModelMessage;
    public record AssistantText(string Text) : ModelMessage;
    /// <summary>A response from an earlier round of this question, replayed as it came</summary>
    public record AssistantResponse(ModelResponse Response) : ModelMessage;
    public record ToolResult(string ToolCallId, string Content, bool IsError);
    /// <summary>Every result for one round, in one message</summary>
    public record ToolResults(List<ToolResult> Results) : ModelMessage;

    public record ModelRequest(string System, IReadOnlyList<ToolDefinition> Tools, IReadOnlyList<ModelMessage> Messages, JsonElement AnswerSchema);

    /// <summary>Claude, or the offline fake used locally and in tests</summary>
    public interface IAssistantModel
    {
        Task<ModelResponse> CreateAsync(ModelRequest request, CancellationToken cancellationToken);
    }

    /// <summary>The model couldn't be reached or was busy (rate limited, overloaded, timed out)</summary>
    public class AssistantModelUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
}
