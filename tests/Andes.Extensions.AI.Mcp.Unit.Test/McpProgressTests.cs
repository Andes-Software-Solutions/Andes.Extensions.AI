using Andes.Extensions.AI.Mcp.Unit.Test.Infrastructure;
using Andes.Extensions.AI.Unit.Test.Infrastructure;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace Andes.Extensions.AI.Mcp.Unit.Test;

public class McpProgressTests(InMemoryMcpFixture fixture) : IClassFixture<InMemoryMcpFixture>
{
    private readonly InMemoryMcpFixture _fixture = fixture;

    [Fact]
    public async Task McpProgress_ServerReportsProgress_EmitsToolProgressWithNumericValues()
    {
        var ack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fixture.ProgressAck = ack;
        int progressCount = 0;
        var observer = new NotifyingProgressObserver
        {
            Callback = update =>
            {
                if (update.Kind == ChatProgressKind.ToolProgress && Interlocked.Increment(ref progressCount) == 3)
                {
                    ack.TrySetResult();
                }
            },
        };
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall(
                "call-1",
                "count_down",
                new Dictionary<string, object?> { ["steps"] = 3, ["waitForAck"] = true }),
            ScriptedTurn.Text("Done."));
        IChatClient client = TestPipeline.Build(scripted, options =>
        {
            options.UseMcpToolClassification();
            options.Observers.Add(observer);
        });

        await TestPipeline.CollectAsync(
            client,
            new ChatOptions { Tools = [_fixture.CountDownTool.WithTracking(_fixture.Client)] });

        List<ChatProgressUpdate> statuses = observer.Updates
            .Where(update => update.Kind == ChatProgressKind.ToolProgress)
            .ToList();
        Assert.Equal(3, statuses.Count);
        Assert.All(statuses, status =>
        {
            Assert.Equal(ToolKind.McpTool, status.ToolKind);
            Assert.Equal("count_down", status.ToolName);
            Assert.Equal(3, status.ProgressTotal);
        });

        // Notifications are dispatched on the MCP receive loop, so their relative order is not
        // guaranteed; assert the set. Step 2 exercises the "{progress}/{total}" fallback.
        Assert.Equal(
            ["2/3", "step 1 of 3", "step 3 of 3"],
            statuses.Select(status => status.Message).OrderBy(message => message, StringComparer.Ordinal));
        ChatProgressUpdate fallback = Assert.Single(statuses, status => status.Progress == 2);
        Assert.Equal("2/3", fallback.Message);

        ChatProgressUpdate invoking = Assert.Single(
            observer.Updates,
            update => update.Kind == ChatProgressKind.ToolInvoking);
        Assert.All(statuses, status => Assert.Equal(invoking.ScopeId, status.ScopeId));
    }

    [Fact]
    public async Task McpProgress_EnableProgressFalse_EmitsNoToolProgress()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall(
                "call-1",
                "count_down",
                new Dictionary<string, object?> { ["steps"] = 2, ["waitForAck"] = false }),
            ScriptedTurn.Text("Done."));
        IChatClient client = TestPipeline.Build(scripted, options => options.UseMcpToolClassification());

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(
            client,
            new ChatOptions { Tools = [_fixture.CountDownTool.WithTracking(_fixture.Client, enableProgress: false)] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        Assert.DoesNotContain(progress, update => update.Kind == ChatProgressKind.ToolProgress);
        Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolCompleted);
    }

    [Fact]
    public async Task McpProgress_UserWrappedMcpTool_ClassifiedMcpWithoutBridge()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall(
                "call-1",
                "count_down",
                new Dictionary<string, object?> { ["steps"] = 2, ["waitForAck"] = false }),
            ScriptedTurn.Text("Done."));
        IChatClient client = TestPipeline.Build(scripted, options => options.UseMcpToolClassification());

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(
            client,
            new ChatOptions { Tools = [new PassthroughFunction(_fixture.CountDownTool)] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        ChatProgressUpdate invoking = Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolInvoking);
        Assert.Equal(ToolKind.McpTool, invoking.ToolKind);
        Assert.DoesNotContain(progress, update => update.Kind == ChatProgressKind.ToolProgress);
        Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolCompleted);
    }

    [Fact]
    public async Task McpProgress_WithoutMcpClassifier_ProgressStillBridged()
    {
        var ack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _fixture.ProgressAck = ack;
        int progressCount = 0;
        var observer = new NotifyingProgressObserver
        {
            Callback = update =>
            {
                if (update.Kind == ChatProgressKind.ToolProgress && Interlocked.Increment(ref progressCount) == 3)
                {
                    ack.TrySetResult();
                }
            },
        };
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall(
                "call-1",
                "count_down",
                new Dictionary<string, object?> { ["steps"] = 3, ["waitForAck"] = true }),
            ScriptedTurn.Text("Done."));
        IChatClient client = TestPipeline.Build(scripted, options => options.Observers.Add(observer));

        await TestPipeline.CollectAsync(
            client,
            new ChatOptions { Tools = [_fixture.CountDownTool.WithTracking(_fixture.Client)] });

        List<ChatProgressUpdate> statuses = observer.Updates
            .Where(update => update.Kind == ChatProgressKind.ToolProgress)
            .ToList();
        Assert.Equal(3, statuses.Count);
        Assert.All(statuses, status =>
        {
            // The bridge is independent of classification: without UseMcpToolClassification the
            // wrapper classifies as a plain function, but progress still flows.
            Assert.Equal(ToolKind.Function, status.ToolKind);
            Assert.NotNull(status.Progress);
        });
    }

    private sealed class PassthroughFunction : DelegatingAIFunction
    {
        public PassthroughFunction(McpClientTool tool)
            : base(tool)
        {
        }
    }
}
