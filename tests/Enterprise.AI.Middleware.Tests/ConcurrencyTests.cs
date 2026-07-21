using Enterprise.AI.Middleware.Tests.TestDoubles;
using Enterprise.AI.Middleware.Tracking;
using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tests;

public sealed class ConcurrencyTests
{
    [Fact]
    public async Task GetStreamingResponseAsync_TwoConcurrentRequests_ReportsDoNotCrossContaminate()
    {
        var observer = new RecordingObserver();

        async Task<string> RunRequestAsync(string toolName)
        {
            AIFunction tool = AIFunctionFactory.Create(() => "ok", toolName);
            ScriptedChatClient inner = new ScriptedChatClient()
                .AddTurn(ScriptedChatClient.FunctionCallUpdate("call-1", toolName))
                .AddTurn(ScriptedChatClient.TextUpdate("done"), ScriptedChatClient.UsageUpdate(10, 5));
            IChatClient client = TestPipeline.Build(inner, observer: observer);

            List<ChatResponseUpdate> updates = await TestPipeline.DrainAsync(client.GetStreamingResponseAsync(
                TestPipeline.UserMessage("go"),
                new ChatOptions { Tools = [tool] }));
            return TestPipeline.StatusContents(updates)
                .First(s => s.Kind == ChatActivityEventKind.ToolCallStarted).ScopeId;
        }

        string[] toolScopeIds = await Task.WhenAll(RunRequestAsync("tool_alpha"), RunRequestAsync("tool_beta"));

        Assert.Equal(2, observer.Reports.Count);
        Assert.NotEqual(observer.Reports[0].RequestId, observer.Reports[1].RequestId);
        Assert.NotEqual(toolScopeIds[0], toolScopeIds[1]);

        foreach (ChatActivityReport report in observer.Reports)
        {
            ActivityScopeReport toolScope = Assert.Single(report.Root.Children);
            Assert.Equal(report.Root.ScopeId, toolScope.ParentScopeId);
            Assert.Equal(15, report.TotalUsage.TotalTokenCount);

            IReadOnlyList<ChatActivityEvent> requestEvents =
                [.. observer.Events.Where(e => e.RequestId == report.RequestId)];
            Assert.Contains(requestEvents, e => e.Kind == ChatActivityEventKind.ToolCallStarted
                && e.ScopeId == toolScope.ScopeId);
        }
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ConcurrentToolInvocations_AllEventsAttributedToOwnScopes()
    {
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int startedCount = 0;

        async Task<string> WaitForPeersAsync()
        {
            if (Interlocked.Increment(ref startedCount) == 2)
            {
                bothStarted.SetResult();
            }

            await bothStarted.Task;
            return "ok";
        }

        AIFunction first = AIFunctionFactory.Create(WaitForPeersAsync, "tool_one");
        AIFunction second = AIFunctionFactory.Create(WaitForPeersAsync, "tool_two");
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(
                ScriptedChatClient.FunctionCallUpdate("call-1", "tool_one"),
                ScriptedChatClient.FunctionCallUpdate("call-2", "tool_two"))
            .AddTurn(ScriptedChatClient.TextUpdate("done"));
        var observer = new RecordingObserver();
        IChatClient client = TestPipeline.Build(inner, observer: observer, allowConcurrentTools: true);

        await TestPipeline.DrainAsync(client.GetStreamingResponseAsync(
            TestPipeline.UserMessage("go"),
            new ChatOptions { Tools = [first, second] }));

        IReadOnlyList<ChatActivityEvent> started = observer.EventsOfKind(ChatActivityEventKind.ToolCallStarted);
        Assert.Equal(2, started.Count);
        Assert.NotEqual(started[0].ScopeId, started[1].ScopeId);
        Assert.Equal(started[0].ParentScopeId, started[1].ParentScopeId);

        ChatActivityReport report = Assert.Single(observer.Reports);
        Assert.Equal(2, report.Root.Children.Count);
        Assert.Equal(
            new[] { "tool_one", "tool_two" },
            report.Root.Children.Select(c => c.ToolName).Order().ToArray());
    }
}
