using Andes.Extensions.AI.Unit.Test.Infrastructure;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.UI.Unit.Test;

public class LiveNestedActivityTests
{
    [Fact]
    public async Task ToStatusSnapshotsAsync_NestedScopeInsideTool_ProducesChildActivityLive()
    {
        AIFunction tool = AIFunctionFactory.Create(
            () =>
            {
                using (ChatProgress.BeginToolScope(NestedDescriptor()))
                {
                    ChatProgress.Report("Condensing…");
                }

                return "done";
            },
            "Outer");
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Outer"),
            ScriptedTurn.Text("Done.", new UsageDetails { TotalTokenCount = 20 }));
        IChatClient client = TestPipeline.Build(scripted);

        AssistantStatusSnapshot? last = null;
        await foreach (AssistantStatusSnapshot snapshot in client
            .GetStreamingResponseAsync("prompt", new ChatOptions { Tools = [tool] })
            .ToStatusSnapshotsAsync())
        {
            last = snapshot;
        }

        Assert.NotNull(last);
        AssistantActivity root = Assert.Single(last.Activities);
        Assert.Equal(ActivityState.Completed, root.State);
        AssistantActivity child = Assert.Single(root.Children);
        Assert.Equal("Summarizer", child.DisplayName);
        Assert.Equal(ToolKind.Agent, child.Kind);
        Assert.Equal(ActivityState.Completed, child.State);
        SubStatus subStatus = Assert.Single(child.SubStatuses);
        Assert.Equal("Condensing…", subStatus.Message);
    }

    [Fact]
    public async Task ToSnapshot_ReportWithNestedChildren_MapsChildActivitiesWithUsage()
    {
        AIFunction tool = AIFunctionFactory.Create(
            () =>
            {
                using (ChatProgress.BeginToolScope(NestedDescriptor()))
                {
                    ChatProgress.ReportUsage(new UsageDetails { TotalTokenCount = 5 });
                }

                return "done";
            },
            "Outer");
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Outer"),
            ScriptedTurn.Text("Done.", new UsageDetails { TotalTokenCount = 20 }));
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });
        AssistantStatusSnapshot snapshot = TestPipeline.ReportOf(updates).ToSnapshot();

        AssistantActivity root = Assert.Single(snapshot.Activities);
        Assert.NotNull(root.Usage);
        Assert.Equal(5, root.Usage.TotalTokens);
        AssistantActivity child = Assert.Single(root.Children);
        Assert.Equal("Summarizer", child.DisplayName);
        Assert.Equal(ToolKind.Agent, child.Kind);
        Assert.NotNull(child.Usage);
        Assert.Equal(5, child.Usage.TotalTokens);
    }

    private static ToolDescriptor NestedDescriptor()
    {
        return new ToolDescriptor
        {
            Name = "summarize",
            DisplayName = "Summarizer",
            Kind = ToolKind.Agent,
            Source = "Summarizer",
        };
    }
}
