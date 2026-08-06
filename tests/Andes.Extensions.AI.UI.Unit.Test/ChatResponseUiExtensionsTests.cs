using Andes.Extensions.AI.Unit.Test.Infrastructure;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.UI.Unit.Test;

public class ChatResponseUiExtensionsTests
{
    [Fact]
    public async Task ToUiEventsAsync_FunctionTool_ProducesCleanDisplayNameAndKind()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "GetForecast"),
            ScriptedTurn.Text("Done."));
        AIFunction tool = AIFunctionFactory.Create(
            () =>
            {
                ChatProgress.Report("Contacting the forecast service…");
                return "sunny";
            },
            "GetForecast");
        IChatClient client = TestPipeline.Build(scripted);

        List<AssistantUiEvent> events = await CollectAsync(client, new ChatOptions { Tools = [tool] });

        AssistantUiEvent started = Assert.Single(events, e => e.Kind == AssistantUiEventKind.ActivityStarted);
        Assert.Equal("GetForecast", started.DisplayName);
        Assert.Equal(ToolKind.Function, started.ToolKind);
        Assert.Contains(
            events,
            e => e.Kind == AssistantUiEventKind.ActivityProgress && e.Message == "Contacting the forecast service…");
        Assert.Contains(events, e => e.Kind == AssistantUiEventKind.ActivityCompleted);
        Assert.Contains(events, e => e.Kind == AssistantUiEventKind.Finished);
    }

    [Theory]
    [InlineData(ToolKind.McpTool, "Andes Test MCP")]
    [InlineData(ToolKind.Agent, "Research Agent")]
    public async Task ToUiEventsAsync_NameEndingWithKindWord_DisplayNameHasNoRepeat(ToolKind kind, string source)
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "do_work"),
            ScriptedTurn.Text("Done."));
        AIFunction tool = AIFunctionFactory.Create(() => "ok", "do_work");
        IChatClient client = TestPipeline.Build(scripted, options =>
            options.ToolClassifier = candidate => new ToolDescriptor
            {
                Name = candidate.Name,
                Kind = kind,
                Source = source,
            });

        List<AssistantUiEvent> events = await CollectAsync(client, new ChatOptions { Tools = [tool] });

        AssistantUiEvent started = Assert.Single(events, e => e.Kind == AssistantUiEventKind.ActivityStarted);
        Assert.Equal(source, started.DisplayName);
        Assert.Equal(kind, started.ToolKind);
        Assert.DoesNotContain("MCP MCP", started.DisplayName);
        Assert.DoesNotContain("Agent Agent", started.DisplayName);
    }

    [Fact]
    public async Task ToStatusSnapshotsAsync_FunctionTool_FoldsIntoCompletedActivityCard()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "GetForecast"),
            ScriptedTurn.Text("Done."));
        AIFunction tool = AIFunctionFactory.Create(
            () =>
            {
                ChatProgress.Report("Working…");
                return "sunny";
            },
            "GetForecast");
        IChatClient client = TestPipeline.Build(scripted);

        AssistantStatusSnapshot? last = null;
        await foreach (AssistantStatusSnapshot snapshot in client
            .GetStreamingResponseAsync("prompt", new ChatOptions { Tools = [tool] })
            .ToStatusSnapshotsAsync())
        {
            last = snapshot;
        }

        Assert.NotNull(last);
        AssistantActivity activity = Assert.Single(last!.Activities);
        Assert.Equal("GetForecast", activity.DisplayName);
        Assert.Equal(ToolKind.Function, activity.Kind);
        Assert.Equal(ActivityState.Completed, activity.State);
        Assert.Contains(activity.SubStatuses, s => s.Message == "Working…");
        Assert.Equal(ActivityState.Completed, last.Phase);
    }

    [Fact]
    public async Task ToStatusSnapshotsAsync_DevPrependedCustomStatus_SetsAssistantStatus()
    {
        var scripted = new ScriptedChatClient(ScriptedTurn.Text("Done."));
        IChatClient client = TestPipeline.Build(scripted);

        async IAsyncEnumerable<ChatResponseUpdate> StreamWithPrependedStatus()
        {
            yield return ChatProgressUpdate.CreateCustom("Starting request").ToResponseUpdate();

            await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync("prompt"))
            {
                yield return update;
            }
        }

        AssistantStatusSnapshot? first = null;
        AssistantStatusSnapshot? last = null;
        await foreach (AssistantStatusSnapshot snapshot in StreamWithPrependedStatus().ToStatusSnapshotsAsync())
        {
            first ??= snapshot;
            last = snapshot;
        }

        Assert.NotNull(first);
        Assert.Equal("Starting request", first!.AssistantStatus);
        Assert.NotNull(last);
        Assert.Equal(ActivityState.Completed, last!.Phase);
        Assert.Contains("Done.", last.Text);
    }

    [Fact]
    public async Task ToUiEventsAsync_ReasoningContent_EmitsReasoningDeltasInOrder()
    {
        var scripted = new ScriptedChatClient(new ScriptedTurn
        {
            Updates =
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("weighing options. ")]),
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("choosing an answer.")]),
                new ChatResponseUpdate(ChatRole.Assistant, "The answer is 42."),
            ],
        });
        IChatClient client = TestPipeline.Build(scripted);

        List<AssistantUiEvent> events = await CollectAsync(client, new ChatOptions());

        List<AssistantUiEvent> reasoningDeltas = [.. events.Where(e => e.Kind == AssistantUiEventKind.ReasoningDelta)];
        Assert.Equal(2, reasoningDeltas.Count);
        Assert.Equal("weighing options. ", reasoningDeltas[0].Text);
        Assert.Equal("choosing an answer.", reasoningDeltas[1].Text);

        AssistantUiEvent reasoningStatus = Assert.Single(
            events,
            e => e.Kind == AssistantUiEventKind.Status && e.Message == "Reasoning...");
        Assert.True(
            events.IndexOf(reasoningStatus) < events.IndexOf(reasoningDeltas[0]),
            "The Reasoning status must precede the first reasoning delta.");

        AssistantUiEvent textDelta = Assert.Single(events, e => e.Kind == AssistantUiEventKind.TextDelta);
        Assert.Equal("The answer is 42.", textDelta.Text);
    }

    [Fact]
    public async Task ToUiEventsAsync_EmptyReasoningContent_EmitsNoReasoningDelta()
    {
        var scripted = new ScriptedChatClient(new ScriptedTurn
        {
            Updates =
            [
                // The encrypted-only shape: empty text, nothing renderable.
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent(string.Empty)]),
                new ChatResponseUpdate(ChatRole.Assistant, "Done."),
            ],
        });
        IChatClient client = TestPipeline.Build(scripted);

        List<AssistantUiEvent> events = await CollectAsync(client, new ChatOptions());

        Assert.DoesNotContain(events, e => e.Kind == AssistantUiEventKind.ReasoningDelta);
    }

    [Fact]
    public async Task ToStatusSnapshotsAsync_ReasoningAcrossToolRoundTrip_AccumulatesReasoningText()
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

        AssistantStatusSnapshot? last = null;
        await foreach (AssistantStatusSnapshot snapshot in client
            .GetStreamingResponseAsync("prompt", new ChatOptions { Tools = [tool] })
            .ToStatusSnapshotsAsync())
        {
            last = snapshot;
        }

        Assert.NotNull(last);
        Assert.Equal("planning the callinterpreting the result", last!.ReasoningText);
        Assert.Equal(ActivityState.Completed, last.Phase);
        AssistantActivity activity = Assert.Single(last.Activities);
        Assert.Equal(ActivityState.Completed, activity.State);
        Assert.Contains("It's sunny.", last.Text);
    }

    private static async Task<List<AssistantUiEvent>> CollectAsync(IChatClient client, ChatOptions options)
    {
        var events = new List<AssistantUiEvent>();
        await foreach (AssistantUiEvent uiEvent in client
            .GetStreamingResponseAsync("prompt", options)
            .ToUiEventsAsync())
        {
            events.Add(uiEvent);
        }

        return events;
    }
}
