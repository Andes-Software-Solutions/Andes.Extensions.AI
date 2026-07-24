using Andes.Extensions.AI.Unit.Test.Infrastructure;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Agent.Unit.Test;

public class AgentUsageTrackingTests
{
    [Fact]
    public async Task WithTracking_AgentUsage_AttributedToToolScope()
    {
        AIAgent agent = CreateWeatherAgent(ScriptedTurn.Text("Sunny", InnerUsage()));

        ChatUsageReport report = await RunPipelineAsync(agent.WithTracking());

        ToolCallUsage call = Assert.Single(report.ToolCalls);
        Assert.NotNull(call.Usage);
        Assert.Equal(100, call.Usage.TotalTokenCount);
        Assert.Equal(130, report.TotalUsage.TotalTokenCount);
    }

    [Fact]
    public async Task WithTracking_TrackUsageFalse_NoUsageAttributed()
    {
        AIAgent agent = CreateWeatherAgent(ScriptedTurn.Text("Sunny", InnerUsage()));

        ChatUsageReport report = await RunPipelineAsync(agent.WithTracking(trackUsage: false));

        ToolCallUsage call = Assert.Single(report.ToolCalls);
        Assert.Null(call.Usage);
        Assert.Equal(30, report.TotalUsage.TotalTokenCount);
    }

    [Fact]
    public async Task WithTracking_NestedTrackedPipelineWithTrackUsageFalse_NoDoubleCount()
    {
        var innerScripted = new ScriptedChatClient(ScriptedTurn.Text("Sunny", InnerUsage()));
        AIAgent agent = TestPipeline.Build(innerScripted).AsAIAgent(
            instructions: "You answer questions about the weather.",
            name: "Weather Agent");

        ChatUsageReport report = await RunPipelineAsync(agent.WithTracking(trackUsage: false));

        ToolCallUsage call = Assert.Single(report.ToolCalls);
        Assert.NotNull(call.Usage);
        Assert.Equal(100, call.Usage.TotalTokenCount);
        Assert.Equal(130, report.TotalUsage.TotalTokenCount);
    }

    [Fact]
    public async Task WithTracking_NestedTrackedPipelineWithTrackUsageTrue_DoubleCounts()
    {
        // Demonstrates why trackUsage: false exists: the nested pipeline rolls up its total AND
        // the wrapper reports AgentResponse.Usage, attributing the same tokens twice.
        var innerScripted = new ScriptedChatClient(ScriptedTurn.Text("Sunny", InnerUsage()));
        AIAgent agent = TestPipeline.Build(innerScripted).AsAIAgent(
            instructions: "You answer questions about the weather.",
            name: "Weather Agent");

        ChatUsageReport report = await RunPipelineAsync(agent.WithTracking());

        ToolCallUsage call = Assert.Single(report.ToolCalls);
        Assert.NotNull(call.Usage);
        Assert.Equal(200, call.Usage.TotalTokenCount);
        Assert.Equal(230, report.TotalUsage.TotalTokenCount);
    }

    private static async Task<ChatUsageReport> RunPipelineAsync(AIFunction wrapped)
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Weather_Agent", new Dictionary<string, object?> { ["query"] = "weather?" }),
            ScriptedTurn.Text("Done.", new UsageDetails { InputTokenCount = 20, OutputTokenCount = 10, TotalTokenCount = 30 }));
        IChatClient client = TestPipeline.Build(scripted, options => options.UseAgentToolClassification());

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(
            client,
            new ChatOptions { Tools = [wrapped] });

        return TestPipeline.ReportOf(updates);
    }

    private static AIAgent CreateWeatherAgent(params ScriptedTurn[] turns)
    {
        return new ScriptedChatClient(turns).AsAIAgent(
            instructions: "You answer questions about the weather.",
            name: "Weather Agent");
    }

    private static UsageDetails InnerUsage()
    {
        return new UsageDetails { InputTokenCount = 60, OutputTokenCount = 40, TotalTokenCount = 100 };
    }
}
