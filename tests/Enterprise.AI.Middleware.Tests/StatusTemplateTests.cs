using Enterprise.AI.Middleware.Tests.TestDoubles;
using Enterprise.AI.Middleware.Tracking;
using Enterprise.AI.Middleware.Tracking.Internal;
using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tests;

public sealed class StatusTemplateTests
{
    private static async Task<ChatActivityEvent> RunSingleToolAsync(
        AIFunction tool,
        string toolName,
        ToolTrackingOptions options)
    {
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.FunctionCallUpdate("call-1", toolName))
            .AddTurn(ScriptedChatClient.TextUpdate("done"));
        var observer = new RecordingObserver();
        IChatClient client = TestPipeline.Build(inner, options, observer);

        await client.GetResponseAsync(
            TestPipeline.UserMessage("go"),
            new ChatOptions { Tools = [tool] });

        return observer.EventsOfKind(ChatActivityEventKind.ToolCallStarted).Single();
    }

    [Fact]
    public async Task Defaults_PlainTool_UsesCallingToolsHeaderAndCalledSubheader()
    {
        AIFunction tool = AIFunctionFactory.Create(() => "ok", "search_document");

        ChatActivityEvent started = await RunSingleToolAsync(tool, "search_document", new ToolTrackingOptions());

        Assert.Equal("Calling Tool(s)", started.Header);
        Assert.Equal("Called search_document", started.Subheader);
    }

    [Fact]
    public async Task Defaults_AgentTool_UsesCallingAgentHeaderWithoutSubheader()
    {
        AIFunction tool = new AnnotatedAIFunction(
            AIFunctionFactory.Create(() => "ok", "researcher"),
            ToolKind.Agent,
            "Researcher");

        ChatActivityEvent started = await RunSingleToolAsync(tool, "researcher", new ToolTrackingOptions());

        Assert.Equal("Calling Researcher Agent", started.Header);
        Assert.Null(started.Subheader);
    }

    [Fact]
    public async Task Defaults_McpTool_UsesCallingMcpHeaderWithServerName()
    {
        AIFunction tool = new AnnotatedAIFunction(
            AIFunctionFactory.Create(() => "ok", "echo"),
            ToolKind.Mcp,
            "GitHub");

        ChatActivityEvent started = await RunSingleToolAsync(tool, "echo", new ToolTrackingOptions());

        Assert.Equal("Calling GitHub MCP", started.Header);
        Assert.Equal("Called echo", started.Subheader);
    }

    [Fact]
    public async Task CustomTemplates_ReplaceDefaultText()
    {
        var options = new ToolTrackingOptions();
        options.Templates.PlainToolHeader = "Working...";
        options.Templates.PlainToolSubheader = "Running {0}";
        AIFunction tool = AIFunctionFactory.Create(() => "ok", "my_tool");

        ChatActivityEvent started = await RunSingleToolAsync(tool, "my_tool", options);

        Assert.Equal("Working...", started.Header);
        Assert.Equal("Running my_tool", started.Subheader);
    }

    [Fact]
    public async Task FormatHeader_CustomDelegate_OverridesTemplates()
    {
        var options = new ToolTrackingOptions();
        options.Templates.FormatHeader = e => $"[{e.ToolKind}] {e.ToolName}";
        AIFunction tool = AIFunctionFactory.Create(() => "ok", "my_tool");

        ChatActivityEvent started = await RunSingleToolAsync(tool, "my_tool", options);

        Assert.Equal("[Function] my_tool", started.Header);
    }

    [Fact]
    public async Task FormatSubheader_ReturnsNull_SuppressesSubheader()
    {
        var options = new ToolTrackingOptions();
        options.Templates.FormatSubheader = _ => null;
        AIFunction tool = AIFunctionFactory.Create(() => "ok", "my_tool");

        ChatActivityEvent started = await RunSingleToolAsync(tool, "my_tool", options);

        Assert.Null(started.Subheader);
    }
}
