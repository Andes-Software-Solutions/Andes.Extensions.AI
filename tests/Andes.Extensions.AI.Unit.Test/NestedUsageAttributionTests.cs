using Andes.Extensions.AI.Unit.Test.Infrastructure;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Unit.Test;

public class NestedUsageAttributionTests
{
    [Fact]
    public async Task ReportUsage_InsideTool_AttributedToToolNotAssistant()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Lookup"),
            ScriptedTurn.Text("Done.", new UsageDetails { TotalTokenCount = 20 }));
        AIFunction tool = AIFunctionFactory.Create(
            () =>
            {
                ChatProgress.ReportUsage(new UsageDetails { InputTokenCount = 3, OutputTokenCount = 2, TotalTokenCount = 5 });
                return "found";
            },
            "Lookup");
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });
        ChatUsageReport report = TestPipeline.ReportOf(updates);

        Assert.Equal(20, report.AssistantUsage.TotalTokenCount);
        ToolCallUsage call = Assert.Single(report.ToolCalls);
        Assert.NotNull(call.Usage);
        Assert.Equal(5, call.Usage.TotalTokenCount);
        Assert.Equal(3, call.Usage.InputTokenCount);
        Assert.Equal(25, report.TotalUsage.TotalTokenCount);
    }

    [Fact]
    public async Task NestedTrackedPipeline_InsideTool_RollsTotalUpIntoOwningToolScope()
    {
        var nestedObserver = new CollectingProgressObserver();
        AIFunction agentTool = AIFunctionFactory.Create(
            async () =>
            {
                var nestedProvider = new ScriptedChatClient(
                    ScriptedTurn.Text("nested answer", new UsageDetails { TotalTokenCount = 100 }));
                IChatClient nestedClient = TestPipeline.Build(
                    nestedProvider,
                    options => options.Observers.Add(nestedObserver));
                await TestPipeline.CollectAsync(nestedClient);
                return "agent result";
            },
            "ResearchAgent");
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "ResearchAgent"),
            ScriptedTurn.Text("Done.", new UsageDetails { TotalTokenCount = 30 }));
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [agentTool] });
        ChatUsageReport report = TestPipeline.ReportOf(updates);

        Assert.Equal(30, report.AssistantUsage.TotalTokenCount);
        ToolCallUsage call = Assert.Single(report.ToolCalls);
        Assert.NotNull(call.Usage);
        Assert.Equal(100, call.Usage.TotalTokenCount);
        Assert.Equal(130, report.TotalUsage.TotalTokenCount);

        Assert.NotNull(nestedObserver.Report);
        Assert.Equal(100, nestedObserver.Report.AssistantUsage.TotalTokenCount);
    }

    [Fact]
    public async Task NestedTrackedPipeline_WithItsOwnTool_RollsUpTransitively()
    {
        AIFunction innerTool = AIFunctionFactory.Create(
            () =>
            {
                ChatProgress.ReportUsage(new UsageDetails { TotalTokenCount = 4 });
                return "leaf";
            },
            "LeafTool");
        AIFunction agentTool = AIFunctionFactory.Create(
            async () =>
            {
                var nestedProvider = new ScriptedChatClient(
                    ScriptedTurn.FunctionCall("nested-call", "LeafTool"),
                    ScriptedTurn.Text("nested done", new UsageDetails { TotalTokenCount = 50 }));
                IChatClient nestedClient = TestPipeline.Build(nestedProvider);
                await TestPipeline.CollectAsync(nestedClient, new ChatOptions { Tools = [innerTool] });
                return "agent result";
            },
            "ResearchAgent");
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "ResearchAgent"),
            ScriptedTurn.Text("Done.", new UsageDetails { TotalTokenCount = 10 }));
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [agentTool] });
        ChatUsageReport report = TestPipeline.ReportOf(updates);

        ToolCallUsage call = Assert.Single(report.ToolCalls);
        Assert.NotNull(call.Usage);
        Assert.Equal(54, call.Usage.TotalTokenCount);
        Assert.Equal(64, report.TotalUsage.TotalTokenCount);
    }
}
