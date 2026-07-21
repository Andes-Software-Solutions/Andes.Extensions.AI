using Enterprise.AI.Middleware.Tracking;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.IntegrationTests;

public sealed class AgentToolTrackingIntegrationTests : IClassFixture<AzureOpenAIFixture>
{
    private readonly AzureOpenAIFixture _azure;

    public AgentToolTrackingIntegrationTests(AzureOpenAIFixture azure)
    {
        _azure = azure;
    }

    [SkippableFact]
    public async Task GetStreamingResponseAsync_MafAgentAsTrackedTool_NestedUsageAttributedToAgentScope()
    {
        _azure.SkipUnlessConfigured();

        IChatClient agentClient = new ChatClientBuilder(_azure.CreateProviderClient())
            .UseToolTracking(new ToolTrackingOptions())
            .UseFunctionInvocation()
            .Build();
        var researcher = new ChatClientAgent(
            agentClient,
            instructions: "You are a research specialist. Answer concisely in one sentence.",
            name: "Researcher");

        var observer = new CollectingObserver();
        IChatClient mainClient = new ChatClientBuilder(_azure.CreateProviderClient())
            .UseToolTracking(new ToolTrackingOptions(), observer)
            .UseFunctionInvocation()
            .Build();
        var options = new ChatOptions
        {
            Tools = [researcher.AsTrackedAIFunction()],
            Temperature = 0,
        };

        List<ChatResponseUpdate> updates = [];
        await foreach (ChatResponseUpdate update in mainClient.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "Delegate to the Researcher agent: what is the capital of France?")],
            options))
        {
            updates.Add(update);
        }

        ChatActivityEvent started = observer.Events.First(e => e.Kind == ChatActivityEventKind.ToolCallStarted);
        Assert.Equal(ToolKind.Agent, started.ToolKind);
        Assert.Equal("Calling Researcher Agent", started.Header);

        ChatActivityReport report = Assert.Single(observer.Reports);
        ActivityScopeReport agentScope = report.Root.Children.Single(scope => scope.ToolKind == ToolKind.Agent);
        Assert.Equal("Researcher", agentScope.SourceName);
        Assert.True(
            agentScope.TotalUsage.TotalTokenCount > 0,
            "the agent's nested model usage must be attributed to the agent tool scope");
        Assert.True(
            report.TotalUsage.TotalTokenCount > agentScope.TotalUsage.TotalTokenCount,
            "the root request must also have its own usage on top of the agent's");
    }
}
