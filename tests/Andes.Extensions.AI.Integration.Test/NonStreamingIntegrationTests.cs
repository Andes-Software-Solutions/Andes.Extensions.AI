using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Integration.Test;

public class NonStreamingIntegrationTests : IClassFixture<AzureOpenAIFixture>
{
    private readonly AzureOpenAIFixture _fixture;

    public NonStreamingIntegrationTests(AzureOpenAIFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task GetResponseAsync_WithTool_AttachesReportAndNotifiesObservers()
    {
        Skip.IfNot(_fixture.IsConfigured, AzureOpenAIFixture.SkipReason);

        var observer = new RecordingObserver();
        AIFunction tool = AIFunctionFactory.Create(
            () => "2026-07-24T12:00:00Z",
            "GetCurrentTime",
            "Gets the current time in UTC.");
        IChatClient client = _fixture.CreatePipeline(options => options.Observers.Add(observer));

        ChatResponse response = await client.GetResponseAsync(
            "Call the GetCurrentTime tool, then tell me what time it returned.",
            new ChatOptions { Tools = [tool] });

        Assert.NotNull(response.AdditionalProperties);
        var report = Assert.IsType<ChatUsageReport>(
            response.AdditionalProperties[ToolTrackingChatClient.UsageReportPropertyName]);
        Assert.True(report.AssistantUsage.TotalTokenCount > 0);
        Assert.Single(report.ToolCalls, call => call.ToolName == "GetCurrentTime");

        Assert.Contains(observer.Kinds, kind => kind == ChatProgressKind.ToolInvoking);
        Assert.Contains(observer.Kinds, kind => kind == ChatProgressKind.RequestCompleted);
        Assert.NotNull(observer.Report);
        Assert.NotEmpty(response.Text);
    }

    private sealed class RecordingObserver : IChatProgressObserver
    {
        private readonly Lock _lock = new();
        private readonly List<ChatProgressKind> _kinds = [];
        private ChatUsageReport? _report;

        public IReadOnlyList<ChatProgressKind> Kinds
        {
            get
            {
                lock (_lock)
                {
                    return [.. _kinds];
                }
            }
        }

        public ChatUsageReport? Report
        {
            get
            {
                lock (_lock)
                {
                    return _report;
                }
            }
        }

        public void OnProgress(ChatProgressUpdate update)
        {
            lock (_lock)
            {
                _kinds.Add(update.Kind);
            }
        }

        public void OnRequestCompleted(ChatUsageReport report)
        {
            lock (_lock)
            {
                _report = report;
            }
        }
    }
}
