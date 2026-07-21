using Enterprise.AI.Middleware.Tests.TestDoubles;
using Enterprise.AI.Middleware.Tracking;
using Enterprise.AI.Middleware.Tracking.Internal;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tests;

public sealed class AgentToolTrackingExtensionsTests
{
    [Fact]
    public void AsTrackedAIFunction_NullAgent_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => AgentToolTrackingExtensions.AsTrackedAIFunction(null!));
    }

    [Fact]
    public void AsTrackedAIFunction_Agent_AnnotatesKindAndAgentName()
    {
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.TextUpdate("agent reply"));
        var agent = new ChatClientAgent(inner, instructions: "Answer questions.", name: "Researcher");

        AIFunction function = agent.AsTrackedAIFunction();

        ToolClassification classification = ToolClassifier.Classify(function, new ToolTrackingOptions());
        Assert.Equal(ToolKind.Agent, classification.Kind);
        Assert.Equal("Researcher", classification.SourceName);
    }

    [Fact]
    public async Task AsTrackedAIFunction_Invoked_RunsUnderlyingAgent()
    {
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.TextUpdate("agent reply"));
        var agent = new ChatClientAgent(inner, instructions: "Answer questions.", name: "Researcher");
        AIFunction function = agent.AsTrackedAIFunction();

        object? result = await function.InvokeAsync(new AIFunctionArguments
        {
            ["query"] = "What is the answer?",
        });

        Assert.Contains("agent reply", result?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MafAgentAsTrackedTool_ThroughPipeline_TracksAgentScopeWithNestedUsage()
    {
        ScriptedChatClient agentInner = new ScriptedChatClient()
            .AddTurn(
                ScriptedChatClient.TextUpdate("researched answer"),
                ScriptedChatClient.UsageUpdate(200, 80, "agent-model"));
        IChatClient agentClient = TestPipeline.Build(agentInner);
        var agent = new ChatClientAgent(agentClient, instructions: "Research things.", name: "Researcher");

        AIFunction agentTool = agent.AsTrackedAIFunction();
        ScriptedChatClient mainInner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.FunctionCallUpdate(
                "call-1",
                agentTool.Name,
                new Dictionary<string, object?> { ["query"] = "look this up" }))
            .AddTurn(
                ScriptedChatClient.TextUpdate("final answer"),
                ScriptedChatClient.UsageUpdate(15, 6, "main-model"));
        var observer = new RecordingObserver();
        IChatClient mainClient = TestPipeline.Build(mainInner, observer: observer);

        ChatResponse response = await mainClient.GetResponseAsync(
            TestPipeline.UserMessage("delegate to the researcher"),
            new ChatOptions { Tools = [agentTool] });

        ChatActivityReport report = ChatActivityReport.FromResponse(response)!;
        ActivityScopeReport agentScope = Assert.Single(report.Root.Children);
        Assert.Equal(ToolKind.Agent, agentScope.ToolKind);
        Assert.Equal("Researcher", agentScope.SourceName);
        Assert.Equal(280, agentScope.TotalUsage.TotalTokenCount);
        Assert.Equal(301, report.TotalUsage.TotalTokenCount);

        ChatActivityEvent started = observer.EventsOfKind(ChatActivityEventKind.ToolCallStarted).Single();
        Assert.Equal("Calling Researcher Agent", started.Header);
    }
}
