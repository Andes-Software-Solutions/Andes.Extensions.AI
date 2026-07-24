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
    /// A tool provided by a Model Context Protocol (MCP) server. First-class classification and
    /// progress bridging ship in the <c>Andes.Extensions.AI.Mcp</c> package.
    /// </summary>
    McpTool = 2,

    /// <summary>
    /// A tool backed by an agent exposed as a function. First-class classification and usage
    /// capture ship in the <c>Andes.Extensions.AI.Agent</c> package.
    /// </summary>
    Agent = 3,
}
