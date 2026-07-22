using System.IO.Pipelines;
using ModelContextProtocol;
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

    public McpClientTool CountDownTool => Tools.First(tool => tool.Name == "count_down");

    /// <summary>
    /// Gets or sets the gate the <c>count_down</c> tool awaits (when asked to) before returning
    /// its result, letting a test hold the tool open until progress notifications were observed.
    /// </summary>
    public TaskCompletionSource? ProgressAck { get; set; }

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
                McpServerTool.Create(
                    async Task<string> (int steps, bool waitForAck, IProgress<ProgressNotificationValue> progress) =>
                    {
                        for (int i = 1; i <= steps; i++)
                        {
                            progress.Report(new ProgressNotificationValue
                            {
                                Progress = i,
                                Total = steps,
                                Message = $"step {i} of {steps}",
                            });
                        }

                        // Progress notifications are sent fire-and-forget and race the tool
                        // result; once the result lands, the client unregisters the progress
                        // handler and late notifications are dropped. Real progress-reporting
                        // tools are long-running, so the race is invisible in practice — this
                        // test tool emulates that by holding the result until the test has
                        // observed the notifications.
                        if (waitForAck && ProgressAck is { } ack)
                        {
                            await ack.Task.WaitAsync(TimeSpan.FromSeconds(10));
                        }

                        return "counted";
                    },
                    new McpServerToolCreateOptions
                    {
                        Name = "count_down",
                        Description = "Counts to the requested number of steps, reporting progress.",
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
