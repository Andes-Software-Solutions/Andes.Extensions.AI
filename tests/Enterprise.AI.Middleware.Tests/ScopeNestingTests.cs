using Enterprise.AI.Middleware.Tests.TestDoubles;
using Enterprise.AI.Middleware.Tracking;
using Enterprise.AI.Middleware.Tracking.Internal;
using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tests;

public sealed class ScopeNestingTests
{
    [Fact]
    public void Current_OutsideRequest_IsNull()
    {
        Assert.Null(ChatActivityScope.Current);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_DuringToolInvocation_CurrentIsToolScope()
    {
        ActivityScope? observedScope = null;
        AIFunction tool = AIFunctionFactory.Create(
            () =>
            {
                observedScope = ChatActivityScope.Current;
                return "ok";
            },
            "scope_probe");
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.FunctionCallUpdate("call-1", "scope_probe"))
            .AddTurn(ScriptedChatClient.TextUpdate("done"));
        IChatClient client = TestPipeline.Build(inner);

        await TestPipeline.DrainAsync(client.GetStreamingResponseAsync(
            TestPipeline.UserMessage("go"),
            new ChatOptions { Tools = [tool] }));

        Assert.NotNull(observedScope);
        Assert.Equal(ToolKind.Function, observedScope!.ToolKind);
        Assert.Equal("scope_probe", observedScope.Name);
        Assert.Equal(1, observedScope.Depth);
        Assert.Null(ChatActivityScope.Current);
    }

    [Fact]
    public async Task NestedTrackedClient_InsideAgentTool_AttributesUsageToAgentToolScope()
    {
        ScriptedChatClient nestedInner = new ScriptedChatClient()
            .AddTurn(
                ScriptedChatClient.TextUpdate("nested reply"),
                ScriptedChatClient.UsageUpdate(100, 50, "nested-model"));
        IChatClient nestedClient = TestPipeline.Build(nestedInner);

        List<ChatResponseUpdate> nestedUpdates = [];
        AIFunction agentTool = new AnnotatedAIFunction(
            AIFunctionFactory.Create(
                async Task<string> () =>
                {
                    nestedUpdates = await TestPipeline.DrainAsync(
                        nestedClient.GetStreamingResponseAsync(TestPipeline.UserMessage("nested question")));
                    return "agent done";
                },
                "researcher_agent"),
            ToolKind.Agent,
            "Researcher");

        ScriptedChatClient outerInner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.FunctionCallUpdate("call-1", "researcher_agent"))
            .AddTurn(
                ScriptedChatClient.TextUpdate("outer done"),
                ScriptedChatClient.UsageUpdate(10, 5, "outer-model"));
        var observer = new RecordingObserver();
        IChatClient outerClient = TestPipeline.Build(outerInner, observer: observer);

        ChatResponse response = await outerClient.GetResponseAsync(
            TestPipeline.UserMessage("ask the researcher"),
            new ChatOptions { Tools = [agentTool] });

        ChatActivityReport report = ChatActivityReport.FromResponse(response)!;
        ActivityScopeReport agentScope = Assert.Single(report.Root.Children);
        Assert.Equal(ToolKind.Agent, agentScope.ToolKind);
        Assert.Equal("Researcher", agentScope.SourceName);

        ActivityScopeReport modelScope = Assert.Single(agentScope.Children);
        Assert.Equal(150, modelScope.OwnUsage.TotalTokenCount);
        Assert.Equal(150, agentScope.TotalUsage.TotalTokenCount);
        Assert.Equal(0, agentScope.OwnUsage.TotalTokenCount ?? 0);
        Assert.Equal(165, report.TotalUsage.TotalTokenCount);

        Assert.Empty(TestPipeline.StatusContents(nestedUpdates));

        Assert.Single(observer.Reports);
        ChatActivityEvent nestedUsage = observer.EventsOfKind(ChatActivityEventKind.UsageReported)
            .Single(e => e.ModelId == "nested-model");
        Assert.Equal(modelScope.ScopeId, nestedUsage.ScopeId);
    }

    [Fact]
    public async Task NestedToolUnderAgent_HeaderAndSubheaderUseAgentTemplates()
    {
        AIFunction nestedTool = AIFunctionFactory.Create(() => "looked up", "lookup_database");
        ScriptedChatClient nestedInner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.FunctionCallUpdate("nested-call", "lookup_database"))
            .AddTurn(ScriptedChatClient.TextUpdate("nested done"));
        IChatClient nestedClient = TestPipeline.Build(nestedInner);

        AIFunction agentTool = new AnnotatedAIFunction(
            AIFunctionFactory.Create(
                async Task<string> () =>
                {
                    await TestPipeline.DrainAsync(nestedClient.GetStreamingResponseAsync(
                        TestPipeline.UserMessage("nested"),
                        new ChatOptions { Tools = [nestedTool] }));
                    return "agent done";
                },
                "researcher_agent"),
            ToolKind.Agent,
            "Researcher");

        ScriptedChatClient outerInner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.FunctionCallUpdate("call-1", "researcher_agent"))
            .AddTurn(ScriptedChatClient.TextUpdate("outer done"));
        var observer = new RecordingObserver();
        IChatClient outerClient = TestPipeline.Build(outerInner, observer: observer);

        await outerClient.GetResponseAsync(
            TestPipeline.UserMessage("go"),
            new ChatOptions { Tools = [agentTool] });

        IReadOnlyList<ChatActivityEvent> started = observer.EventsOfKind(ChatActivityEventKind.ToolCallStarted);
        Assert.Equal(2, started.Count);

        ChatActivityEvent agentStarted = started[0];
        Assert.Equal("Calling Researcher Agent", agentStarted.Header);
        Assert.Null(agentStarted.Subheader);

        ChatActivityEvent nestedStarted = started[1];
        Assert.Equal("Calling Researcher Agent", nestedStarted.Header);
        Assert.Equal("Calling lookup_database", nestedStarted.Subheader);
    }
}
