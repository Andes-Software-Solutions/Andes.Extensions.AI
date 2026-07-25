using Andes.Extensions.AI.Unit.Test.Infrastructure;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Agent.Unit.Test;

public class AgentToolTrackingExtensionsTests
{
    [Fact]
    public void WithTracking_NullAgent_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((AIAgent)null!).WithTracking());
    }

    [Fact]
    public void WithTracking_PreservesNameAndDescription()
    {
        AIAgent agent = CreateWeatherAgent(ScriptedTurn.Text("Sunny"));

        AIFunction wrapped = agent.WithTracking();

        Assert.Equal(agent.AsAIFunction().Name, wrapped.Name);
        Assert.Equal("Weather_Agent", wrapped.Name);
        Assert.Equal("Answers weather questions.", wrapped.Description);
    }

    [Fact]
    public async Task WithTracking_Invocation_RunsAgentAndReturnsText()
    {
        AIAgent agent = CreateWeatherAgent(ScriptedTurn.Text("Sunny"));
        AIFunction wrapped = agent.WithTracking();

        object? result = await wrapped.InvokeAsync(new AIFunctionArguments { ["query"] = "weather?" });

        Assert.Contains("Sunny", result?.ToString());
    }

    [Fact]
    public void WithTracking_GetService_ExposesOriginalAgent()
    {
        AIAgent agent = CreateWeatherAgent(ScriptedTurn.Text("Sunny"));

        AIFunction wrapped = agent.WithTracking();

        Assert.Same(agent, wrapped.GetService(typeof(AIAgent)));
        Assert.Same(agent, wrapped.GetService(typeof(ChatClientAgent)));
        Assert.Same(wrapped, wrapped.GetService(typeof(object)));
    }

    [Fact]
    public async Task WithTracking_OutsideTrackedPipeline_UsageReportIsNoOp()
    {
        AIAgent agent = CreateWeatherAgent(ScriptedTurn.Text(
            "Sunny",
            new UsageDetails { InputTokenCount = 60, OutputTokenCount = 40, TotalTokenCount = 100 }));
        AIFunction wrapped = agent.WithTracking();

        object? result = await wrapped.InvokeAsync(new AIFunctionArguments { ["query"] = "weather?" });

        Assert.Contains("Sunny", result?.ToString());
    }

    private static AIAgent CreateWeatherAgent(params ScriptedTurn[] turns)
    {
        return new ScriptedChatClient(turns).AsAIAgent(
            instructions: "You answer questions about the weather.",
            name: "Weather Agent",
            description: "Answers weather questions.");
    }
}
