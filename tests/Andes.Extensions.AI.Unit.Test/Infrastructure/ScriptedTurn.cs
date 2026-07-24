using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Unit.Test.Infrastructure;

/// <summary>
/// One scripted provider turn: the exact <see cref="ChatResponseUpdate"/> sequence the fake
/// provider replays for a single inner call.
/// </summary>
internal sealed class ScriptedTurn
{
    public required IReadOnlyList<ChatResponseUpdate> Updates { get; init; }

    /// <summary>
    /// Optional gate awaited before each update is yielded; lets tests interleave work
    /// (for example, cancellation) with the stream.
    /// </summary>
    public Func<Task>? BeforeEachUpdate { get; init; }

    public static ScriptedTurn Text(
        string text,
        UsageDetails? usage = null,
        string? responseId = null,
        string? modelId = null)
    {
        var updates = new List<ChatResponseUpdate>
        {
            new(ChatRole.Assistant, text) { ResponseId = responseId, ModelId = modelId },
        };

        if (usage is not null)
        {
            updates.Add(new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(usage)])
            {
                ResponseId = responseId,
                ModelId = modelId,
            });
        }

        return new ScriptedTurn { Updates = updates };
    }

    public static ScriptedTurn FunctionCall(
        string callId,
        string name,
        IDictionary<string, object?>? arguments = null,
        UsageDetails? usage = null,
        string? responseId = null,
        string? modelId = null)
    {
        var updates = new List<ChatResponseUpdate>
        {
            new(ChatRole.Assistant, [new FunctionCallContent(callId, name, arguments)])
            {
                ResponseId = responseId,
                ModelId = modelId,
            },
        };

        if (usage is not null)
        {
            updates.Add(new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(usage)])
            {
                ResponseId = responseId,
                ModelId = modelId,
            });
        }

        return new ScriptedTurn { Updates = updates };
    }

    public static ScriptedTurn FunctionCalls(
        IReadOnlyList<(string CallId, string Name)> calls,
        UsageDetails? usage = null,
        string? responseId = null,
        string? modelId = null)
    {
        var contents = new List<AIContent>();
        foreach ((string callId, string name) in calls)
        {
            contents.Add(new FunctionCallContent(callId, name));
        }

        var updates = new List<ChatResponseUpdate>
        {
            new(ChatRole.Assistant, contents) { ResponseId = responseId, ModelId = modelId },
        };

        if (usage is not null)
        {
            updates.Add(new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(usage)])
            {
                ResponseId = responseId,
                ModelId = modelId,
            });
        }

        return new ScriptedTurn { Updates = updates };
    }
}
