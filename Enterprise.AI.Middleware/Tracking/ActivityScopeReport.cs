using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tracking;

/// <summary>
/// Describes one node of the activity scope tree produced for a chat request: the root request,
/// a tool invocation, or a nested model call made by an agent tool.
/// </summary>
public sealed class ActivityScopeReport
{
    /// <summary>
    /// Gets the unique identifier of this scope within the request.
    /// </summary>
    public required string ScopeId { get; init; }

    /// <summary>
    /// Gets the identifier of the parent scope, or <see langword="null"/> for the root scope.
    /// </summary>
    public string? ParentScopeId { get; init; }

    /// <summary>
    /// Gets the kind of tool this scope represents, or <see langword="null"/> for the root
    /// request scope and nested model-call scopes.
    /// </summary>
    public ToolKind? ToolKind { get; init; }

    /// <summary>
    /// Gets the name of the tool this scope represents, or <see langword="null"/> when the
    /// scope is not a tool invocation.
    /// </summary>
    public string? ToolName { get; init; }

    /// <summary>
    /// Gets the display name of the tool's source — the agent name for agent tools or the MCP
    /// server name for MCP tools — or <see langword="null"/> otherwise.
    /// </summary>
    public string? SourceName { get; init; }

    /// <summary>
    /// Gets the elapsed wall-clock duration of this scope.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets the token usage recorded directly in this scope, excluding descendants.
    /// </summary>
    public required UsageDetails OwnUsage { get; init; }

    /// <summary>
    /// Gets the token usage of this scope including all descendant scopes.
    /// </summary>
    public required UsageDetails TotalUsage { get; init; }

    /// <summary>
    /// Gets the child scopes, in start order.
    /// </summary>
    public required IReadOnlyList<ActivityScopeReport> Children { get; init; }
}
