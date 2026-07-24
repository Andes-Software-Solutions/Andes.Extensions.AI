using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Unit.Test.Infrastructure;

/// <summary>
/// A fake innermost provider that replays scripted turns, so the real
/// <see cref="FunctionInvokingChatClient"/> drives the middleware under test without a network.
/// </summary>
internal sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<ScriptedTurn> _turns;

    public ScriptedChatClient(params ScriptedTurn[] turns)
    {
        _turns = new Queue<ScriptedTurn>(turns);
    }

    public ChatClientMetadata Metadata { get; } = new("scripted", providerUri: null, defaultModelId: "fake-model");

    public int CallCount { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ScriptedTurn turn = NextTurn();
        return Task.FromResult(turn.Updates.ToChatResponse());
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ScriptedTurn turn = NextTurn();
        foreach (ChatResponseUpdate update in turn.Updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (turn.BeforeEachUpdate is { } gate)
            {
                await gate();
            }

            await Task.Yield();
            yield return update;
        }
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

    private ScriptedTurn NextTurn()
    {
        CallCount++;
        return _turns.Count > 0
            ? _turns.Dequeue()
            : throw new InvalidOperationException("The script has no turns left; the pipeline made more inner calls than expected.");
    }
}
