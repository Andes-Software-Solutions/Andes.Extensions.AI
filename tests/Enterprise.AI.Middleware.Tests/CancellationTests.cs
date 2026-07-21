using Enterprise.AI.Middleware.Tests.TestDoubles;
using Enterprise.AI.Middleware.Tracking;
using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tests;

public sealed class CancellationTests
{
    [Fact]
    public async Task GetStreamingResponseAsync_CanceledMidStream_ThrowsAndReportsRequestFailed()
    {
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedChatClient inner = new ScriptedChatClient().AddBlockingTurn(never);
        var observer = new RecordingObserver();
        IChatClient client = TestPipeline.Build(inner, observer: observer);
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(
                TestPipeline.UserMessage("hi"),
                options: null,
                cts.Token))
            {
                cts.Cancel();
            }
        });

        ChatActivityEvent failed = observer.EventsOfKind(ChatActivityEventKind.RequestFailed).Single();
        Assert.Contains("CanceledException", failed.ErrorType, StringComparison.Ordinal);
        Assert.Single(observer.Reports);
        Assert.Empty(observer.EventsOfKind(ChatActivityEventKind.RequestCompleted));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_CancellationToken_FlowsIntoToolInvocation()
    {
        CancellationToken observedToken = CancellationToken.None;
        AIFunction tool = AIFunctionFactory.Create(
            (CancellationToken cancellationToken) =>
            {
                observedToken = cancellationToken;
                return "ok";
            },
            "token_probe");
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.FunctionCallUpdate("call-1", "token_probe"))
            .AddTurn(ScriptedChatClient.TextUpdate("done"));
        IChatClient client = TestPipeline.Build(inner);
        using var cts = new CancellationTokenSource();

        await TestPipeline.DrainAsync(
            client.GetStreamingResponseAsync(
                TestPipeline.UserMessage("go"),
                new ChatOptions { Tools = [tool] },
                cts.Token),
            cts.Token);

        Assert.True(observedToken.CanBeCanceled, "the request token must flow into the tool");
    }
}
