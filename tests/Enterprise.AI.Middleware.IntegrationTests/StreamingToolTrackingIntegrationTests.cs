using System.ComponentModel;
using Enterprise.AI.Middleware.Tracking;
using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.IntegrationTests;

public sealed class StreamingToolTrackingIntegrationTests : IClassFixture<AzureOpenAIFixture>
{
    private readonly AzureOpenAIFixture _azure;

    public StreamingToolTrackingIntegrationTests(AzureOpenAIFixture azure)
    {
        _azure = azure;
    }

    [Description("Gets the current weather for a location.")]
    private static string GetWeather([Description("The location to get the weather for.")] string location)
    {
        return $"The weather in {location} is sunny with a high of 24 degrees Celsius.";
    }

    [SkippableFact]
    public async Task GetStreamingResponseAsync_LocalFunctionTool_EmitsStatusAndUsageWithProviderName()
    {
        _azure.SkipUnlessConfigured();

        var observer = new CollectingObserver();
        IChatClient client = new ChatClientBuilder(_azure.CreateProviderClient())
            .UseToolTracking(new ToolTrackingOptions(), observer)
            .UseFunctionInvocation()
            .Build();
        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(GetWeather)],
            Temperature = 0,
        };

        List<ChatResponseUpdate> updates = [];
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "Use the GetWeather tool to tell me the weather in Amsterdam.")],
            options))
        {
            updates.Add(update);
        }

        ChatActivityEvent started = observer.Events.First(e => e.Kind == ChatActivityEventKind.ToolCallStarted);
        Assert.Equal("Calling Tool(s)", started.Header);
        Assert.Equal(ToolKind.Function, started.ToolKind);

        ChatActivityReport report = Assert.Single(observer.Reports);
        Assert.True(report.TotalUsage.TotalTokenCount > 0, "real token usage must be recorded");
        ModelUsageBreakdown breakdown = Assert.Single(report.UsageByModel);
        Assert.False(string.IsNullOrEmpty(breakdown.ProviderName), "the provider name must come from ChatClientMetadata");

        ActivityStatusContent finalUsage = updates
            .SelectMany(u => u.Contents)
            .OfType<ActivityStatusContent>()
            .Single(s => s.Kind == ChatActivityEventKind.RequestCompleted);
        Assert.True(finalUsage.Usage!.TotalTokenCount > 0);
    }

    [SkippableFact]
    public async Task GetResponseAsync_NonStreamingWithTool_ReportAttachedWithRealUsage()
    {
        _azure.SkipUnlessConfigured();

        IChatClient client = new ChatClientBuilder(_azure.CreateProviderClient())
            .UseToolTracking(new ToolTrackingOptions())
            .UseFunctionInvocation()
            .Build();
        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(GetWeather)],
            Temperature = 0,
        };

        ChatResponse response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Use the GetWeather tool to tell me the weather in Oslo.")],
            options);

        ChatActivityReport? report = ChatActivityReport.FromResponse(response);
        Assert.NotNull(report);
        Assert.True(report!.TotalUsage.TotalTokenCount > 0);
        Assert.Contains(report.Root.Children, scope => scope.ToolName == nameof(GetWeather));
    }
}
