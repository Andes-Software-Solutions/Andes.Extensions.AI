using System.ComponentModel;
using System.IO.Pipelines;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Andes.Extensions.AI.Demo;

/// <summary>
/// Hosts a genuine MCP client/server pair over in-process pipe streams — no processes or
/// network — so the demo can expose real <see cref="McpClientTool"/> instances through
/// <c>WithTracking</c>, with MCP progress notifications bridged into chat progress updates.
/// </summary>
internal sealed class DemoMcpServer : IAsyncDisposable
{
    public const string ServerName = "Andes Demo MCP";

    private McpServer? _server;
    private Task? _serverTask;

    public McpClient Client { get; private set; } = null!;

    public IList<McpClientTool> Tools { get; private set; } = null!;

    public static async Task<DemoMcpServer> StartAsync()
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
                    async Task<string> (
                        [Description("The city to forecast.")] string city,
                        [Description("The number of days to forecast (1-10).")] int days,
                        IProgress<ProgressNotificationValue> progress,
                        CancellationToken cancellationToken) =>
                    {
                        // Clamp the model-chosen argument so a "100-day forecast" prompt
                        // cannot stall the demo.
                        int clampedDays = Math.Clamp(days, 1, 10);

                        // A real progress-reporting tool is long-running; the per-step delay
                        // also keeps the fire-and-forget notifications ahead of the result.
                        for (int day = 1; day <= clampedDays; day++)
                        {
                            progress.Report(new ProgressNotificationValue
                            {
                                Progress = day,
                                Total = clampedDays,
                                Message = $"forecast day {day} of {clampedDays}",
                            });
                            await Task.Delay(350, cancellationToken);
                        }

                        return $"{clampedDays}-day outlook for {city}: mild, 18-24C, light winds.";
                    },
                    new McpServerToolCreateOptions
                    {
                        Name = "get_forecast",
                        Description = "Gets a multi-day weather forecast for a city, reporting per-day progress.",
                    }),
            ],
        };

        var demo = new DemoMcpServer();
        var serverTransport = new StreamServerTransport(
            clientToServer.Reader.AsStream(),
            serverToClient.Writer.AsStream());
        demo._server = McpServer.Create(serverTransport, serverOptions);
        demo._serverTask = demo._server.RunAsync(CancellationToken.None);

        try
        {
            var clientTransport = new StreamClientTransport(
                serverInput: clientToServer.Writer.AsStream(),
                serverOutput: serverToClient.Reader.AsStream());
            demo.Client = await McpClient.CreateAsync(clientTransport);
            demo.Tools = await demo.Client.ListToolsAsync();
        }
        catch
        {
            // The instance never reaches the caller's `await using` on a failed handshake,
            // so tear the already-running server down here.
            await demo.DisposeAsync();
            throw;
        }

        return demo;
    }

    public async ValueTask DisposeAsync()
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
                // Server shutdown races with transport teardown; irrelevant to the demo.
            }
        }
    }
}
