using System.ComponentModel;
using Andes.Extensions.AI.Integration.Test;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Agent.Integration.Test;

public sealed class AgentToolTrackingIntegrationTests(AzureOpenAIFixture fixture) : IClassFixture<AzureOpenAIFixture>
{
    private readonly AzureOpenAIFixture _fixture = fixture;

    [Description("Gets the weather for a given city.")]
    private static string GetWeather([Description("The city to get the weather for.")] string city)
    {
        return $"The weather in {city} is sunny with a high of 25C.";
    }

    private AIAgent CreateWeatherAgent()
    {
        return CreateRawClient().AsAIAgent(
            instructions: "You answer questions about the weather using the GetWeather tool.",
            name: "Weather Agent",
            description: "Answers questions about the weather in a given city.",
            tools: [AIFunctionFactory.Create(GetWeather)]);
    }

    private AIAgent CreatePackingAgent()
    {
        return CreateRawClient().AsAIAgent(
            instructions: "You suggest what to pack for a destination in one short sentence.",
            name: "Packing Agent",
            description: "Suggests what to pack for a destination.");
    }

    private AIAgent CreateResearchAgent()
    {
        return CreateRawClient().AsAIAgent(
            instructions: "You research travel topics. Always call the Packing Agent tool once and base your answer on its reply.",
            name: "Research Agent",
            description: "Researches what to pack for a destination.",
            tools: [CreatePackingAgent().WithTracking()]);
    }

    private IChatClient CreateRawClient()
    {
        // Inner agents run over raw (untracked) chat clients, so the only usage the outer
        // pipeline can attribute to an agent tool comes from WithTracking's usage capture.
        return new AzureOpenAIClient(
                new Uri(_fixture.Settings.Endpoint!),
                new AzureKeyCredential(_fixture.Settings.ApiKey!))
            .GetChatClient(_fixture.Settings.Deployment!)
            .AsIChatClient();
    }

    [SkippableFact]
    public async Task GetStreamingResponseAsync_AgentAsTool_EmitsAgentHeaderAndClassification()
    {
        Skip.IfNot(_fixture.IsConfigured, AzureOpenAIFixture.SkipReason);

        var observer = new RecordingObserver();
        IChatClient client = _fixture.CreatePipeline(options =>
        {
            options.UseAgentToolClassification();
            options.Observers.Add(observer);
        });

        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "Use the Weather Agent tool to get the weather in Quito, then confirm.")],
            new ChatOptions { Tools = [CreateWeatherAgent().WithTracking()] }))
        {
        }

        ChatProgressUpdate? invoking = observer.Updates.FirstOrDefault(update =>
            update.Kind == ChatProgressKind.ToolInvoking && update.ToolKind == ToolKind.Agent);
        Assert.NotNull(invoking);
        Assert.Equal("Calling Weather Agent", invoking.Message);
        Assert.Equal("Weather_Agent", invoking.ToolName);
        Assert.Equal("Weather Agent", invoking.ToolSource);

        Assert.NotNull(observer.Report);
        Assert.Contains(
            observer.Report.ToolCalls,
            call => call.Kind == ToolKind.Agent && call.ToolName == "Weather_Agent");
    }

    [SkippableFact]
    public async Task GetStreamingResponseAsync_AgentAsTool_AttributesAgentUsageToToolScope()
    {
        Skip.IfNot(_fixture.IsConfigured, AzureOpenAIFixture.SkipReason);

        var observer = new RecordingObserver();
        IChatClient client = _fixture.CreatePipeline(options =>
        {
            options.UseAgentToolClassification();
            options.Observers.Add(observer);
        });

        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "Use the Weather Agent tool to get the weather in Quito, then confirm.")],
            new ChatOptions { Tools = [CreateWeatherAgent().WithTracking()] }))
        {
        }

        Assert.NotNull(observer.Report);
        List<ToolCallUsage> agentCalls = observer.Report.ToolCalls
            .Where(call => call.Kind == ToolKind.Agent && call.ToolName == "Weather_Agent")
            .ToList();
        Assert.NotEmpty(agentCalls);
        Assert.All(agentCalls, call =>
        {
            Assert.NotNull(call.Usage);
            Assert.True(call.Usage.TotalTokenCount > 0, "Each agent run should attribute non-zero usage to its tool scope.");
        });
        Assert.True(
            observer.Report.TotalUsage.TotalTokenCount > observer.Report.AssistantUsage.TotalTokenCount,
            "The report total should exceed the assistant-only usage because the agent's usage is included.");
    }

    [SkippableFact]
    public async Task GetStreamingResponseAsync_AgentToolInsideAgent_EmitsChildScopeWithUsage()
    {
        Skip.IfNot(_fixture.IsConfigured, AzureOpenAIFixture.SkipReason);

        var observer = new RecordingObserver();
        IChatClient client = _fixture.CreatePipeline(options =>
        {
            options.UseAgentToolClassification();
            options.Observers.Add(observer);
        });

        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "Use the Research Agent tool to find out what to pack for Quito, then confirm.")],
            new ChatOptions { Tools = [CreateResearchAgent().WithTracking()] }))
        {
        }

        ChatProgressUpdate? childInvoking = observer.Updates.FirstOrDefault(update =>
            update.Kind == ChatProgressKind.ToolInvoking && update.ToolName == "Packing_Agent");
        Assert.NotNull(childInvoking);
        Assert.Equal(ToolKind.Agent, childInvoking.ToolKind);
        Assert.Equal("Packing Agent", childInvoking.ToolSource);
        Assert.NotNull(childInvoking.ParentScopeId);
        ChatProgressUpdate? parentInvoking = observer.Updates.FirstOrDefault(update =>
            update.Kind == ChatProgressKind.ToolInvoking && update.ScopeId == childInvoking.ParentScopeId);
        Assert.NotNull(parentInvoking);
        Assert.Equal("Research_Agent", parentInvoking.ToolName);

        Assert.NotNull(observer.Report);
        ToolCallUsage? researchCall = observer.Report.ToolCalls.FirstOrDefault(call =>
            call.ToolName == "Research_Agent" && call.Children.Any(child => child.ToolName == "Packing_Agent"));
        Assert.NotNull(researchCall);
        ToolCallUsage packingCall = researchCall.Children.First(child => child.ToolName == "Packing_Agent");
        Assert.Equal(ToolKind.Agent, packingCall.Kind);
        Assert.NotNull(packingCall.Usage);
        Assert.True(packingCall.Usage.TotalTokenCount > 0, "The nested agent's run should attribute non-zero usage to its child scope.");
        Assert.NotNull(researchCall.Usage);
        Assert.True(
            researchCall.Usage.TotalTokenCount > packingCall.Usage.TotalTokenCount,
            "The parent agent's rollup should include its own usage on top of the nested agent's.");
    }

    private sealed class RecordingObserver : IChatProgressObserver
    {
        private readonly Lock _lock = new();
        private readonly List<ChatProgressUpdate> _updates = [];
        private ChatUsageReport? _report;

        public IReadOnlyList<ChatProgressUpdate> Updates
        {
            get
            {
                lock (_lock)
                {
                    return [.. _updates];
                }
            }
        }

        public ChatUsageReport? Report
        {
            get
            {
                lock (_lock)
                {
                    return _report;
                }
            }
        }

        public void OnProgress(ChatProgressUpdate update)
        {
            lock (_lock)
            {
                _updates.Add(update);
            }
        }

        public void OnRequestCompleted(ChatUsageReport report)
        {
            lock (_lock)
            {
                _report = report;
            }
        }
    }
}
