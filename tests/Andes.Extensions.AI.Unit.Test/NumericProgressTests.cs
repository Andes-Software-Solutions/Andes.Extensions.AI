using Andes.Extensions.AI.Unit.Test.Infrastructure;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Unit.Test;

public class NumericProgressTests
{
    [Fact]
    public async Task Report_WithProgressAndTotal_PopulatesUpdateFields()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Import"),
            ScriptedTurn.Text("Done."));
        AIFunction tool = AIFunctionFactory.Create(
            () =>
            {
                ChatProgress.Report("step 1", 1, 3);
                return "imported";
            },
            "Import");
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        ChatProgressUpdate invoking = Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolInvoking);
        ChatProgressUpdate status = Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolProgress);
        Assert.Equal("step 1", status.Message);
        Assert.Equal(1, status.Progress);
        Assert.Equal(3, status.ProgressTotal);
        Assert.Equal(invoking.ScopeId, status.ScopeId);
        Assert.Equal(invoking.Depth + 1, status.Depth);
    }

    [Fact]
    public async Task Report_StringOnlyOverload_LeavesProgressNull()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Import"),
            ScriptedTurn.Text("Done."));
        AIFunction tool = AIFunctionFactory.Create(
            () =>
            {
                ChatProgress.Report("working");
                return "imported";
            },
            "Import");
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        ChatProgressUpdate status = Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolProgress);
        Assert.Null(status.Progress);
        Assert.Null(status.ProgressTotal);
    }

    [Fact]
    public async Task Report_ProgressWithoutTotal_TotalRemainsNull()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Import"),
            ScriptedTurn.Text("Done."));
        AIFunction tool = AIFunctionFactory.Create(
            () =>
            {
                ChatProgress.Report("step 2", 2);
                return "imported";
            },
            "Import");
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        ChatProgressUpdate status = Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolProgress);
        Assert.Equal(2, status.Progress);
        Assert.Null(status.ProgressTotal);
    }

    [Fact]
    public void Report_WithValues_EmptyStatus_Throws()
    {
        Assert.Throws<ArgumentException>(() => ChatProgress.Report("", 1, 2));
        Assert.Throws<ArgumentNullException>(() => ChatProgress.Report(null!, 1, 2));
    }

    [Fact]
    public async Task CurrentReporter_ReportWithValues_EmitsToolProgressWithValues()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Import"),
            ScriptedTurn.Text("Done."));
        AIFunction tool = AIFunctionFactory.Create(
            () =>
            {
                IChatProgressReporter reporter = ChatProgress.Current;
                Assert.True(reporter.IsActive);
                reporter.Report("step 3", 3, 5);
                return "imported";
            },
            "Import");
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        ChatProgressUpdate status = Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolProgress);
        Assert.Equal("step 3", status.Message);
        Assert.Equal(3, status.Progress);
        Assert.Equal(5, status.ProgressTotal);
    }

    [Fact]
    public void CurrentReporter_OutsideRequest_ReportWithValuesIsNoop()
    {
        IChatProgressReporter reporter = ChatProgress.Current;

        Assert.False(reporter.IsActive);
        reporter.Report("nobody is listening", 1, 2);
        ChatProgress.Report("still nobody", 1, 2);
    }

    [Fact]
    public void ReporterInterface_DefaultImplementation_ForwardsToStringReport()
    {
        var minimal = new MinimalReporter();
        IChatProgressReporter reporter = minimal;

        reporter.Report("compressing", 4, 10);

        Assert.Equal("compressing", Assert.Single(minimal.Statuses));
    }

    private sealed class MinimalReporter : IChatProgressReporter
    {
        public List<string> Statuses { get; } = [];

        public bool IsActive => true;

        public void Report(string status)
        {
            Statuses.Add(status);
        }

        public void ReportUsage(UsageDetails usage)
        {
        }
    }
}
