using Andes.Extensions.AI.Integration.Test;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace Andes.Extensions.AI.Mcp.Integration.Test;

public sealed class McpToolTrackingIntegrationTests(AzureOpenAIFixture fixture) : IClassFixture<AzureOpenAIFixture>
{
    private readonly AzureOpenAIFixture _fixture = fixture;

    private static StdioClientTransport CreateTestServerTransport()
    {
        string serverPath = Path.Combine(AppContext.BaseDirectory, "Andes.Extensions.AI.TestMcpServer.dll");
        Assert.True(File.Exists(serverPath), $"Test MCP server not found at {serverPath}.");

        return new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = [serverPath],
            Name = "Andes Test MCP",
        });
    }

    [SkippableFact]
    public async Task GetStreamingResponseAsync_McpToolViaStdioTestServer_EmitsMcpHeaderWithServerName()
    {
        Skip.IfNot(_fixture.IsConfigured, AzureOpenAIFixture.SkipReason);

        await using McpClient mcpClient = await McpClient.CreateAsync(CreateTestServerTransport());
        IList<McpClientTool> mcpTools = await mcpClient.ListToolsAsync();
        IList<AITool> trackedTools = mcpTools.WithTracking(mcpClient);

        var observer = new RecordingObserver();
        IChatClient client = _fixture.CreatePipeline(options =>
        {
            options.UseMcpToolClassification();
            options.Observers.Add(observer);
        });

        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "Use the echo tool to echo the text 'integration test'.")],
            new ChatOptions { Tools = trackedTools }))
        {
        }

        ChatProgressUpdate? invoking = observer.Updates.FirstOrDefault(update =>
            update.Kind == ChatProgressKind.ToolInvoking && update.ToolKind == ToolKind.McpTool);
        Assert.NotNull(invoking);
        Assert.Equal("Andes Test MCP", invoking.ToolSource);
        Assert.Equal("Calling Andes Test MCP", invoking.Message);
        Assert.Equal("echo", invoking.ToolName);

        Assert.NotNull(observer.Report);
        Assert.Contains(
            observer.Report.ToolCalls,
            call => call.Kind == ToolKind.McpTool && call.ToolName == "echo");
    }

    [SkippableFact]
    public async Task GetStreamingResponseAsync_McpToolReportsProgress_EmitsToolProgressWithNumericValues()
    {
        Skip.IfNot(_fixture.IsConfigured, AzureOpenAIFixture.SkipReason);

        await using McpClient mcpClient = await McpClient.CreateAsync(CreateTestServerTransport());
        IList<McpClientTool> mcpTools = await mcpClient.ListToolsAsync();
        IList<AITool> trackedTools = mcpTools.WithTracking(mcpClient);

        var observer = new RecordingObserver();
        IChatClient client = _fixture.CreatePipeline(options =>
        {
            options.UseMcpToolClassification();
            options.Observers.Add(observer);
        });

        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "Use the count_down tool with steps set to 3, then confirm.")],
            new ChatOptions { Tools = trackedTools }))
        {
        }

        List<ChatProgressUpdate> statuses = observer.Updates
            .Where(update => update.Kind == ChatProgressKind.ToolProgress)
            .ToList();
        Assert.NotEmpty(statuses);
        Assert.All(statuses, status =>
        {
            Assert.Equal(ToolKind.McpTool, status.ToolKind);
            Assert.Equal("count_down", status.ToolName);
            Assert.NotNull(status.Progress);
        });
    }

    private sealed class RecordingObserver : IChatProgressObserver
    {
        private readonly Lock _lock = new();
        private readonly List<ChatProgressUpdate> _updates = [];
        private ChatUsageReport? _report;

        public IReadOnlyList<ChatProgressUpdate> Updates
        {
            get
            {
                lock (_lock)
                {
                    return [.. _updates];
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
                _updates.Add(update);
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
