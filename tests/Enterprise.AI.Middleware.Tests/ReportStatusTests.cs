using Enterprise.AI.Middleware.Tests.TestDoubles;
using Enterprise.AI.Middleware.Tracking;
using Enterprise.AI.Middleware.Tracking.Internal;
using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tests;

public sealed class ReportStatusTests
{
    [Fact]
    public void ReportStatus_OutsideTrackedRequest_DoesNotThrow()
    {
        ChatActivityScope.ReportStatus("nobody is listening");
    }

    [Fact]
    public void ReportStatus_NullMessage_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ChatActivityScope.ReportStatus(null!));
    }

    [Fact]
    public async Task ReportStatus_InsideTool_PublishesStatusReportedInBandAndToObserver()
    {
        AIFunction tool = AIFunctionFactory.Create(
            () =>
            {
                ChatActivityScope.ReportStatus("halfway there");
                return "done";
            },
            "long_tool");
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.FunctionCallUpdate("call-1", "long_tool"))
            .AddTurn(ScriptedChatClient.TextUpdate("finished"));
        var observer = new RecordingObserver();
        IChatClient client = TestPipeline.Build(inner, observer: observer);

        List<ChatResponseUpdate> updates = await TestPipeline.DrainAsync(client.GetStreamingResponseAsync(
            TestPipeline.UserMessage("go"),
            new ChatOptions { Tools = [tool] }));

        ChatActivityEvent status = observer.EventsOfKind(ChatActivityEventKind.StatusReported).Single();
        Assert.Equal("Calling Tool(s)", status.Header);
        Assert.Equal("halfway there", status.Subheader);
        Assert.Equal("long_tool", status.ToolName);

        ChatActivityEvent started = observer.EventsOfKind(ChatActivityEventKind.ToolCallStarted).Single();
        Assert.Equal(started.ScopeId, status.ScopeId);
        Assert.Equal(started.ParentScopeId, status.ParentScopeId);

        ActivityStatusContent inBand = TestPipeline.StatusContents(updates)
            .Single(s => s.Kind == ChatActivityEventKind.StatusReported);
        Assert.Equal("halfway there", inBand.Subheader);
    }

    [Fact]
    public async Task ReportStatus_InsideAgentTool_HeaderUsesAgentTemplate()
    {
        AIFunction agentTool = new AnnotatedAIFunction(
            AIFunctionFactory.Create(
                () =>
                {
                    ChatActivityScope.ReportStatus("consulting sources");
                    return "agent done";
                },
                "researcher_agent"),
            ToolKind.Agent,
            "Researcher");
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.FunctionCallUpdate("call-1", "researcher_agent"))
            .AddTurn(ScriptedChatClient.TextUpdate("done"));
        var observer = new RecordingObserver();
        IChatClient client = TestPipeline.Build(inner, observer: observer);

        await client.GetResponseAsync(
            TestPipeline.UserMessage("go"),
            new ChatOptions { Tools = [agentTool] });

        ChatActivityEvent status = observer.EventsOfKind(ChatActivityEventKind.StatusReported).Single();
        Assert.Equal("Calling Researcher Agent", status.Header);
        Assert.Equal("consulting sources", status.Subheader);
    }

    [Fact]
    public async Task ReportStatus_WithProgressValues_CarriesProgressAndTotal()
    {
        AIFunction tool = AIFunctionFactory.Create(
            () =>
            {
                ChatActivityScope.ReportStatus("indexing", progress: 2, progressTotal: 5);
                return "ok";
            },
            "indexer");
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.FunctionCallUpdate("call-1", "indexer"))
            .AddTurn(ScriptedChatClient.TextUpdate("done"));
        var observer = new RecordingObserver();
        IChatClient client = TestPipeline.Build(inner, observer: observer);

        List<ChatResponseUpdate> updates = await TestPipeline.DrainAsync(client.GetStreamingResponseAsync(
            TestPipeline.UserMessage("go"),
            new ChatOptions { Tools = [tool] }));

        ChatActivityEvent status = observer.EventsOfKind(ChatActivityEventKind.StatusReported).Single();
        Assert.Equal(2, status.Progress);
        Assert.Equal(5, status.ProgressTotal);

        ActivityStatusContent inBand = TestPipeline.StatusContents(updates)
            .Single(s => s.Kind == ChatActivityEventKind.StatusReported);
        Assert.Equal(2, inBand.Progress);
        Assert.Equal(5, inBand.ProgressTotal);
    }

    [Fact]
    public async Task ReportStatus_FormatSubheaderDelegate_Overrides()
    {
        var options = new ToolTrackingOptions();
        options.Templates.FormatSubheader = e =>
            e.Kind == ChatActivityEventKind.StatusReported ? "localized status" : e.Subheader;
        AIFunction tool = AIFunctionFactory.Create(
            () =>
            {
                ChatActivityScope.ReportStatus("original message");
                return "ok";
            },
            "custom_tool");
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.FunctionCallUpdate("call-1", "custom_tool"))
            .AddTurn(ScriptedChatClient.TextUpdate("done"));
        var observer = new RecordingObserver();
        IChatClient client = TestPipeline.Build(inner, options, observer);

        await client.GetResponseAsync(
            TestPipeline.UserMessage("go"),
            new ChatOptions { Tools = [tool] });

        ChatActivityEvent status = observer.EventsOfKind(ChatActivityEventKind.StatusReported).Single();
        Assert.Equal("localized status", status.Subheader);
    }
}
