using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
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
        [McpServerTool(Name = "echo"), Description("Echoes the provided message back to the caller.")]
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
        [McpServerTool(Name = "add"), Description("Adds two integers and returns the sum.")]
        public static int Add(
            [Description("The first addend.")] int a,
            [Description("The second addend.")] int b)
        {
            return a + b;
        }

        /// <summary>
        /// Counts to the requested number of steps, reporting progress after each step.
        /// </summary>
        /// <param name="steps">The number of steps to count.</param>
        /// <param name="progress">Receives a progress notification per step.</param>
        /// <returns>A confirmation message.</returns>
        [McpServerTool(Name = "count_down"), Description("Counts to the requested number of steps, reporting progress along the way.")]
        public static async Task<string> CountDown(
            [Description("The number of steps to count.")] int steps,
            IProgress<ProgressNotificationValue> progress)
        {
            for (int i = 1; i <= steps; i++)
            {
                progress.Report(new ProgressNotificationValue
                {
                    Progress = i,
                    Total = steps,
                    Message = $"step {i} of {steps}",
                });
                await Task.Delay(200);
            }

            return $"Counted {steps} steps.";
        }
    }
}
