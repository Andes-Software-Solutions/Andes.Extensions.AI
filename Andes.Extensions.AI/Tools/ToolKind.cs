namespace Andes.Extensions.AI;

/// <summary>
/// Identifies the category of a tool participating in a tracked chat request.
/// </summary>
public enum ToolKind
{
    /// <summary>
    /// The tool category could not be determined (for example, a tool declaration the pipeline cannot invoke).
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// A plain <see cref="Microsoft.Extensions.AI.AIFunction"/> tool.
    /// </summary>
    Function = 1,

    /// <summary>
    /// A tool provided by a Model Context Protocol (MCP) server. Reserved for a future release.
    /// </summary>
    McpTool = 2,

    /// <summary>
    /// A tool backed by an agent exposed as a function. Reserved for a future release.
    /// </summary>
    Agent = 3,
}
