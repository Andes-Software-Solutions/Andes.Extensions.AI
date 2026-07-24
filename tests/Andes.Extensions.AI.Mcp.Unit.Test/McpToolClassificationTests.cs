using Andes.Extensions.AI.Mcp.Unit.Test.Infrastructure;
using Andes.Extensions.AI.Unit.Test.Infrastructure;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Mcp.Unit.Test;

public class McpToolClassificationTests(InMemoryMcpFixture fixture) : IClassFixture<InMemoryMcpFixture>
{
    private readonly InMemoryMcpFixture _fixture = fixture;

    [Fact]
    public void UseMcpToolClassification_RawMcpClientTool_McpKindWithDefaultServerName()
    {
        ToolTrackingOptions options = new ToolTrackingOptions().UseMcpToolClassification();

        ToolDescriptor descriptor = options.ToolClassifier!(_fixture.EchoTool);

        Assert.Equal(ToolKind.McpTool, descriptor.Kind);
        Assert.Equal("MCP", descriptor.Source);
    }

    [Fact]
    public void UseMcpToolClassification_CustomDefaultServerName_Used()
    {
        ToolTrackingOptions options = new ToolTrackingOptions().UseMcpToolClassification("My Hub");

        ToolDescriptor descriptor = options.ToolClassifier!(_fixture.EchoTool);

        Assert.Equal("My Hub", descriptor.Source);
    }

    [Fact]
    public void UseMcpToolClassification_ServerNameResolver_PreferredOverDefault()
    {
        ToolTrackingOptions options = new ToolTrackingOptions()
            .UseMcpToolClassification(serverNameResolver: _ => "Resolved Server");

        ToolDescriptor descriptor = options.ToolClassifier!(_fixture.EchoTool);

        Assert.Equal("Resolved Server", descriptor.Source);
    }

    [Fact]
    public void UseMcpToolClassification_TrackedTool_AnnotationWinsOverResolver()
    {
        ToolTrackingOptions options = new ToolTrackingOptions()
            .UseMcpToolClassification(serverNameResolver: _ => "Resolved Server");
        AIFunction wrapped = _fixture.EchoTool.WithTracking(_fixture.Client);

        ToolDescriptor descriptor = options.ToolClassifier!(wrapped);

        Assert.Equal(InMemoryMcpFixture.ServerName, descriptor.Source);
    }

    [Fact]
    public void UseMcpToolClassification_PlainFunction_FallsBackToDefaultClassifier()
    {
        ToolTrackingOptions options = new ToolTrackingOptions().UseMcpToolClassification();
        AIFunction plain = AIFunctionFactory.Create(() => "sunny", "GetWeather");

        ToolDescriptor descriptor = options.ToolClassifier!(plain);

        Assert.Equal(ToolKind.Function, descriptor.Kind);
        Assert.Null(descriptor.Source);
    }

    [Fact]
    public void UseMcpToolClassification_PreexistingClassifier_NonMcpDelegatesToIt()
    {
        var options = new ToolTrackingOptions
        {
            ToolClassifier = tool => new ToolDescriptor
            {
                Name = tool.Name,
                Kind = ToolKind.Agent,
            },
        };
        options.UseMcpToolClassification();
        AIFunction plain = AIFunctionFactory.Create(() => "sunny", "GetWeather");

        Assert.Equal(ToolKind.Agent, options.ToolClassifier!(plain).Kind);
        Assert.Equal(ToolKind.McpTool, options.ToolClassifier!(_fixture.EchoTool).Kind);
    }

    [Fact]
    public async Task McpHeader_EndToEnd_EmitsCallingServerMcp()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "echo", new Dictionary<string, object?> { ["message"] = "hi" }),
            ScriptedTurn.Text("Done."));
        IChatClient client = TestPipeline.Build(scripted, options => options.UseMcpToolClassification());

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(
            client,
            new ChatOptions { Tools = [_fixture.EchoTool.WithTracking(_fixture.Client)] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        ChatProgressUpdate invoking = Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolInvoking);
        Assert.Equal($"Calling {InMemoryMcpFixture.ServerName} MCP", invoking.Message);
        Assert.Equal(ToolKind.McpTool, invoking.ToolKind);
        Assert.Equal(InMemoryMcpFixture.ServerName, invoking.ToolSource);

        ChatUsageReport report = TestPipeline.ReportOf(updates);
        ToolCallUsage call = Assert.Single(report.ToolCalls);
        Assert.Equal(ToolKind.McpTool, call.Kind);
        Assert.Equal("echo", call.ToolName);
        Assert.Equal(InMemoryMcpFixture.ServerName, call.Source);
    }
}
