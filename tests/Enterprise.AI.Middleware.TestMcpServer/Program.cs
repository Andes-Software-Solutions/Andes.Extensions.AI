using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "Enterprise Test MCP",
            Version = "1.0.0",
        };
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();

namespace Enterprise.AI.Middleware.TestMcpServer
{
    /// <summary>
    /// Provides deterministic tools used by the integration tests to exercise MCP tool tracking.
    /// </summary>
    [McpServerToolType]
    public static class TestTools
    {
        /// <summary>
        /// Echoes the provided message back to the caller.
        /// </summary>
        /// <param name="message">The message to echo.</param>
        /// <returns>The echoed message.</returns>
        [McpServerTool, Description("Echoes the provided message back to the caller.")]
        public static string Echo([Description("The message to echo.")] string message)
        {
            return $"Echo: {message}";
        }

        /// <summary>
        /// Adds two integers and returns the sum.
        /// </summary>
        /// <param name="a">The first addend.</param>
        /// <param name="b">The second addend.</param>
        /// <returns>The sum of <paramref name="a"/> and <paramref name="b"/>.</returns>
        [McpServerTool, Description("Adds two integers and returns the sum.")]
        public static int Add(
            [Description("The first addend.")] int a,
            [Description("The second addend.")] int b)
        {
            return a + b;
        }
    }
}
