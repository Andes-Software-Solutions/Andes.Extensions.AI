using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Integration.Test;

public class StreamingIntegrationTests(AzureOpenAIFixture fixture) : IClassFixture<AzureOpenAIFixture>
{
    private readonly AzureOpenAIFixture _fixture = fixture;

    [SkippableFact]
    public async Task GetStreamingResponseAsync_WithTool_TracksUsageAndProgress()
    {
        Skip.IfNot(_fixture.IsConfigured, AzureOpenAIFixture.SkipReason);

        AIFunction tool = AIFunctionFactory.Create(
            () =>
            {
                ChatProgress.Report("Formatting time...");
                return "2026-07-24T12:00:00Z";
            },
            "GetCurrentTime",
            "Gets the current time in UTC.");
        IChatClient client = _fixture.CreatePipeline();

        var updates = new List<ChatResponseUpdate>();
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(
            "Call the GetCurrentTime tool, then tell me what time it returned.",
            new ChatOptions { Tools = [tool] }))
        {
            updates.Add(update);
        }

        List<ChatProgressUpdate> progress = updates
            .SelectMany(update => update.Contents)
            .OfType<ChatProgressContent>()
            .Select(content => content.Progress)
            .ToList();
        ChatUsageReport report = updates
            .SelectMany(update => update.Contents)
            .OfType<UsageReportContent>()
            .Single()
            .Report;

        Assert.Equal(ChatProgressKind.RequestStarted, progress[0].Kind);
        Assert.Contains(progress, update => update.Kind == ChatProgressKind.ToolInvoking && update.ToolName == "GetCurrentTime");
        Assert.Contains(progress, update => update.Kind == ChatProgressKind.ToolProgress && update.Message == "Formatting time...");
        Assert.Contains(progress, update => update.Kind == ChatProgressKind.ToolCompleted);
        Assert.Equal(ChatProgressKind.RequestCompleted, progress[^1].Kind);

        Assert.True(report.AssistantUsage.TotalTokenCount > 0, "The assistant should report token usage.");
        ToolCallUsage call = Assert.Single(report.ToolCalls, call => call.ToolName == "GetCurrentTime");
        Assert.True(call.Succeeded);
        Assert.NotEmpty(string.Concat(updates.Select(update => update.Text)));
    }

    [SkippableFact]
    public async Task GetStreamingResponseAsync_WithoutTools_ProducesUsageOnlyReport()
    {
        Skip.IfNot(_fixture.IsConfigured, AzureOpenAIFixture.SkipReason);

        IChatClient client = _fixture.CreatePipeline();

        var updates = new List<ChatResponseUpdate>();
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync("Reply with the single word: pong"))
        {
            updates.Add(update);
        }

        ChatUsageReport report = updates
            .SelectMany(update => update.Contents)
            .OfType<UsageReportContent>()
            .Single()
            .Report;

        Assert.True(report.TotalUsage.TotalTokenCount > 0);
        Assert.Empty(report.ToolCalls);
        Assert.NotNull(report.ModelId);
    }

    [SkippableFact]
    public async Task GetStreamingResponseAsync_StrippedResponse_KeepsRealTextOnly()
    {
        Skip.IfNot(_fixture.IsConfigured, AzureOpenAIFixture.SkipReason);

        IChatClient client = _fixture.CreatePipeline();

        var updates = new List<ChatResponseUpdate>();
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync("Reply with the single word: pong"))
        {
            updates.Add(update);
        }

        ChatResponse response = updates.ToChatResponse().StripProgressContent();

        Assert.Empty(response.Messages.SelectMany(message => message.Contents).OfType<ChatProgressContent>());
        Assert.Empty(response.Messages.SelectMany(message => message.Contents).OfType<UsageReportContent>());
        Assert.NotEmpty(response.Text);
    }
}
