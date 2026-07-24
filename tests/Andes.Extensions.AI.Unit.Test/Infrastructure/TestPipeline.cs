using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Unit.Test.Infrastructure;

/// <summary>
/// Builds the canonical pipeline under test (scripted provider → tool tracking → real
/// function invocation) and provides stream-collection helpers.
/// </summary>
internal static class TestPipeline
{
    public static IChatClient Build(
        ScriptedChatClient scripted,
        Action<ToolTrackingOptions>? configure = null,
        Action<FunctionInvokingChatClient>? configureInvocation = null,
        IServiceProvider? services = null)
    {
        return scripted
            .AsBuilder()
            .UseToolTracking(configure)
            .UseFunctionInvocation(configure: configureInvocation)
            .Build(services);
    }

    public static async Task<List<ChatResponseUpdate>> CollectAsync(
        IChatClient client,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync("prompt", options, cancellationToken))
        {
            updates.Add(update);
        }

        return updates;
    }

    public static List<ChatProgressUpdate> ProgressOf(IEnumerable<ChatResponseUpdate> updates)
    {
        return updates
            .SelectMany(update => update.Contents)
            .OfType<ChatProgressContent>()
            .Select(content => content.Progress)
            .ToList();
    }

    public static ChatUsageReport ReportOf(IEnumerable<ChatResponseUpdate> updates)
    {
        return updates
            .SelectMany(update => update.Contents)
            .OfType<UsageReportContent>()
            .Single()
            .Report;
    }

    public static int IndexOfProgress(IReadOnlyList<ChatResponseUpdate> updates, ChatProgressKind kind)
    {
        for (int i = 0; i < updates.Count; i++)
        {
            if (updates[i].Contents.OfType<ChatProgressContent>().Any(content => content.Progress.Kind == kind))
            {
                return i;
            }
        }

        return -1;
    }

    public static int IndexOfContent<TContent>(IReadOnlyList<ChatResponseUpdate> updates)
        where TContent : AIContent
    {
        for (int i = 0; i < updates.Count; i++)
        {
            if (updates[i].Contents.OfType<TContent>().Any())
            {
                return i;
            }
        }

        return -1;
    }
}
