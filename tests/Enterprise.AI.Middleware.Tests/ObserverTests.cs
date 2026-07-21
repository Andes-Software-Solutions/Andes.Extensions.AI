using Enterprise.AI.Middleware.Tests.TestDoubles;
using Enterprise.AI.Middleware.Tracking;
using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tests;

public sealed class ObserverTests
{
    private sealed class ThrowingObserver : IChatActivityObserver
    {
        public void OnActivityEvent(ChatActivityEvent activityEvent) =>
            throw new InvalidOperationException("observer bug");

        public void OnRequestCompleted(ChatActivityReport report) =>
            throw new InvalidOperationException("observer bug");
    }

    [Fact]
    public async Task OnActivityEvent_ToolLifecycle_ReceivesOrderedEvents()
    {
        AIFunction tool = AIFunctionFactory.Create(() => "ok", "ordered_tool");
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.FunctionCallUpdate("call-1", "ordered_tool"))
            .AddTurn(ScriptedChatClient.TextUpdate("done"));
        var observer = new RecordingObserver();
        IChatClient client = TestPipeline.Build(inner, observer: observer);

        await TestPipeline.DrainAsync(client.GetStreamingResponseAsync(
            TestPipeline.UserMessage("go"),
            new ChatOptions { Tools = [tool] }));

        List<ChatActivityEventKind> kinds = [.. observer.Events.Select(e => e.Kind)];
        int started = kinds.IndexOf(ChatActivityEventKind.RequestStarted);
        int toolStarted = kinds.IndexOf(ChatActivityEventKind.ToolCallStarted);
        int toolCompleted = kinds.IndexOf(ChatActivityEventKind.ToolCallCompleted);
        int requestCompleted = kinds.IndexOf(ChatActivityEventKind.RequestCompleted);

        Assert.True(started < toolStarted, "RequestStarted must precede ToolCallStarted");
        Assert.True(toolStarted < toolCompleted, "ToolCallStarted must precede ToolCallCompleted");
        Assert.True(toolCompleted < requestCompleted, "ToolCallCompleted must precede RequestCompleted");
    }

    [Fact]
    public async Task OnActivityEvent_ObserverThrows_StreamCompletesNormally()
    {
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.TextUpdate("hello"));
        IChatClient client = TestPipeline.Build(inner, observer: new ThrowingObserver());

        List<ChatResponseUpdate> updates = await TestPipeline.DrainAsync(
            client.GetStreamingResponseAsync(TestPipeline.UserMessage("hi")));

        Assert.Contains(updates, u => u.Text.Contains("hello", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnRequestCompleted_StreamingAndNonStreaming_FiresExactlyOncePerRequest()
    {
        var observer = new RecordingObserver();

        ScriptedChatClient streamingInner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.TextUpdate("streamed"));
        await TestPipeline.DrainAsync(TestPipeline.Build(streamingInner, observer: observer)
            .GetStreamingResponseAsync(TestPipeline.UserMessage("hi")));

        ScriptedChatClient nonStreamingInner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.TextUpdate("direct"));
        await TestPipeline.Build(nonStreamingInner, observer: observer)
            .GetResponseAsync(TestPipeline.UserMessage("hi"));

        Assert.Equal(2, observer.Reports.Count);
        Assert.Equal(2, observer.EventsOfKind(ChatActivityEventKind.RequestCompleted).Count);
    }

    [Fact]
    public async Task OnActivityEvent_ToolThrows_ReceivesToolCallFailedWithErrorTypeOnly()
    {
        AIFunction tool = AIFunctionFactory.Create(
            string () => throw new InvalidOperationException("secret detail"),
            "failing_tool");
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.FunctionCallUpdate("call-1", "failing_tool"))
            .AddTurn(ScriptedChatClient.TextUpdate("recovered"));
        var observer = new RecordingObserver();
        IChatClient client = TestPipeline.Build(inner, observer: observer);

        await TestPipeline.DrainAsync(client.GetStreamingResponseAsync(
            TestPipeline.UserMessage("go"),
            new ChatOptions { Tools = [tool] }));

        ChatActivityEvent failed = observer.EventsOfKind(ChatActivityEventKind.ToolCallFailed).Single();
        Assert.Equal(typeof(InvalidOperationException).FullName, failed.ErrorType);
        Assert.Equal("failing_tool", failed.ToolName);
        Assert.NotNull(failed.Duration);
    }

    [Fact]
    public async Task OnActivityEvent_ActivityTagSet_CopiedToEveryEvent()
    {
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.TextUpdate("hello"));
        var observer = new RecordingObserver();
        IChatClient client = TestPipeline.Build(inner, observer: observer);
        ChatOptions options = new ChatOptions().WithActivityTag("connection-42");

        await TestPipeline.DrainAsync(client.GetStreamingResponseAsync(
            TestPipeline.UserMessage("hi"),
            options));

        Assert.All(observer.Events, e => Assert.Equal("connection-42", e.ActivityTag));
    }
}
