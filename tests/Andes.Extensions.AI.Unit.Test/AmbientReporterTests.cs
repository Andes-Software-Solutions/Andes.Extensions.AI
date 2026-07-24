using Andes.Extensions.AI.Unit.Test.Infrastructure;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Unit.Test;

public class AmbientReporterTests
{
    [Fact]
    public async Task Report_InsideTool_AttachesSubStatusToToolScope()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "GetWeather"),
            ScriptedTurn.Text("Done."));
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

        ChatProgressUpdate invoking = Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolInvoking);
        ChatProgressUpdate subStatus = Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolProgress);
        Assert.Equal(invoking.ScopeId, subStatus.ScopeId);
        Assert.Equal(invoking.Depth + 1, subStatus.Depth);
        Assert.Equal("GetWeather", subStatus.ToolName);
    }

    [Fact]
    public void Report_OutsideTrackedRequest_IsSafeNoOp()
    {
        Assert.False(ChatProgress.IsActive);
        ChatProgress.Report("nobody is listening");
        ChatProgress.ReportUsage(new UsageDetails { TotalTokenCount = 1 });

        IChatProgressReporter reporter = ChatProgress.Current;
        Assert.False(reporter.IsActive);
        reporter.Report("still nobody");
        reporter.ReportUsage(new UsageDetails { TotalTokenCount = 1 });
    }

    [Fact]
    public async Task Report_ConcurrentTools_DoNotCrossTalk()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCalls([("call-a", "ToolA"), ("call-b", "ToolB")]),
            ScriptedTurn.Text("Done."));
        var aStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AIFunction toolA = AIFunctionFactory.Create(
            async () =>
            {
                aStarted.TrySetResult();
                await bStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
                ChatProgress.Report("A-status");
                return "a";
            },
            "ToolA");
        AIFunction toolB = AIFunctionFactory.Create(
            async () =>
            {
                bStarted.TrySetResult();
                await aStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
                ChatProgress.Report("B-status");
                return "b";
            },
            "ToolB");
        IChatClient client = TestPipeline.Build(
            scripted,
            configureInvocation: invocation => invocation.AllowConcurrentInvocation = true);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(
            client,
            new ChatOptions { Tools = [toolA, toolB] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        Dictionary<string, string> headerScopeByTool = progress
            .Where(update => update.Kind == ChatProgressKind.ToolInvoking)
            .ToDictionary(update => update.ToolName!, update => update.ScopeId);
        ChatProgressUpdate statusA = Assert.Single(progress, update => update.Message == "A-status");
        ChatProgressUpdate statusB = Assert.Single(progress, update => update.Message == "B-status");
        Assert.Equal(headerScopeByTool["ToolA"], statusA.ScopeId);
        Assert.Equal(headerScopeByTool["ToolB"], statusB.ScopeId);
        Assert.NotEqual(statusA.ScopeId, statusB.ScopeId);
    }
}
