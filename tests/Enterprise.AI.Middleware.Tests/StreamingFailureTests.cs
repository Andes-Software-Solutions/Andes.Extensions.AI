using Enterprise.AI.Middleware.Tests.TestDoubles;
using Enterprise.AI.Middleware.Tracking;
using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tests;

public sealed class StreamingFailureTests
{
    private static async IAsyncEnumerable<ChatResponseUpdate> YieldThenThrowAsync()
    {
        yield return ScriptedChatClient.TextUpdate("partial answer");
        await Task.Yield();
        throw new InvalidOperationException("provider exploded");
    }

    [Fact]
    public async Task GetStreamingResponseAsync_InnerThrowsMidStream_OriginalExceptionPropagates()
    {
        ScriptedChatClient inner = new ScriptedChatClient().AddTurn(_ => YieldThenThrowAsync());
        var observer = new RecordingObserver();
        IChatClient client = TestPipeline.Build(inner, observer: observer);

        List<ChatResponseUpdate> received = [];
        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(
                TestPipeline.UserMessage("hi")))
            {
                received.Add(update);
            }
        });

        Assert.Equal("provider exploded", thrown.Message);
        Assert.Contains(received, u => u.Text.Contains("partial answer", StringComparison.Ordinal));

        ChatActivityEvent failed = observer.EventsOfKind(ChatActivityEventKind.RequestFailed).Single();
        Assert.Equal(typeof(InvalidOperationException).FullName, failed.ErrorType);
        Assert.Null(failed.ErrorMessage);
        Assert.Single(observer.Reports);
        Assert.Empty(observer.EventsOfKind(ChatActivityEventKind.RequestCompleted));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_InnerThrows_IncludeErrorMessagesCarriesMessage()
    {
        ScriptedChatClient inner = new ScriptedChatClient().AddTurn(_ => YieldThenThrowAsync());
        var observer = new RecordingObserver();
        var options = new ToolTrackingOptions { IncludeErrorMessages = true };
        IChatClient client = TestPipeline.Build(inner, options, observer);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => TestPipeline.DrainAsync(client.GetStreamingResponseAsync(TestPipeline.UserMessage("hi"))));

        ChatActivityEvent failed = observer.EventsOfKind(ChatActivityEventKind.RequestFailed).Single();
        Assert.Equal("provider exploded", failed.ErrorMessage);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_AbandonedEnumeration_PublishesRequestFailedOnce()
    {
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedChatClient inner = new ScriptedChatClient().AddBlockingTurn(never);
        var observer = new RecordingObserver();
        IChatClient client = TestPipeline.Build(inner, observer: observer);

        IAsyncEnumerator<ChatResponseUpdate> enumerator = client
            .GetStreamingResponseAsync(TestPipeline.UserMessage("hi"))
            .GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        await enumerator.DisposeAsync();

        ChatActivityEvent failed = observer.EventsOfKind(ChatActivityEventKind.RequestFailed).Single();
        Assert.Contains("OperationCanceledException", failed.ErrorType, StringComparison.Ordinal);
        Assert.Single(observer.Reports);
        Assert.Empty(observer.EventsOfKind(ChatActivityEventKind.RequestCompleted));
    }
}
