using System.IO.Pipelines;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Enterprise.AI.Middleware.Tests.TestDoubles;

/// <summary>
/// Hosts a genuine MCP client/server pair over in-process pipe streams so tests can obtain real
/// <see cref="McpClientTool"/> instances (and invoke them) without processes or network access.
/// </summary>
public sealed class InMemoryMcpFixture : IAsyncLifetime
{
    private McpServer? _server;
    private Task? _serverTask;

    public const string ServerName = "Unit Test MCP";

    public McpClient Client { get; private set; } = null!;

    public IList<McpClientTool> Tools { get; private set; } = null!;

    public McpClientTool EchoTool => Tools.First(tool => tool.Name == "echo");

    public async Task InitializeAsync()
    {
        Pipe clientToServer = new();
        Pipe serverToClient = new();

        var serverOptions = new McpServerOptions
        {
            ServerInfo = new Implementation
            {
                Name = ServerName,
                Version = "1.0.0",
            },
            ToolCollection =
            [
                McpServerTool.Create(
                    (string message) => $"Echo: {message}",
                    new McpServerToolCreateOptions
                    {
                        Name = "echo",
                        Description = "Echoes the provided message back to the caller.",
                    }),
            ],
        };

        var serverTransport = new StreamServerTransport(
            clientToServer.Reader.AsStream(),
            serverToClient.Writer.AsStream());
        _server = McpServer.Create(serverTransport, serverOptions);
        _serverTask = _server.RunAsync(CancellationToken.None);

        var clientTransport = new StreamClientTransport(
            serverInput: clientToServer.Writer.AsStream(),
            serverOutput: serverToClient.Reader.AsStream());
        Client = await McpClient.CreateAsync(clientTransport);
        Tools = await Client.ListToolsAsync();
    }

    public async Task DisposeAsync()
    {
        if (Client is not null)
        {
            await Client.DisposeAsync();
        }

        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        if (_serverTask is not null)
        {
            try
            {
                await _serverTask;
            }
            catch (Exception)
            {
                // Server shutdown races with transport teardown; failures here are irrelevant to tests.
            }
        }
    }
}
