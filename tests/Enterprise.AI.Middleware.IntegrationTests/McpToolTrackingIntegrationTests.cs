using Enterprise.AI.Middleware.Tracking;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace Enterprise.AI.Middleware.IntegrationTests;

public sealed class McpToolTrackingIntegrationTests : IClassFixture<AzureOpenAIFixture>
{
    private readonly AzureOpenAIFixture _azure;

    public McpToolTrackingIntegrationTests(AzureOpenAIFixture azure)
    {
        _azure = azure;
    }

    private static StdioClientTransport CreateTestServerTransport()
    {
        string serverPath = Path.Combine(AppContext.BaseDirectory, "Enterprise.AI.Middleware.TestMcpServer.dll");
        Assert.True(File.Exists(serverPath), $"Test MCP server not found at {serverPath}.");

        return new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = [serverPath],
            Name = "Enterprise Test MCP",
        });
    }

    [SkippableFact]
    public async Task GetStreamingResponseAsync_McpToolViaStdioTestServer_EmitsMcpHeaderWithServerName()
    {
        _azure.SkipUnlessConfigured();

        await using McpClient mcpClient = await McpClient.CreateAsync(CreateTestServerTransport());
        IList<McpClientTool> mcpTools = await mcpClient.ListToolsAsync();
        IList<AITool> trackedTools = mcpTools.WithTrackingMetadata(mcpClient);

        var observer = new CollectingObserver();
        IChatClient client = new ChatClientBuilder(_azure.CreateProviderClient())
            .UseToolTracking(new ToolTrackingOptions(), observer)
            .UseFunctionInvocation()
            .Build();

        List<ChatResponseUpdate> updates = [];
        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "Use the echo tool to echo the text 'integration test'.")],
            new ChatOptions { Tools = trackedTools, Temperature = 0 }))
        {
            updates.Add(update);
        }

        ChatActivityEvent started = observer.Events.First(e => e.Kind == ChatActivityEventKind.ToolCallStarted);
        Assert.Equal(ToolKind.Mcp, started.ToolKind);
        Assert.Equal("Enterprise Test MCP", started.SourceName);
        Assert.Equal("Calling Enterprise Test MCP MCP", started.Header);
        Assert.Equal("Called echo", started.Subheader);

        ChatActivityReport report = Assert.Single(observer.Reports);
        Assert.Contains(report.Root.Children, scope => scope.ToolKind == ToolKind.Mcp && scope.ToolName == "echo");
    }
}
