using Andes.Extensions.AI.Unit.Test.Infrastructure;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Unit.Test;

public class StreamingProgressTests
{
    [Fact]
    public async Task GetStreamingResponseAsync_ToolLoop_EmitsEventsInExpectedOrder()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "GetWeather"),
            ScriptedTurn.Text("It's sunny."));
        AIFunction tool = AIFunctionFactory.Create(
            () =>
            {
                ChatProgress.Report("Extracting...");
                return "sunny";
            },
            "GetWeather");
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        Assert.Equal(ChatProgressKind.RequestStarted, progress[0].Kind);
        Assert.Equal(ChatProgressKind.Thinking, progress[1].Kind);

        int invokingIndex = TestPipeline.IndexOfProgress(updates, ChatProgressKind.ToolInvoking);
        int resultIndex = TestPipeline.IndexOfContent<FunctionResultContent>(updates);
        Assert.True(invokingIndex >= 0 && resultIndex >= 0 && invokingIndex < resultIndex,
            $"ToolInvoking (index {invokingIndex}) must precede the function result (index {resultIndex}).");

        List<ChatProgressKind> kinds = progress.Select(update => update.Kind).ToList();
        int toolInvoking = kinds.IndexOf(ChatProgressKind.ToolInvoking);
        int toolProgress = kinds.IndexOf(ChatProgressKind.ToolProgress);
        int toolCompleted = kinds.IndexOf(ChatProgressKind.ToolCompleted);
        Assert.True(toolInvoking < toolProgress && toolProgress < toolCompleted,
            "The sub-status must fall between the tool header and the tool completion.");
        Assert.Equal("Calling GetWeather Tool", progress[toolInvoking].Message);
        Assert.Equal("Extracting...", progress[toolProgress].Message);

        Assert.Contains(kinds.Skip(toolCompleted), kind => kind == ChatProgressKind.Thinking);
        Assert.Equal(ChatProgressKind.RequestCompleted, progress[^1].Kind);
        Assert.IsType<UsageReportContent>(updates[^1].Contents.Single());

        string text = string.Concat(updates.Select(update => update.Text));
        Assert.Contains("It's sunny.", text);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ProgressContentDisabled_ObserversStillNotified()
    {
        var observer = new CollectingProgressObserver();
        var scripted = new ScriptedChatClient(ScriptedTurn.Text("Hello!"));
        IChatClient client = TestPipeline.Build(scripted, options =>
        {
            options.EmitProgressContent = false;
            options.Observers.Add(observer);
        });

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client);

        Assert.Empty(updates.SelectMany(update => update.Contents).OfType<ChatProgressContent>());
        Assert.NotEmpty(updates.SelectMany(update => update.Contents).OfType<UsageReportContent>());
        Assert.Contains(observer.Updates, update => update.Kind == ChatProgressKind.RequestStarted);
        Assert.Contains(observer.Updates, update => update.Kind == ChatProgressKind.RequestCompleted);
        Assert.NotNull(observer.Report);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ToolThrows_EmitsToolFailedAndStreamCompletes()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Boom"),
            ScriptedTurn.Text("Recovered."));
        AIFunction tool = AIFunctionFactory.Create(
            new Func<string>(() => throw new InvalidOperationException("kaboom")),
            "Boom");
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        ChatProgressUpdate failed = Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolFailed);
        Assert.Equal("Boom", failed.ToolName);
        Assert.Equal(ChatProgressKind.RequestCompleted, progress[^1].Kind);

        ChatUsageReport report = TestPipeline.ReportOf(updates);
        ToolCallUsage call = Assert.Single(report.ToolCalls);
        Assert.False(call.Succeeded);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ObserverThrows_StreamIsUnaffected()
    {
        var throwing = new ThrowingObserver();
        var collecting = new CollectingProgressObserver();
        var scripted = new ScriptedChatClient(ScriptedTurn.Text("Hello!"));
        IChatClient client = TestPipeline.Build(scripted, options =>
        {
            options.Observers.Add(throwing);
            options.Observers.Add(collecting);
        });

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client);

        Assert.Contains("Hello!", string.Concat(updates.Select(update => update.Text)));
        Assert.NotNull(collecting.Report);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_Canceled_ThrowsOperationCanceledWithoutHanging()
    {
        using var cancellation = new CancellationTokenSource();
        var scripted = new ScriptedChatClient(new ScriptedTurn
        {
            Updates =
            [
                new ChatResponseUpdate(ChatRole.Assistant, "chunk1"),
                new ChatResponseUpdate(ChatRole.Assistant, "chunk2"),
            ],
            BeforeEachUpdate = () =>
            {
                cancellation.Cancel();
                return Task.CompletedTask;
            },
        });
        IChatClient client = TestPipeline.Build(scripted);

        Task<OperationCanceledException> collecting = Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => TestPipeline.CollectAsync(client, cancellationToken: cancellation.Token));

        await collecting.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_InnerStreamFaults_ObserversStillReceiveAccounting()
    {
        var observer = new CollectingProgressObserver();
        var scripted = new ScriptedChatClient(new ScriptedTurn
        {
            Updates = [new ChatResponseUpdate(ChatRole.Assistant, "chunk1")],
            BeforeEachUpdate = () => Task.FromException(new InvalidOperationException("provider down")),
        });
        IChatClient client = TestPipeline.Build(scripted, options => options.Observers.Add(observer));

        await Assert.ThrowsAsync<InvalidOperationException>(() => TestPipeline.CollectAsync(client));

        Assert.Contains(observer.Updates, update => update.Kind == ChatProgressKind.RequestFailed);
        Assert.DoesNotContain(observer.Updates, update => update.Kind == ChatProgressKind.RequestCompleted);
        Assert.NotNull(observer.Report);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ConsumerAbandonsStream_ObserversStillReceiveAccounting()
    {
        var observer = new CollectingProgressObserver();
        var scripted = new ScriptedChatClient(ScriptedTurn.Text("a long answer", new UsageDetails { TotalTokenCount = 9 }));
        IChatClient client = TestPipeline.Build(scripted, options => options.Observers.Add(observer));

        await foreach (ChatResponseUpdate _ in client.GetStreamingResponseAsync("prompt"))
        {
            break;
        }

        Assert.Contains(observer.Updates, update => update.Kind == ChatProgressKind.RequestFailed);
        Assert.NotNull(observer.Report);
    }

    private sealed class ThrowingObserver : IChatProgressObserver
    {
        public void OnProgress(ChatProgressUpdate update)
        {
            throw new InvalidOperationException("observer failure");
        }

        public void OnRequestCompleted(ChatUsageReport report)
        {
            throw new InvalidOperationException("observer failure");
        }
    }
}
