using Enterprise.AI.Middleware.Tests.TestDoubles;
using Enterprise.AI.Middleware.Tracking;
using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tests;

public sealed class McpProgressTests : IClassFixture<InMemoryMcpFixture>
{
    private sealed class PassthroughFunction : DelegatingAIFunction
    {
        public PassthroughFunction(AIFunction innerFunction)
            : base(innerFunction)
        {
        }
    }

    private readonly InMemoryMcpFixture _mcp;

    public McpProgressTests(InMemoryMcpFixture mcp)
    {
        _mcp = mcp;
    }

    private static ScriptedChatClient CreateCountDownScript(bool waitForAck)
    {
        return new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.FunctionCallUpdate(
                "call-1",
                "count_down",
                new Dictionary<string, object?> { ["steps"] = 3, ["waitForAck"] = waitForAck }))
            .AddTurn(ScriptedChatClient.TextUpdate("done"));
    }

    [Fact]
    public async Task McpProgress_ServerReportsProgress_EmitsStatusReportedWithMcpHeaderAndValues()
    {
        AIFunction tool = _mcp.CountDownTool.WithTrackingMetadata(_mcp.Client);
        var ack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _mcp.ProgressAck = ack;
        int statusCount = 0;
        var observer = new RecordingObserver
        {
            // Notification handlers may run out of order, so release the gate on count rather
            // than on seeing the final step's value.
            EventCallback = e =>
            {
                if (e.Kind == ChatActivityEventKind.StatusReported &&
                    Interlocked.Increment(ref statusCount) == 3)
                {
                    ack.TrySetResult();
                }
            },
        };
        IChatClient client = TestPipeline.Build(CreateCountDownScript(waitForAck: true), observer: observer);

        await TestPipeline.DrainAsync(client.GetStreamingResponseAsync(
            TestPipeline.UserMessage("count to three"),
            new ChatOptions { Tools = [tool] }));

        IReadOnlyList<ChatActivityEvent> statuses = observer.EventsOfKind(ChatActivityEventKind.StatusReported);
        Assert.Equal(3, statuses.Count);
        Assert.All(statuses, s =>
        {
            Assert.Equal(ToolKind.Mcp, s.ToolKind);
            Assert.Equal("count_down", s.ToolName);
            Assert.Equal($"Calling {InMemoryMcpFixture.ServerName} MCP", s.Header);
            Assert.Equal(3, s.ProgressTotal);
        });

        // Notification dispatch order is not guaranteed, so assert content as a set. Step 2 has
        // no server message, exercising the synthesized "{progress}/{total}" fallback.
        Assert.Equal(
            new[] { "2/3", "step 1 of 3", "step 3 of 3" },
            statuses.Select(s => s.Subheader).Order().ToArray());
        ChatActivityEvent secondStep = statuses.Single(s => s.Progress == 2);
        Assert.Equal("2/3", secondStep.Subheader);

        ChatActivityEvent started = observer.EventsOfKind(ChatActivityEventKind.ToolCallStarted).Single();
        Assert.All(statuses, s => Assert.Equal(started.ScopeId, s.ScopeId));
    }

    [Fact]
    public async Task McpProgress_Disabled_EmitsNoStatusReportedEvents()
    {
        AIFunction tool = _mcp.CountDownTool.WithTrackingMetadata(_mcp.Client);
        var observer = new RecordingObserver();
        var options = new ToolTrackingOptions { EnableMcpProgress = false };
        IChatClient client = TestPipeline.Build(CreateCountDownScript(waitForAck: false), options, observer);

        await TestPipeline.DrainAsync(client.GetStreamingResponseAsync(
            TestPipeline.UserMessage("count to three"),
            new ChatOptions { Tools = [tool] }));

        Assert.Empty(observer.EventsOfKind(ChatActivityEventKind.StatusReported));
        Assert.Single(observer.EventsOfKind(ChatActivityEventKind.ToolCallCompleted));
    }

    [Fact]
    public async Task McpProgress_UserWrappedMcpTool_NoBridgeButInvocationSucceeds()
    {
        AIFunction tool = new PassthroughFunction(_mcp.CountDownTool);
        var observer = new RecordingObserver();
        IChatClient client = TestPipeline.Build(CreateCountDownScript(waitForAck: false), observer: observer);

        await TestPipeline.DrainAsync(client.GetStreamingResponseAsync(
            TestPipeline.UserMessage("count to three"),
            new ChatOptions { Tools = [tool] }));

        Assert.Empty(observer.EventsOfKind(ChatActivityEventKind.StatusReported));

        ChatActivityEvent started = observer.EventsOfKind(ChatActivityEventKind.ToolCallStarted).Single();
        Assert.Equal(ToolKind.Mcp, started.ToolKind);
        Assert.Single(observer.EventsOfKind(ChatActivityEventKind.ToolCallCompleted));
    }
}
