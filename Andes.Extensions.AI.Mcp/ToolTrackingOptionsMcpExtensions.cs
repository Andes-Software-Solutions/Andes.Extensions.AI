using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace Andes.Extensions.AI;

/// <summary>
/// Adds MCP tool classification to <see cref="ToolTrackingOptions"/> so MCP tools render with
/// the <c>"Calling {Server} MCP"</c> header and report <see cref="ToolKind.McpTool"/> in usage reports.
/// </summary>
public static class ToolTrackingOptionsMcpExtensions
{
    /// <summary>
    /// Installs a <see cref="ToolTrackingOptions.ToolClassifier"/> that recognizes
    /// <see cref="McpClientTool"/> instances (raw, or wrapped by
    /// <see cref="McpToolTrackingExtensions.WithTracking(McpClientTool, McpClient, bool)"/>, or
    /// inside user <see cref="DelegatingAIFunction"/> chains) as <see cref="ToolKind.McpTool"/>.
    /// </summary>
    /// <remarks>
    /// The server name shown in headers is resolved in precedence order: the name carried by
    /// <see cref="McpToolTrackingExtensions.WithTracking(McpClientTool, McpClient, bool)"/>, then
    /// <paramref name="serverNameResolver"/>, then <paramref name="defaultServerName"/>.
    /// Tools that are not MCP tools are delegated to the classifier configured before this call,
    /// or classified with <see cref="ToolDescriptor.CreateDefault"/> when there is none.
    /// </remarks>
    /// <param name="options">The options to configure.</param>
    /// <param name="defaultServerName">The server name used when no other source provides one.</param>
    /// <param name="serverNameResolver">
    /// An optional callback resolving the server name for MCP tools that were not wrapped by
    /// <see cref="McpToolTrackingExtensions.WithTracking(McpClientTool, McpClient, bool)"/>.
    /// Return <see langword="null"/>, empty, or whitespace to fall back to
    /// <paramref name="defaultServerName"/>.
    /// </param>
    /// <returns>The same <paramref name="options"/> instance, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or <paramref name="defaultServerName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="defaultServerName"/> is empty.</exception>
    /// <example>
    /// <code language="csharp">
    /// IChatClient client = innerClient
    ///     .AsBuilder()
    ///     .UseToolTracking(options => options.UseMcpToolClassification())
    ///     .UseFunctionInvocation()
    ///     .Build();
    /// </code>
    /// </example>
    public static ToolTrackingOptions UseMcpToolClassification(
        this ToolTrackingOptions options,
        string defaultServerName = "MCP",
        Func<AIFunction, string?>? serverNameResolver = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(defaultServerName);

        Func<AITool, ToolDescriptor>? previous = options.ToolClassifier;
        options.ToolClassifier = tool =>
        {
            if (tool is AIFunction function && function.GetService<McpClientTool>() is not null)
            {
                string source;
                if (function.GetService<McpTrackingAIFunction>() is { } tracked)
                {
                    source = tracked.ServerName;
                }
                else
                {
                    string? resolved = serverNameResolver?.Invoke(function);
                    source = string.IsNullOrWhiteSpace(resolved) ? defaultServerName : resolved;
                }

                return new ToolDescriptor
                {
                    Name = tool.Name,
                    Kind = ToolKind.McpTool,
                    Source = source,
                };
            }

            // Preserve a previous classifier's null result so the core null guard still fires.
            return previous is not null ? previous(tool) : ToolDescriptor.CreateDefault(tool);
        };
        return options;
    }
}
