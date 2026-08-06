using Andes.Extensions.AI.Unit.Test.Infrastructure;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Unit.Test;

public class ReasoningDetectionTests
{
    [Fact]
    public async Task GetStreamingResponseAsync_ReasoningContent_EmitsSingleReasoningStatus()
    {
        var scripted = new ScriptedChatClient(new ScriptedTurn
        {
            Updates =
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("secret chain of thought")]),
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("more hidden reasoning")]),
                new ChatResponseUpdate(ChatRole.Assistant, "The answer is 42."),
            ],
        });
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client);
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        ChatProgressUpdate reasoning = Assert.Single(progress, update => update.Kind == ChatProgressKind.Reasoning);
        Assert.Equal("Reasoning...", reasoning.Message);
        Assert.Equal(0, reasoning.Depth);
        Assert.DoesNotContain("chain of thought", reasoning.Message);
        Assert.DoesNotContain("hidden reasoning", reasoning.Message);

        int reasoningIndex = TestPipeline.IndexOfProgress(updates, ChatProgressKind.Reasoning);
        int contentIndex = TestPipeline.IndexOfContent<TextReasoningContent>(updates);
        Assert.True(reasoningIndex >= 0 && contentIndex >= 0 && reasoningIndex < contentIndex,
            $"The Reasoning status (index {reasoningIndex}) must precede the reasoning content (index {contentIndex}).");
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ReasoningThenText_EmitsSingleReasoningCompleted()
    {
        var scripted = new ScriptedChatClient(new ScriptedTurn
        {
            Updates =
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("secret chain of thought")]),
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("more hidden reasoning")]),
                new ChatResponseUpdate(ChatRole.Assistant, string.Empty),
                new ChatResponseUpdate(ChatRole.Assistant, "The answer is 42."),
            ],
        });
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client);
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        ChatProgressUpdate completed = Assert.Single(progress, update => update.Kind == ChatProgressKind.ReasoningCompleted);
        Assert.Equal("Reasoning completed", completed.Message);
        Assert.Equal(0, completed.Depth);
        Assert.NotNull(completed.Duration);
        Assert.True(completed.Duration >= TimeSpan.Zero);
        Assert.DoesNotContain("chain of thought", completed.Message);

        // The empty text chunk must not close the turn; completion coincides with the first
        // non-empty answer text, landing after the Reasoning status and before that text update.
        int reasoningIndex = TestPipeline.IndexOfProgress(updates, ChatProgressKind.Reasoning);
        int completedIndex = TestPipeline.IndexOfProgress(updates, ChatProgressKind.ReasoningCompleted);
        int textIndex = updates.FindIndex(update => update.Text == "The answer is 42.");
        Assert.True(reasoningIndex < completedIndex && completedIndex < textIndex,
            $"Expected Reasoning ({reasoningIndex}) < ReasoningCompleted ({completedIndex}) < answer text ({textIndex}).");
        int emptyTextIndex = updates.FindIndex(update =>
            update.Contents.OfType<TextContent>().Any(text => text.Text.Length == 0));
        Assert.True(emptyTextIndex >= 0 && emptyTextIndex < completedIndex,
            "The empty text chunk must not close the reasoning turn.");
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ReasoningThenUnwrappedToolCall_CompletesBeforeToolHeader()
    {
        var scripted = new ScriptedChatClient(
            new ScriptedTurn
            {
                Updates =
                [
                    new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("planning the call")]),
                    new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("call-1", "SomeHostedTool")]),
                ],
            },
            ScriptedTurn.Text("Handled elsewhere."));
        AIFunction tool = AIFunctionFactory.Create(() => "sunny", "GetWeather");
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        // "SomeHostedTool" is not a wrapped tool, so its sighting emits the best-effort
        // ToolInvoking header from within the same inspection pass — the completion must win.
        List<ChatProgressKind> kinds = progress.Select(update => update.Kind).ToList();
        int completedIndex = kinds.IndexOf(ChatProgressKind.ReasoningCompleted);
        int toolInvokingIndex = kinds.IndexOf(ChatProgressKind.ToolInvoking);
        Assert.True(completedIndex >= 0 && toolInvokingIndex >= 0 && completedIndex < toolInvokingIndex,
            $"ReasoningCompleted ({completedIndex}) must precede the unwrapped tool header ({toolInvokingIndex}).");
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ReasoningOnlyTurn_ClosesAtStreamEnd()
    {
        var scripted = new ScriptedChatClient(new ScriptedTurn
        {
            Updates =
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("thinking without answering")]),
            ],
        });
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client);
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        ChatProgressUpdate completed = Assert.Single(progress, update => update.Kind == ChatProgressKind.ReasoningCompleted);
        Assert.NotNull(completed.Duration);
        Assert.Equal(ChatProgressKind.RequestCompleted, progress[^1].Kind);
        Assert.True(progress.IndexOf(completed) < progress.Count - 1,
            "The stream-end close must precede the trailing RequestCompleted.");
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ReasoningAcrossToolRoundTrip_ReemitsPerTurn()
    {
        var scripted = new ScriptedChatClient(
            new ScriptedTurn
            {
                Updates =
                [
                    new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("planning the call")]),
                    new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("call-1", "GetWeather")]),
                ],
            },
            new ScriptedTurn
            {
                Updates =
                [
                    new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("interpreting the result")]),
                    new ChatResponseUpdate(ChatRole.Assistant, "It's sunny."),
                ],
            });
        AIFunction tool = AIFunctionFactory.Create(() => "sunny", "GetWeather");
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        List<ChatProgressKind> kinds = progress.Select(update => update.Kind).ToList();
        Assert.Equal(2, kinds.Count(kind => kind == ChatProgressKind.Reasoning));
        Assert.Equal(2, kinds.Count(kind => kind == ChatProgressKind.ReasoningCompleted));

        int firstReasoning = kinds.IndexOf(ChatProgressKind.Reasoning);
        int firstCompleted = kinds.IndexOf(ChatProgressKind.ReasoningCompleted);
        int toolInvoking = kinds.IndexOf(ChatProgressKind.ToolInvoking);
        int toolCompleted = kinds.IndexOf(ChatProgressKind.ToolCompleted);
        int secondReasoning = kinds.LastIndexOf(ChatProgressKind.Reasoning);
        int secondCompleted = kinds.LastIndexOf(ChatProgressKind.ReasoningCompleted);
        Assert.True(firstReasoning < toolInvoking,
            "The first turn's Reasoning must precede the tool header.");
        Assert.True(firstReasoning < firstCompleted && firstCompleted < toolInvoking,
            "The first turn's ReasoningCompleted must close before the tool header.");
        Assert.True(toolCompleted < secondReasoning,
            "The second turn's Reasoning must follow the tool completion.");
        Assert.True(secondReasoning < secondCompleted,
            "The second turn's ReasoningCompleted must follow its Reasoning.");
    }

    [Fact]
    public async Task GetStreamingResponseAsync_NoReasoningContent_EmitsNoRequestLevelStatuses()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "GetWeather"),
            ScriptedTurn.Text("It's sunny."));
        AIFunction tool = AIFunctionFactory.Create(() => "sunny", "GetWeather");
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        Assert.Equal(ChatProgressKind.ToolInvoking, progress[0].Kind);
        Assert.DoesNotContain(progress, update => update.Kind == ChatProgressKind.Custom);
        Assert.DoesNotContain(progress, update => update.Kind == ChatProgressKind.Reasoning);
        Assert.DoesNotContain(progress, update => update.Kind == ChatProgressKind.ReasoningCompleted);
    }

    [Fact]
    public async Task GetResponseAsync_ReasoningContent_NotifiesObserversOnce()
    {
        var observer = new CollectingProgressObserver();
        var scripted = new ScriptedChatClient(new ScriptedTurn
        {
            Updates =
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("weighing options")]),
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("choosing an answer")]),
                new ChatResponseUpdate(ChatRole.Assistant, "Done."),
            ],
        });
        IChatClient client = TestPipeline.Build(scripted, options => options.Observers.Add(observer));

        ChatResponse response = await client.GetResponseAsync("prompt");

        ChatProgressUpdate reasoning = Assert.Single(observer.Updates, update => update.Kind == ChatProgressKind.Reasoning);
        Assert.Equal("Reasoning...", reasoning.Message);
        Assert.DoesNotContain("weighing options", reasoning.Message);
        Assert.Contains("Done.", response.Text);
    }

    [Fact]
    public async Task GetResponseAsync_ReasoningContent_NotifiesObserversWithCompletedPair()
    {
        var observer = new CollectingProgressObserver();
        var scripted = new ScriptedChatClient(new ScriptedTurn
        {
            Updates =
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("weighing options")]),
                new ChatResponseUpdate(ChatRole.Assistant, "Done."),
            ],
        });
        IChatClient client = TestPipeline.Build(scripted, options => options.Observers.Add(observer));

        await client.GetResponseAsync("prompt");

        List<ChatProgressUpdate> notified = [.. observer.Updates];
        ChatProgressUpdate reasoning = Assert.Single(notified, update => update.Kind == ChatProgressKind.Reasoning);
        ChatProgressUpdate completed = Assert.Single(notified, update => update.Kind == ChatProgressKind.ReasoningCompleted);
        Assert.Equal("Reasoning completed", completed.Message);
        Assert.Null(completed.Duration);
        Assert.True(notified.IndexOf(reasoning) < notified.IndexOf(completed),
            "The post-hoc pair must arrive in detection-then-completion order.");
    }
}
