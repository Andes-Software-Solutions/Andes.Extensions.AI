using Andes.Extensions.AI.Unit.Test.Infrastructure;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Agent.Unit.Test;

public class AgentFunctionCallReportingTests
{
    [Fact]
    public async Task WithTracking_ReportFunctionCalls_EmitsCallingFunctionSubheader()
    {
        AIAgent agent = CreateAgentWithTool(AIFunctionFactory.Create(() => "sunny", "GetWeather"));

        List<ChatProgressUpdate> progress = await RunPipelineAsync(agent.WithTracking(reportFunctionCalls: true));

        ChatProgressUpdate subheader = Assert.Single(
            progress,
            update => update.Kind == ChatProgressKind.ToolProgress && update.Message == "Calling GetWeather Tool");
        Assert.Equal("Weather_Agent", subheader.ToolName);
        ChatProgressUpdate invoking = Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolInvoking);
        Assert.Equal("Calling Weather Agent", invoking.Message);
    }

    [Fact]
    public async Task WithTracking_Default_NoFunctionCallSubheaders()
    {
        AIAgent agent = CreateAgentWithTool(AIFunctionFactory.Create(() => "sunny", "GetWeather"));

        List<ChatProgressUpdate> progress = await RunPipelineAsync(agent.WithTracking());

        Assert.DoesNotContain(progress, update => update.Message == "Calling GetWeather Tool");
    }

    [Fact]
    public async Task InnerToolChatProgressReport_PropagatesRegardlessOfFlag()
    {
        // The agent runs in-process on the caller's async flow, so the ambient reporter reaches
        // the agent's function tools without any bridging.
        AIFunction reportingTool = AIFunctionFactory.Create(
            () =>
            {
                ChatProgress.Report("Extracting…");
                return "sunny";
            },
            "GetWeather");
        AIAgent agent = CreateAgentWithTool(reportingTool);

        List<ChatProgressUpdate> progress = await RunPipelineAsync(agent.WithTracking());

        ChatProgressUpdate subheader = Assert.Single(progress, update => update.Message == "Extracting…");
        Assert.Equal(ChatProgressKind.ToolProgress, subheader.Kind);
        Assert.Equal("Weather_Agent", subheader.ToolName);
    }

    private static async Task<List<ChatProgressUpdate>> RunPipelineAsync(AIFunction wrapped)
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Weather_Agent", new Dictionary<string, object?> { ["query"] = "weather?" }),
            ScriptedTurn.Text("Done."));
        IChatClient client = TestPipeline.Build(scripted, options => options.UseAgentToolClassification());

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(
            client,
            new ChatOptions { Tools = [wrapped] });

        return TestPipeline.ProgressOf(updates);
    }

    private static AIAgent CreateAgentWithTool(AIFunction tool)
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("inner-1", tool.Name),
            ScriptedTurn.Text("It is sunny."));
        return scripted.AsAIAgent(
            instructions: "You answer questions about the weather.",
            name: "Weather Agent",
            tools: [tool]);
    }
}
