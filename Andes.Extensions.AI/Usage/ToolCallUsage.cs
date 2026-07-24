using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI;

/// <summary>
/// Records a single tool invocation within a tracked request, including any token usage
/// attributed to it and any nested tool calls it produced.
/// </summary>
public sealed class ToolCallUsage
{
    /// <summary>
    /// Gets the function call identifier assigned by the model, if available.
    /// </summary>
    public string? CallId { get; init; }

    /// <summary>
    /// Gets the name of the invoked tool.
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Gets the category of the invoked tool.
    /// </summary>
    public ToolKind Kind { get; init; }

    /// <summary>
    /// Gets the origin of the tool, such as an MCP server or agent name, if any.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Gets the token usage attributed to this invocation, rolled up from usage reported inside the
    /// tool (via <see cref="ChatProgress.ReportUsage"/> or a nested tracked pipeline) plus all
    /// <see cref="Children"/>. <see langword="null"/> when no usage was attributed.
    /// </summary>
    public UsageDetails? Usage { get; init; }

    /// <summary>
    /// Gets the tool invocations nested inside this one (for example, tools run by an agent exposed as a tool).
    /// </summary>
    public IReadOnlyList<ToolCallUsage> Children { get; init; } = [];

    /// <summary>
    /// Gets the wall-clock duration of the invocation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets a value indicating whether the invocation completed without throwing.
    /// </summary>
    public bool Succeeded { get; init; }
}
