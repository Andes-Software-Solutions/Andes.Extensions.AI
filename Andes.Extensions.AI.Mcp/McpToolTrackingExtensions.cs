using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace Andes.Extensions.AI;

/// <summary>
/// Wraps MCP tools for first-class tool tracking: the wrapper carries the server display name
/// shown in progress headers (for example <c>"Calling GitHub MCP"</c>) and bridges the server's
/// progress notifications into <see cref="ChatProgressKind.ToolProgress"/> updates.
/// </summary>
/// <remarks>
/// <see cref="McpClientTool"/> does not expose its owning client or server name publicly, so the
/// tool-tracking middleware cannot discover the server name on its own. Apply one of these
/// helpers when registering MCP tools; unwrapped MCP tools are still detected by
/// <see cref="ToolTrackingOptionsMcpExtensions.UseMcpToolClassification"/> but fall back to its
/// default server name and receive no progress bridge.
/// <para>
/// During tracked invocations the progress bridge supersedes any handler previously attached via
/// <see cref="McpClientTool.WithProgress(IProgress{ProgressNotificationValue})"/> — the MCP SDK
/// supports only one progress handler per invocation. Pass <c>enableProgress: false</c> to keep
/// a handler you attached yourself active.
/// </para>
/// </remarks>
public static class McpToolTrackingExtensions
{
    /// <summary>
    /// Wraps an MCP tool for tracking, taking the server display name from the client's
    /// <see cref="McpClient.ServerInfo"/>.
    /// </summary>
    /// <param name="tool">The MCP tool to wrap.</param>
    /// <param name="client">The client connected to the server that exposes the tool.</param>
    /// <param name="enableProgress">
    /// <see langword="true"/> to bridge the server's progress notifications into chat progress
    /// updates during tracked invocations (superseding any handler attached via
    /// <see cref="McpClientTool.WithProgress(IProgress{ProgressNotificationValue})"/>);
    /// <see langword="false"/> to only carry the server name.
    /// </param>
    /// <returns>A wrapped function that invokes the original tool unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tool"/> or <paramref name="client"/> is <see langword="null"/>.</exception>
    public static AIFunction WithTracking(this McpClientTool tool, McpClient client, bool enableProgress = true)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(client);

        return tool.WithTracking(GetServerDisplayName(client), enableProgress);
    }

    /// <summary>
    /// Wraps an MCP tool for tracking with an explicit server display name.
    /// </summary>
    /// <param name="tool">The MCP tool to wrap.</param>
    /// <param name="serverName">The display name used in progress headers.</param>
    /// <param name="enableProgress">
    /// <see langword="true"/> to bridge the server's progress notifications into chat progress
    /// updates during tracked invocations (superseding any handler attached via
    /// <see cref="McpClientTool.WithProgress(IProgress{ProgressNotificationValue})"/>);
    /// <see langword="false"/> to only carry the server name.
    /// </param>
    /// <returns>A wrapped function that invokes the original tool unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tool"/> or <paramref name="serverName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="serverName"/> is empty.</exception>
    public static AIFunction WithTracking(this McpClientTool tool, string serverName, bool enableProgress = true)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentException.ThrowIfNullOrEmpty(serverName);

        return new McpTrackingAIFunction(tool, serverName, enableProgress);
    }

    /// <summary>
    /// Wraps a set of MCP tools for tracking, taking the server display name from the client's
    /// <see cref="McpClient.ServerInfo"/>, ready to assign to <see cref="ChatOptions.Tools"/>.
    /// </summary>
    /// <param name="tools">The MCP tools to wrap, typically from <c>ListToolsAsync</c>.</param>
    /// <param name="client">The client connected to the server that exposes the tools.</param>
    /// <param name="enableProgress">
    /// <see langword="true"/> to bridge the servers' progress notifications into chat progress
    /// updates during tracked invocations (superseding any handlers attached via
    /// <see cref="McpClientTool.WithProgress(IProgress{ProgressNotificationValue})"/>);
    /// <see langword="false"/> to only carry the server name.
    /// </param>
    /// <returns>Wrapped functions that invoke the original tools unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tools"/> or <paramref name="client"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code language="csharp">
    /// McpClient mcpClient = await McpClient.CreateAsync(transport);
    /// IList&lt;McpClientTool&gt; mcpTools = await mcpClient.ListToolsAsync();
    ///
    /// var options = new ChatOptions { Tools = mcpTools.WithTracking(mcpClient) };
    /// </code>
    /// </example>
    public static IList<AITool> WithTracking(this IEnumerable<McpClientTool> tools, McpClient client, bool enableProgress = true)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(client);

        string serverName = GetServerDisplayName(client);
        var wrapped = new List<AITool>();
        foreach (McpClientTool tool in tools)
        {
            wrapped.Add(tool.WithTracking(serverName, enableProgress));
        }

        return wrapped;
    }

    private static string GetServerDisplayName(McpClient client)
    {
        string? title = client.ServerInfo.Title;
        return string.IsNullOrEmpty(title) ? client.ServerInfo.Name : title;
    }
}
