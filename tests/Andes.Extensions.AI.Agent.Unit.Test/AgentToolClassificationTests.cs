using Andes.Extensions.AI.Unit.Test.Infrastructure;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Agent.Unit.Test;

public class AgentToolClassificationTests
{
    [Fact]
    public void UseAgentToolClassification_WrappedAgent_AgentKindWithAgentNameDisplay()
    {
        ToolTrackingOptions options = new ToolTrackingOptions().UseAgentToolClassification();
        AIFunction wrapped = CreateWeatherAgent().WithTracking();

        ToolDescriptor descriptor = options.ToolClassifier!(wrapped);

        Assert.Equal(ToolKind.Agent, descriptor.Kind);
        Assert.Equal("Weather_Agent", descriptor.Name);
        Assert.Equal("Weather Agent", descriptor.DisplayName);
        Assert.Equal("Weather Agent", descriptor.Source);
    }

    [Fact]
    public void UseAgentToolClassification_DisplayNameResolver_PreferredOverAgentName()
    {
        ToolTrackingOptions options = new ToolTrackingOptions()
            .UseAgentToolClassification(displayNameResolver: _ => "Resolved Agent");
        AIFunction wrapped = CreateWeatherAgent().WithTracking();

        ToolDescriptor descriptor = options.ToolClassifier!(wrapped);

        Assert.Equal("Resolved Agent", descriptor.DisplayName);
        Assert.Equal("Weather Agent", descriptor.Source);
    }

    [Fact]
    public void UseAgentToolClassification_WhitespaceResolverResult_FallsBackToAgentName()
    {
        ToolTrackingOptions options = new ToolTrackingOptions()
            .UseAgentToolClassification(displayNameResolver: _ => "  ");
        AIFunction wrapped = CreateWeatherAgent().WithTracking();

        ToolDescriptor descriptor = options.ToolClassifier!(wrapped);

        Assert.Equal("Weather Agent", descriptor.DisplayName);
    }

    [Fact]
    public void UseAgentToolClassification_UnnamedAgent_SourceFallsBackToAgentId()
    {
        ToolTrackingOptions options = new ToolTrackingOptions().UseAgentToolClassification();
        AIAgent agent = new ScriptedChatClient(ScriptedTurn.Text("Sunny"))
            .AsAIAgent(instructions: "You answer questions about the weather.");
        AIFunction wrapped = agent.WithTracking();

        ToolDescriptor descriptor = options.ToolClassifier!(wrapped);

        Assert.Equal(ToolKind.Agent, descriptor.Kind);
        Assert.Equal(agent.Id, descriptor.Source);
        Assert.Equal(wrapped.Name, descriptor.DisplayName);
    }

    [Fact]
    public void UseAgentToolClassification_UserWrappedTrackedAgent_StillClassifiesAsAgent()
    {
        ToolTrackingOptions options = new ToolTrackingOptions().UseAgentToolClassification();
        AIFunction wrapped = new PassthroughFunction(CreateWeatherAgent().WithTracking());

        ToolDescriptor descriptor = options.ToolClassifier!(wrapped);

        Assert.Equal(ToolKind.Agent, descriptor.Kind);
        Assert.Equal("Weather Agent", descriptor.DisplayName);
        Assert.Equal("Weather Agent", descriptor.Source);
    }

    [Fact]
    public void UseAgentToolClassification_PlainAsAIFunction_ClassifiesAsFunction()
    {
        // Pins the documented limitation: the framework's AsAIFunction() does not expose its
        // agent via GetService. If a future release does, this fails and docs need updating.
        ToolTrackingOptions options = new ToolTrackingOptions().UseAgentToolClassification();
        AIFunction plain = CreateWeatherAgent().AsAIFunction();

        ToolDescriptor descriptor = options.ToolClassifier!(plain);

        Assert.Equal(ToolKind.Function, descriptor.Kind);
        Assert.Null(descriptor.Source);
    }

    [Fact]
    public void UseAgentToolClassification_PlainFunction_FallsBackToDefaultClassifier()
    {
        ToolTrackingOptions options = new ToolTrackingOptions().UseAgentToolClassification();
        AIFunction plain = AIFunctionFactory.Create(() => "sunny", "GetWeather");

        ToolDescriptor descriptor = options.ToolClassifier!(plain);

        Assert.Equal(ToolKind.Function, descriptor.Kind);
        Assert.Null(descriptor.Source);
    }

    [Fact]
    public void UseAgentToolClassification_PreexistingClassifier_NonAgentDelegatesToIt()
    {
        var options = new ToolTrackingOptions
        {
            ToolClassifier = tool => new ToolDescriptor
            {
                Name = tool.Name,
                Kind = ToolKind.McpTool,
            },
        };
        options.UseAgentToolClassification();
        AIFunction plain = AIFunctionFactory.Create(() => "sunny", "GetWeather");
        AIFunction wrapped = CreateWeatherAgent().WithTracking();

        Assert.Equal(ToolKind.McpTool, options.ToolClassifier!(plain).Kind);
        Assert.Equal(ToolKind.Agent, options.ToolClassifier!(wrapped).Kind);
    }

    [Fact]
    public async Task AgentHeader_EndToEnd_EmitsCallingAgentAgent()
    {
        AIFunction wrapped = CreateWeatherAgent().WithTracking();
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Weather_Agent", new Dictionary<string, object?> { ["query"] = "weather?" }),
            ScriptedTurn.Text("Done."));
        IChatClient client = TestPipeline.Build(scripted, options => options.UseAgentToolClassification());

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(
            client,
            new ChatOptions { Tools = [wrapped] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        ChatProgressUpdate invoking = Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolInvoking);
        Assert.Equal("Calling Weather Agent", invoking.Message);
        Assert.Equal(ToolKind.Agent, invoking.ToolKind);
        Assert.Equal("Weather Agent", invoking.ToolSource);

        ChatUsageReport report = TestPipeline.ReportOf(updates);
        ToolCallUsage call = Assert.Single(report.ToolCalls);
        Assert.Equal(ToolKind.Agent, call.Kind);
        Assert.Equal("Weather_Agent", call.ToolName);
        Assert.Equal("Weather Agent", call.Source);
    }

    private static AIAgent CreateWeatherAgent()
    {
        return new ScriptedChatClient(ScriptedTurn.Text("Sunny")).AsAIAgent(
            instructions: "You answer questions about the weather.",
            name: "Weather Agent",
            description: "Answers weather questions.");
    }

    private sealed class PassthroughFunction(AIFunction inner) : DelegatingAIFunction(inner);
}
