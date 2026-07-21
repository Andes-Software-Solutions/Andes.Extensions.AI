using Enterprise.AI.Middleware.Tracking;
using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tests.TestDoubles;

/// <summary>
/// Builds the canonical test pipeline: tracking outermost, then real function invocation, then
/// the scripted inner client.
/// </summary>
internal static class TestPipeline
{
    public static IChatClient Build(
        IChatClient inner,
        ToolTrackingOptions? options = null,
        IChatActivityObserver? observer = null,
        bool allowConcurrentTools = false)
    {
        return new ChatClientBuilder(inner)
            .UseToolTracking(options ?? new ToolTrackingOptions(), observer)
            .UseFunctionInvocation(configure: client => client.AllowConcurrentInvocation = allowConcurrentTools)
            .Build();
    }

    public static async Task<List<ChatResponseUpdate>> DrainAsync(
        IAsyncEnumerable<ChatResponseUpdate> stream,
        CancellationToken cancellationToken = default)
    {
        List<ChatResponseUpdate> updates = [];
        await foreach (ChatResponseUpdate update in stream.WithCancellation(cancellationToken))
        {
            updates.Add(update);
        }

        return updates;
    }

    public static List<ActivityStatusContent> StatusContents(IEnumerable<ChatResponseUpdate> updates)
    {
        return [.. updates.SelectMany(update => update.Contents).OfType<ActivityStatusContent>()];
    }

    public static List<ChatMessage> UserMessage(string text)
    {
        return [new ChatMessage(ChatRole.User, text)];
    }
}
