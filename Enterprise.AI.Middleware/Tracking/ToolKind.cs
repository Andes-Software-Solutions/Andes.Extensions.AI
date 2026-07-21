namespace Enterprise.AI.Middleware.Tracking;

/// <summary>
/// Identifies the kind of tool invoked by the assistant during a chat request.
/// </summary>
public enum ToolKind
{
    /// <summary>
    /// A plain <see cref="Microsoft.Extensions.AI.AIFunction"/> tool exposed directly to the model.
    /// </summary>
    Function = 0,

    /// <summary>
    /// A Microsoft Agent Framework agent exposed as a function tool
    /// (for example via <c>agent.AsAIFunction()</c>).
    /// </summary>
    Agent = 1,

    /// <summary>
    /// A tool exposed by a Model Context Protocol (MCP) server.
    /// </summary>
    Mcp = 2,
}
