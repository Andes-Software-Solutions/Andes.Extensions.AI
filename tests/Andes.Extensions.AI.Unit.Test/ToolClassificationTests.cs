using Andes.Extensions.AI.Unit.Test.Infrastructure;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Unit.Test;

public class ToolClassificationTests
{
    [Fact]
    public async Task DefaultClassifier_PlainFunction_ProducesFunctionKindAndHeader()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "GetWeather"),
            ScriptedTurn.Text("Done."));
        AIFunction tool = AIFunctionFactory.Create(() => "sunny", "GetWeather");
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        ChatProgressUpdate invoking = Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolInvoking);
        Assert.Equal("Calling GetWeather Tool", invoking.Message);
        Assert.Equal(ToolKind.Function, invoking.ToolKind);

        ChatUsageReport report = TestPipeline.ReportOf(updates);
        Assert.Equal(ToolKind.Function, Assert.Single(report.ToolCalls).Kind);
    }

    [Fact]
    public async Task CustomClassifier_McpKind_ProducesMcpHeaderAndSource()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "get_forecast"),
            ScriptedTurn.Text("Done."));
        AIFunction tool = AIFunctionFactory.Create(() => "sunny", "get_forecast");
        IChatClient client = TestPipeline.Build(scripted, options =>
            options.ToolClassifier = candidate => new ToolDescriptor
            {
                Name = candidate.Name,
                Kind = ToolKind.McpTool,
                Source = "weather-server",
            });

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        ChatProgressUpdate invoking = Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolInvoking);
        Assert.Equal("Calling weather-server MCP", invoking.Message);
        Assert.Equal(ToolKind.McpTool, invoking.ToolKind);
        Assert.Equal("weather-server", invoking.ToolSource);

        ChatUsageReport report = TestPipeline.ReportOf(updates);
        ToolCallUsage call = Assert.Single(report.ToolCalls);
        Assert.Equal(ToolKind.McpTool, call.Kind);
        Assert.Equal("weather-server", call.Source);
    }

    [Fact]
    public async Task CustomClassifier_RenamesDescriptor_EmitsNoPhantomDuplicateHeader()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "get_forecast"),
            ScriptedTurn.Text("Done."));
        AIFunction tool = AIFunctionFactory.Create(() => "sunny", "get_forecast");
        IChatClient client = TestPipeline.Build(scripted, options =>
            options.ToolClassifier = candidate => new ToolDescriptor
            {
                Name = "Weather Service",
                Kind = ToolKind.Function,
            });

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        ChatProgressUpdate invoking = Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolInvoking);
        Assert.Equal("Calling Weather Service Tool", invoking.Message);
    }

    [Fact]
    public void CreateDefault_AIFunction_ReturnsFunctionKind()
    {
        AIFunction tool = AIFunctionFactory.Create(() => "sunny", "GetWeather");

        ToolDescriptor descriptor = ToolDescriptor.CreateDefault(tool);

        Assert.Equal("GetWeather", descriptor.Name);
        Assert.Equal(ToolKind.Function, descriptor.Kind);
        Assert.Null(descriptor.Source);
    }

    [Fact]
    public void CreateDefault_NonFunctionTool_ReturnsUnknownKind()
    {
        var tool = new HostedWebSearchTool();

        var descriptor = ToolDescriptor.CreateDefault(tool);

        Assert.Equal(tool.Name, descriptor.Name);
        Assert.Equal(ToolKind.Unknown, descriptor.Kind);
    }

    [Fact]
    public async Task CustomHeaderFormatter_Provided_OverridesDefaultHeader()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "GetWeather"),
            ScriptedTurn.Text("Done."));
        AIFunction tool = AIFunctionFactory.Create(() => "sunny", "GetWeather");
        IChatClient client = TestPipeline.Build(scripted, options =>
            options.HeaderFormatter = descriptor => $">> {descriptor.DisplayName}");

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        ChatProgressUpdate invoking = Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolInvoking);
        Assert.Equal(">> GetWeather", invoking.Message);
    }
}
