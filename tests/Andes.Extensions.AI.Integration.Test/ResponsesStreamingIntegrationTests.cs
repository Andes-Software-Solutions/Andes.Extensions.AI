using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Andes.Extensions.AI.Integration.Test;

/// <summary>
/// Exercises the tracked pipeline against the Azure OpenAI Responses API, reached through the
/// OpenAI-v1-compatible endpoint with the stable OpenAI library (the stable Azure.AI.OpenAI client
/// has no Responses surface). Requires the optional "AzureOpenAI:ResponsesDeployment" setting.
/// </summary>
public class ResponsesStreamingIntegrationTests(AzureOpenAIFixture fixture) : IClassFixture<AzureOpenAIFixture>
{
    private readonly AzureOpenAIFixture _fixture = fixture;

    [SkippableFact]
    public async Task GetStreamingResponseAsync_ResponsesApi_DetectsReasoningAndTracksUsage()
    {
        Skip.IfNot(_fixture.IsResponsesConfigured, AzureOpenAIFixture.ResponsesSkipReason);

        IChatClient client = CreateResponsesPipeline();
        var options = new ChatOptions
        {
            Reasoning = new ReasoningOptions { Output = ReasoningOutput.Summary },
        };

        var updates = new List<ChatResponseUpdate>();
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(
            "What is the sum of the first ten prime numbers? Reason it out, then answer.",
            options))
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

        Assert.DoesNotContain(progress, update => update.Kind == ChatProgressKind.RequestStarted);
        Assert.True(report.AssistantUsage.TotalTokenCount > 0, "The assistant should report token usage.");
        Assert.NotEmpty(string.Concat(updates.Select(update => update.Text)));

        // Whether summaries stream depends on the deployment (and possibly organization
        // verification); when they do, the middleware must announce Reasoning exactly once
        // for this single-turn request, ahead of the first reasoning content.
        int reasoningContentIndex = IndexOfContent<TextReasoningContent>(updates);
        Skip.If(reasoningContentIndex < 0,
            "The deployment streamed no reasoning summaries; enable summaries on a reasoning-capable deployment to exercise detection.");

        ChatProgressUpdate reasoning = Assert.Single(progress, update => update.Kind == ChatProgressKind.Reasoning);
        Assert.Equal("Reasoning...", reasoning.Message);
        int reasoningStatusIndex = IndexOfProgress(updates, ChatProgressKind.Reasoning);
        Assert.True(reasoningStatusIndex < reasoningContentIndex,
            $"The Reasoning status (index {reasoningStatusIndex}) must precede the reasoning content (index {reasoningContentIndex}).");
    }

    private IChatClient CreateResponsesPipeline()
    {
        var openAIClient = new OpenAIClient(
            new ApiKeyCredential(_fixture.Settings.ApiKey!),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(_fixture.Settings.Endpoint!.TrimEnd('/') + "/openai/v1"),
            });

        return openAIClient
            .GetResponsesClient()
            .AsIChatClient(_fixture.Settings.ResponsesDeployment!)
            .AsBuilder()
            .UseToolTracking()
            .UseFunctionInvocation()
            .Build();
    }

    private static int IndexOfProgress(IReadOnlyList<ChatResponseUpdate> updates, ChatProgressKind kind)
    {
        for (int i = 0; i < updates.Count; i++)
        {
            if (updates[i].Contents.OfType<ChatProgressContent>().Any(content => content.Progress.Kind == kind))
            {
                return i;
            }
        }

        return -1;
    }

    private static int IndexOfContent<TContent>(IReadOnlyList<ChatResponseUpdate> updates)
        where TContent : AIContent
    {
        for (int i = 0; i < updates.Count; i++)
        {
            if (updates[i].Contents.OfType<TContent>().Any())
            {
                return i;
            }
        }

        return -1;
    }
}
