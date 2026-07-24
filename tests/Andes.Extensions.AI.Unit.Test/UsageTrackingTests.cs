using Andes.Extensions.AI.Unit.Test.Infrastructure;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Unit.Test;

public class UsageTrackingTests
{
    [Fact]
    public async Task GetStreamingResponseAsync_SingleTurnWithoutTools_ReportsAssistantUsage()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.Text(
                "Hello!",
                new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5, TotalTokenCount = 15 },
                responseId: "resp-1",
                modelId: "gpt-test"));
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client);
        ChatUsageReport report = TestPipeline.ReportOf(updates);

        Assert.Equal(10, report.AssistantUsage.InputTokenCount);
        Assert.Equal(5, report.AssistantUsage.OutputTokenCount);
        Assert.Equal(15, report.AssistantUsage.TotalTokenCount);
        Assert.Equal(15, report.TotalUsage.TotalTokenCount);
        Assert.Equal("resp-1", report.ResponseId);
        Assert.Equal("gpt-test", report.ModelId);
        Assert.Equal("scripted", report.ProviderName);
        Assert.Empty(report.ToolCalls);
        AssistantTurnUsage turn = Assert.Single(report.Turns);
        Assert.Equal(0, turn.Iteration);
        Assert.Equal(15, turn.Usage.TotalTokenCount);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ToolLoop_SumsUsageAcrossTurns()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall(
                "call-1",
                "GetWeather",
                usage: new UsageDetails { InputTokenCount = 20, OutputTokenCount = 10, TotalTokenCount = 30 }),
            ScriptedTurn.Text(
                "It's sunny.",
                new UsageDetails { InputTokenCount = 30, OutputTokenCount = 15, TotalTokenCount = 45 }));
        AIFunction tool = AIFunctionFactory.Create(() => "sunny", "GetWeather");
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });
        ChatUsageReport report = TestPipeline.ReportOf(updates);

        Assert.Equal(50, report.AssistantUsage.InputTokenCount);
        Assert.Equal(25, report.AssistantUsage.OutputTokenCount);
        Assert.Equal(75, report.AssistantUsage.TotalTokenCount);
        Assert.Equal(2, report.Turns.Count);
        Assert.Equal(0, report.Turns[0].Iteration);
        Assert.Equal(1, report.Turns[1].Iteration);
        Assert.Equal(30, report.Turns[0].Usage.TotalTokenCount);
        Assert.Equal(45, report.Turns[1].Usage.TotalTokenCount);
        ToolCallUsage call = Assert.Single(report.ToolCalls);
        Assert.Equal("GetWeather", call.ToolName);
        Assert.Equal("call-1", call.CallId);
        Assert.True(call.Succeeded);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ProviderReportsNoUsage_ReportIsWellFormed()
    {
        var scripted = new ScriptedChatClient(ScriptedTurn.Text("Hello!"));
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client);
        ChatUsageReport report = TestPipeline.ReportOf(updates);

        Assert.Null(report.AssistantUsage.TotalTokenCount);
        Assert.Null(report.TotalUsage.TotalTokenCount);
        Assert.Empty(report.Turns);
        Assert.Empty(report.ToolCalls);
        Assert.Equal("fake-model", report.ModelId);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ToolReportsUsage_TotalIncludesToolRollup()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Lookup"),
            ScriptedTurn.Text("Done.", new UsageDetails { TotalTokenCount = 30 }));
        AIFunction tool = AIFunctionFactory.Create(
            () =>
            {
                ChatProgress.ReportUsage(new UsageDetails { TotalTokenCount = 7 });
                return "found";
            },
            "Lookup");
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });
        ChatUsageReport report = TestPipeline.ReportOf(updates);

        Assert.Equal(30, report.AssistantUsage.TotalTokenCount);
        ToolCallUsage call = Assert.Single(report.ToolCalls);
        Assert.NotNull(call.Usage);
        Assert.Equal(7, call.Usage.TotalTokenCount);
        Assert.Equal(37, report.TotalUsage.TotalTokenCount);
    }
}
