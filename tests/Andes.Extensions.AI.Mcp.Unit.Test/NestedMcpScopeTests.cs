using Andes.Extensions.AI.Mcp.Unit.Test.Infrastructure;
using Andes.Extensions.AI.Unit.Test.Infrastructure;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace Andes.Extensions.AI.Mcp.Unit.Test;

public class NestedMcpScopeTests(InMemoryMcpFixture fixture) : IClassFixture<InMemoryMcpFixture>
{
    private readonly InMemoryMcpFixture _fixture = fixture;

    [Fact]
    public async Task WithTracking_InvokedDirectlyInsideFunctionTool_OpensChildScope()
    {
        AIFunction echo = _fixture.EchoTool.WithTracking(_fixture.Client);
        AIFunction outer = AIFunctionFactory.Create(
            async (CancellationToken cancellationToken) =>
            {
                await echo.InvokeAsync(new AIFunctionArguments { ["message"] = "hi" }, cancellationToken);
                return "done";
            },
            "Outer");
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Outer"),
            ScriptedTurn.Text("Done."));
        IChatClient client = TestPipeline.Build(scripted, options => options.UseMcpToolClassification());

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [outer] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);
        ChatUsageReport report = TestPipeline.ReportOf(updates);

        List<ChatProgressUpdate> invoking = progress.Where(update => update.Kind == ChatProgressKind.ToolInvoking).ToList();
        Assert.Equal(2, invoking.Count);
        Assert.Equal("Outer", invoking[0].ToolName);
        Assert.Equal("echo", invoking[1].ToolName);
        Assert.Equal(ToolKind.McpTool, invoking[1].ToolKind);
        Assert.Equal(InMemoryMcpFixture.ServerName, invoking[1].ToolSource);
        Assert.Equal(invoking[0].ScopeId, invoking[1].ParentScopeId);
        Assert.Null(invoking[1].CallId);
        Assert.Single(
            progress,
            update => update.Kind == ChatProgressKind.ToolCompleted && update.ScopeId == invoking[1].ScopeId);

        ToolCallUsage call = Assert.Single(report.ToolCalls);
        ToolCallUsage child = Assert.Single(call.Children);
        Assert.Equal("echo", child.ToolName);
        Assert.Equal(ToolKind.McpTool, child.Kind);
        Assert.True(child.Succeeded);
    }

    [Fact]
    public async Task WithTracking_BridgedProgressInsideNestedScope_LandsOnChildScope()
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
        AIFunction countDown = _fixture.CountDownTool.WithTracking(_fixture.Client);
        AIFunction outer = AIFunctionFactory.Create(
            async (CancellationToken cancellationToken) =>
            {
                await countDown.InvokeAsync(
                    new AIFunctionArguments { ["steps"] = 3, ["waitForAck"] = true },
                    cancellationToken);
                return "done";
            },
            "Outer");
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Outer"),
            ScriptedTurn.Text("Done."));
        IChatClient client = TestPipeline.Build(scripted, options =>
        {
            options.UseMcpToolClassification();
            options.Observers.Add(observer);
        });

        await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [outer] });

        ChatProgressUpdate childInvoking = Assert.Single(
            observer.Updates,
            update => update.Kind == ChatProgressKind.ToolInvoking && update.ToolName == "count_down");
        List<ChatProgressUpdate> statuses = observer.Updates
            .Where(update => update.Kind == ChatProgressKind.ToolProgress)
            .ToList();
        Assert.Equal(3, statuses.Count);
        Assert.All(statuses, status => Assert.Equal(childInvoking.ScopeId, status.ScopeId));
    }

    [Fact]
    public async Task WithTracking_WrappedByOuterTracker_OpensExactlyOneScope()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "echo", new Dictionary<string, object?> { ["message"] = "hi" }),
            ScriptedTurn.Text("Done."));
        IChatClient client = TestPipeline.Build(scripted, options => options.UseMcpToolClassification());

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(
            client,
            new ChatOptions { Tools = [_fixture.EchoTool.WithTracking(_fixture.Client)] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);
        ChatUsageReport report = TestPipeline.ReportOf(updates);

        Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolInvoking);
        ToolCallUsage call = Assert.Single(report.ToolCalls);
        Assert.Empty(call.Children);
    }
}
