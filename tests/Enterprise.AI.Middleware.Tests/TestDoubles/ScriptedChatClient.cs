using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tests.TestDoubles;

/// <summary>
/// An <see cref="IChatClient"/> that replays pre-programmed turns, one per call, so the real
/// <see cref="FunctionInvokingChatClient"/> tool loop can be driven without any network access.
/// </summary>
public sealed class ScriptedChatClient : IChatClient
{
    private readonly List<Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>> _turns = [];
    private int _callIndex = -1;

    public ChatClientMetadata Metadata { get; set; } = new("test.provider", new Uri("https://provider.test"), "test-default-model");

    public List<ChatOptions?> ObservedOptions { get; } = [];

    public int CallCount => _callIndex + 1;

    public ScriptedChatClient AddTurn(params ChatResponseUpdate[] updates)
    {
        _turns.Add(_ => ToAsyncEnumerable(updates));
        return this;
    }

    public ScriptedChatClient AddTurn(Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> turn)
    {
        _turns.Add(turn);
        return this;
    }

    /// <summary>
    /// Adds a turn that yields one text update and then blocks until <paramref name="release"/>
    /// completes, honoring cancellation.
    /// </summary>
    public ScriptedChatClient AddBlockingTurn(TaskCompletionSource release)
    {
        _turns.Add(ct => BlockingTurnAsync(release, ct));
        return this;
    }

    public static ChatResponseUpdate TextUpdate(string text, string? modelId = null) =>
        new(ChatRole.Assistant, text) { ModelId = modelId };

    public static ChatResponseUpdate FunctionCallUpdate(string callId, string name, IDictionary<string, object?>? arguments = null) =>
        new(ChatRole.Assistant, [new FunctionCallContent(callId, name, arguments)]);

    public static ChatResponseUpdate UsageUpdate(long inputTokens, long outputTokens, string? modelId = null)
    {
        var details = new UsageDetails
        {
            InputTokenCount = inputTokens,
            OutputTokenCount = outputTokens,
            TotalTokenCount = inputTokens + outputTokens,
        };
        return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(details)]) { ModelId = modelId };
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        BuildResponseAsync(messages, options, cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObservedOptions.Add(options);
        return NextTurn()(cancellationToken);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey is not null)
        {
            return null;
        }

        if (serviceType == typeof(ChatClientMetadata))
        {
            return Metadata;
        }

        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
    }

    private async Task<ChatResponse> BuildResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        _ = messages;
        ObservedOptions.Add(options);

        List<AIContent> contents = [];
        UsageDetails? usage = null;
        string? modelId = null;
        await foreach (ChatResponseUpdate update in NextTurn()(cancellationToken))
        {
            modelId ??= update.ModelId;
            foreach (AIContent content in update.Contents)
            {
                if (content is UsageContent usageContent)
                {
                    usage ??= new UsageDetails();
                    usage.Add(usageContent.Details);
                }
                else
                {
                    contents.Add(content);
                }
            }
        }

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, contents))
        {
            Usage = usage,
            ModelId = modelId,
        };
    }

    private Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> NextTurn()
    {
        int index = Interlocked.Increment(ref _callIndex);
        if (index >= _turns.Count)
        {
            throw new InvalidOperationException($"ScriptedChatClient has {_turns.Count} scripted turn(s) but call {index + 1} was made.");
        }

        return _turns[index];
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ToAsyncEnumerable(
        ChatResponseUpdate[] updates,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (ChatResponseUpdate update in updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return update;
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> BlockingTurnAsync(
        TaskCompletionSource release,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return TextUpdate("partial");
        await release.Task.WaitAsync(cancellationToken);
        yield return TextUpdate("resumed");
    }
}
