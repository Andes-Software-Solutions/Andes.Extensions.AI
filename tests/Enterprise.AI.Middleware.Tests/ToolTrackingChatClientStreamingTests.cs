using Enterprise.AI.Middleware.Tests.TestDoubles;
using Enterprise.AI.Middleware.Tracking;
using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tests;

public sealed class ToolTrackingChatClientStreamingTests : IClassFixture<InMemoryMcpFixture>
{
    private readonly InMemoryMcpFixture _mcp;

    public ToolTrackingChatClientStreamingTests(InMemoryMcpFixture mcp)
    {
        _mcp = mcp;
    }

    [Fact]
    public async Task GetStreamingResponseAsync_PlainToolInvoked_InjectsStartedStatusBeforeFinalText()
    {
        AIFunction tool = AIFunctionFactory.Create(() => "42", "search_document");
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.FunctionCallUpdate("call-1", "search_document"))
            .AddTurn(ScriptedChatClient.TextUpdate("The answer is 42."));
        IChatClient client = TestPipeline.Build(inner);

        List<ChatResponseUpdate> updates = await TestPipeline.DrainAsync(client.GetStreamingResponseAsync(
            TestPipeline.UserMessage("find it"),
            new ChatOptions { Tools = [tool] }));

        List<ActivityStatusContent> statuses = TestPipeline.StatusContents(updates);
        ActivityStatusContent started = statuses.First(s => s.Kind == ChatActivityEventKind.ToolCallStarted);
        Assert.Equal("Calling Tool(s)", started.Header);
        Assert.Equal("Called search_document", started.Subheader);
        Assert.Equal("call-1", started.ToolCallId);

        int startedIndex = updates.FindIndex(u => u.Contents.OfType<ActivityStatusContent>()
            .Any(s => s.Kind == ChatActivityEventKind.ToolCallStarted));
        int finalTextIndex = updates.FindIndex(u => u.Text.Contains("The answer is 42.", StringComparison.Ordinal));
        Assert.True(startedIndex >= 0 && finalTextIndex > startedIndex, "tool status must precede the final text");
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ToolBlocked_StartedStatusObservableBeforeToolCompletes()
    {
        var toolGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AIFunction tool = AIFunctionFactory.Create(
            async Task<string> () =>
            {
                await toolGate.Task;
                return "done";
            },
            "blocking_tool");
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.FunctionCallUpdate("call-1", "blocking_tool"))
            .AddTurn(ScriptedChatClient.TextUpdate("finished"));
        IChatClient client = TestPipeline.Build(inner);

        await using IAsyncEnumerator<ChatResponseUpdate> enumerator = client.GetStreamingResponseAsync(
            TestPipeline.UserMessage("go"),
            new ChatOptions { Tools = [tool] }).GetAsyncEnumerator();

        bool sawStartedWhileToolBlocked = false;
        while (await enumerator.MoveNextAsync())
        {
            if (enumerator.Current.Contents.OfType<ActivityStatusContent>()
                .Any(s => s.Kind == ChatActivityEventKind.ToolCallStarted))
            {
                sawStartedWhileToolBlocked = !toolGate.Task.IsCompleted;
                break;
            }
        }

        Assert.True(sawStartedWhileToolBlocked, "the started status must surface while the tool is still executing");

        toolGate.SetResult();
        while (await enumerator.MoveNextAsync())
        {
        }
    }

    [Fact]
    public async Task GetStreamingResponseAsync_InBandDisabled_InjectsNoActivityContent()
    {
        AIFunction tool = AIFunctionFactory.Create(() => "ok", "quiet_tool");
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.FunctionCallUpdate("call-1", "quiet_tool"))
            .AddTurn(ScriptedChatClient.TextUpdate("done"));
        var options = new ToolTrackingOptions
        {
            EnableInBandStatusUpdates = false,
            EnableFinalUsageUpdate = false,
        };
        IChatClient client = TestPipeline.Build(inner, options);

        List<ChatResponseUpdate> updates = await TestPipeline.DrainAsync(client.GetStreamingResponseAsync(
            TestPipeline.UserMessage("go"),
            new ChatOptions { Tools = [tool] }));

        Assert.Empty(TestPipeline.StatusContents(updates));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_NoTools_InnerUpdatesPassThroughInOrder()
    {
        ChatResponseUpdate first = ScriptedChatClient.TextUpdate("one");
        ChatResponseUpdate second = ScriptedChatClient.TextUpdate("two");
        ScriptedChatClient inner = new ScriptedChatClient().AddTurn(first, second);
        var options = new ToolTrackingOptions { EnableFinalUsageUpdate = false };
        IChatClient client = TestPipeline.Build(inner, options);

        List<ChatResponseUpdate> updates = await TestPipeline.DrainAsync(
            client.GetStreamingResponseAsync(TestPipeline.UserMessage("hi")));

        Assert.Equal(2, updates.Count);
        Assert.Same(first, updates[0]);
        Assert.Same(second, updates[1]);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_RequestCompletes_EmitsFinalUsageUpdateWithTotals()
    {
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(
                ScriptedChatClient.TextUpdate("hello"),
                ScriptedChatClient.UsageUpdate(11, 7, "scripted-model"));
        IChatClient client = TestPipeline.Build(inner);

        List<ChatResponseUpdate> updates = await TestPipeline.DrainAsync(
            client.GetStreamingResponseAsync(TestPipeline.UserMessage("hi")));

        ActivityStatusContent final = TestPipeline.StatusContents(updates)
            .Single(s => s.Kind == ChatActivityEventKind.RequestCompleted);
        Assert.NotNull(final.Usage);
        Assert.Equal(11, final.Usage!.InputTokenCount);
        Assert.Equal(7, final.Usage.OutputTokenCount);
        Assert.Equal(18, final.Usage.TotalTokenCount);
        Assert.Contains(final, updates[^1].Contents);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_FinalUsageDisabled_OmitsFinalUpdate()
    {
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.TextUpdate("hello"), ScriptedChatClient.UsageUpdate(1, 2));
        var options = new ToolTrackingOptions { EnableFinalUsageUpdate = false };
        IChatClient client = TestPipeline.Build(inner, options);

        List<ChatResponseUpdate> updates = await TestPipeline.DrainAsync(
            client.GetStreamingResponseAsync(TestPipeline.UserMessage("hi")));

        Assert.DoesNotContain(
            TestPipeline.StatusContents(updates),
            s => s.Kind == ChatActivityEventKind.RequestCompleted);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_McpAnnotatedTool_EmitsMcpHeaderWithServerName()
    {
        AIFunction tool = _mcp.EchoTool.WithTrackingMetadata(_mcp.Client);
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.FunctionCallUpdate(
                "call-1",
                "echo",
                new Dictionary<string, object?> { ["message"] = "hi" }))
            .AddTurn(ScriptedChatClient.TextUpdate("echoed"));
        var observer = new RecordingObserver();
        IChatClient client = TestPipeline.Build(inner, observer: observer);

        await TestPipeline.DrainAsync(client.GetStreamingResponseAsync(
            TestPipeline.UserMessage("echo hi"),
            new ChatOptions { Tools = [tool] }));

        ChatActivityEvent started = observer.EventsOfKind(ChatActivityEventKind.ToolCallStarted).Single();
        Assert.Equal(ToolKind.Mcp, started.ToolKind);
        Assert.Equal(InMemoryMcpFixture.ServerName, started.SourceName);
        Assert.Equal($"Calling {InMemoryMcpFixture.ServerName} MCP", started.Header);
        Assert.Equal("Called echo", started.Subheader);
        Assert.Single(observer.EventsOfKind(ChatActivityEventKind.ToolCallCompleted));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_UsageContentInStream_RecordedWithModelAndProvider()
    {
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(
                ScriptedChatClient.TextUpdate("hi"),
                ScriptedChatClient.UsageUpdate(5, 3, "scripted-model"));
        var observer = new RecordingObserver();
        IChatClient client = TestPipeline.Build(inner, observer: observer);

        await TestPipeline.DrainAsync(client.GetStreamingResponseAsync(TestPipeline.UserMessage("hi")));

        ChatActivityEvent usage = observer.EventsOfKind(ChatActivityEventKind.UsageReported).Single();
        Assert.Equal("scripted-model", usage.ModelId);
        Assert.Equal("test.provider", usage.ProviderName);
        Assert.Equal(5, usage.Usage!.InputTokenCount);
        Assert.Equal(3, usage.Usage.OutputTokenCount);
    }
}
