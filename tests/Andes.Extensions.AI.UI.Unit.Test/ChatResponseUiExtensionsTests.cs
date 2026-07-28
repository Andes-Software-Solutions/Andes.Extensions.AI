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
