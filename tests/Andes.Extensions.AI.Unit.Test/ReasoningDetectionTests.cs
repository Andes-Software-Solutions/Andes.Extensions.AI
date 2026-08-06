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

        int firstReasoning = kinds.IndexOf(ChatProgressKind.Reasoning);
        int toolInvoking = kinds.IndexOf(ChatProgressKind.ToolInvoking);
        int toolCompleted = kinds.IndexOf(ChatProgressKind.ToolCompleted);
        int secondReasoning = kinds.LastIndexOf(ChatProgressKind.Reasoning);
        Assert.True(firstReasoning < toolInvoking,
            "The first turn's Reasoning must precede the tool header.");
        Assert.True(toolCompleted < secondReasoning,
            "The second turn's Reasoning must follow the tool completion.");
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
        Assert.DoesNotContain(progress, update => update.Kind == ChatProgressKind.RequestStarted);
        Assert.DoesNotContain(progress, update => update.Kind == ChatProgressKind.Reasoning);
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
}
