using Enterprise.AI.Middleware.Tests.TestDoubles;
using Enterprise.AI.Middleware.Tracking;
using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tests;

public sealed class ToolTrackingChatClientNonStreamingTests
{
    [Fact]
    public async Task GetResponseAsync_ToolInvoked_AttachesActivityReportWithToolScope()
    {
        AIFunction tool = AIFunctionFactory.Create(() => "sunny", "get_weather");
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.FunctionCallUpdate("call-1", "get_weather"))
            .AddTurn(
                ScriptedChatClient.TextUpdate("It is sunny."),
                ScriptedChatClient.UsageUpdate(9, 4, "scripted-model"));
        IChatClient client = TestPipeline.Build(inner);

        ChatResponse response = await client.GetResponseAsync(
            TestPipeline.UserMessage("weather?"),
            new ChatOptions { Tools = [tool] });

        ChatActivityReport? report = ChatActivityReport.FromResponse(response);
        Assert.NotNull(report);
        ActivityScopeReport toolScope = Assert.Single(report!.Root.Children);
        Assert.Equal(ToolKind.Function, toolScope.ToolKind);
        Assert.Equal("get_weather", toolScope.ToolName);
        Assert.Equal(13, report.TotalUsage.TotalTokenCount);
    }

    [Fact]
    public async Task GetResponseAsync_NoTools_ReportContainsRootUsageOnly()
    {
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(
                ScriptedChatClient.TextUpdate("plain answer"),
                ScriptedChatClient.UsageUpdate(3, 2, "scripted-model"));
        IChatClient client = TestPipeline.Build(inner);

        ChatResponse response = await client.GetResponseAsync(TestPipeline.UserMessage("hi"));

        ChatActivityReport? report = ChatActivityReport.FromResponse(response);
        Assert.NotNull(report);
        Assert.Empty(report!.Root.Children);
        Assert.Equal(5, report.Root.OwnUsage.TotalTokenCount);
        ModelUsageBreakdown breakdown = Assert.Single(report.UsageByModel);
        Assert.Equal("scripted-model", breakdown.ModelId);
        Assert.Equal("test.provider", breakdown.ProviderName);
    }

    [Fact]
    public async Task FromResponse_TrackedResponse_ReturnsSameInstanceAsObserverReport()
    {
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.TextUpdate("hi"));
        var observer = new RecordingObserver();
        IChatClient client = TestPipeline.Build(inner, observer: observer);

        ChatResponse response = await client.GetResponseAsync(TestPipeline.UserMessage("hi"));

        ChatActivityReport? attached = ChatActivityReport.FromResponse(response);
        Assert.Same(Assert.Single(observer.Reports), attached);
    }

    [Fact]
    public void FromResponse_UntrackedResponse_ReturnsNull()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "plain"));

        Assert.Null(ChatActivityReport.FromResponse(response));
    }

    [Fact]
    public async Task GetResponseAsync_InnerThrows_PublishesRequestFailedAndRethrows()
    {
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(_ => throw new InvalidOperationException("provider down"));
        var observer = new RecordingObserver();
        IChatClient client = TestPipeline.Build(inner, observer: observer);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetResponseAsync(TestPipeline.UserMessage("hi")));

        ChatActivityEvent failed = observer.EventsOfKind(ChatActivityEventKind.RequestFailed).Single();
        Assert.Equal(typeof(InvalidOperationException).FullName, failed.ErrorType);
        Assert.Single(observer.Reports);
    }
}
