using Enterprise.AI.Middleware.Tracking.Internal;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace Enterprise.AI.Middleware.Tracking;

/// <summary>
/// Annotates MCP tools with the server display name shown in activity status updates
/// (for example <c>"Calling GitHub MCP"</c>).
/// </summary>
/// <remarks>
/// <see cref="McpClientTool"/> does not expose its owning client or server name publicly, so the
/// tool-tracking middleware cannot discover the server name on its own. Apply one of these
/// helpers when registering MCP tools; unannotated MCP tools are still detected but fall back to
/// <see cref="ToolTrackingOptions.DefaultMcpServerName"/>.
/// </remarks>
public static class McpToolTrackingExtensions
{
    /// <summary>
    /// Annotates an MCP tool with the server name taken from the client's
    /// <see cref="McpClient.ServerInfo"/>.
    /// </summary>
    /// <param name="tool">The MCP tool to annotate.</param>
    /// <param name="client">The client connected to the server that exposes the tool.</param>
    /// <returns>An annotated function that invokes the original tool unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tool"/> or <paramref name="client"/> is <see langword="null"/>.</exception>
    public static AIFunction WithTrackingMetadata(this McpClientTool tool, McpClient client)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(client);

        return tool.WithTrackingMetadata(GetServerDisplayName(client));
    }

    /// <summary>
    /// Annotates an MCP tool with an explicit server display name.
    /// </summary>
    /// <param name="tool">The MCP tool to annotate.</param>
    /// <param name="serverName">The display name used in status headers.</param>
    /// <returns>An annotated function that invokes the original tool unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tool"/> or <paramref name="serverName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="serverName"/> is empty.</exception>
    public static AIFunction WithTrackingMetadata(this McpClientTool tool, string serverName)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentException.ThrowIfNullOrEmpty(serverName);

        return new AnnotatedAIFunction(tool, ToolKind.Mcp, serverName);
    }

    /// <summary>
    /// Annotates a set of MCP tools with the server name taken from the client's
    /// <see cref="McpClient.ServerInfo"/>, ready to assign to
    /// <see cref="ChatOptions.Tools"/>.
    /// </summary>
    /// <param name="tools">The MCP tools to annotate, typically from <c>ListToolsAsync</c>.</param>
    /// <param name="client">The client connected to the server that exposes the tools.</param>
    /// <returns>Annotated functions that invoke the original tools unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tools"/> or <paramref name="client"/> is <see langword="null"/>.</exception>
    public static IList<AITool> WithTrackingMetadata(this IEnumerable<McpClientTool> tools, McpClient client)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(client);

        string serverName = GetServerDisplayName(client);
        var annotated = new List<AITool>();
        foreach (McpClientTool tool in tools)
        {
            annotated.Add(tool.WithTrackingMetadata(serverName));
        }

        return annotated;
    }

    private static string GetServerDisplayName(McpClient client)
    {
        return client.ServerInfo.Title ?? client.ServerInfo.Name;
    }
}
