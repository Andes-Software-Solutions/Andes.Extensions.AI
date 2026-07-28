namespace Andes.Extensions.AI;

/// <summary>
/// One activity the assistant performed while producing its answer — a function, an MCP tool, or an
/// agent — rendered as a card. Activities nest: an agent's own tools appear in <see cref="Children"/>.
/// </summary>
/// <remarks>
/// The card label is <see cref="DisplayName"/> (the clean name only) plus <see cref="Kind"/> as a
/// separate badge. The contract deliberately never carries a pre-composed header such as
/// "Calling {name} MCP", so the kind word is never repeated and the label localizes cleanly.
/// </remarks>
public sealed record AssistantActivity
{
    /// <summary>
    /// Gets the identifier of the tracked scope this activity corresponds to, stable across all of
    /// its sub-statuses and lifecycle events.
    /// </summary>
    public required string ScopeId { get; init; }

    /// <summary>
    /// Gets the clean display name of the activity — the function, MCP server, or agent name — with
    /// no "Calling" prefix and no kind word appended (for example "GetForecast", "Andes Test MCP",
    /// or "Research Agent").
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the category of the activity, for a badge or icon.
    /// </summary>
    public ToolKind Kind { get; init; }

    /// <summary>
    /// Gets the origin of the activity — an MCP server name or agent identifier — when applicable.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Gets whether the activity is still running, completed, or failed.
    /// </summary>
    public ActivityState State { get; init; } = ActivityState.Running;

    /// <summary>
    /// Gets the elapsed time in seconds, set when the activity completes or fails.
    /// </summary>
    public double? DurationSeconds { get; init; }

    /// <summary>
    /// Gets the sub-status lines reported while the activity ran, oldest first.
    /// </summary>
    public IReadOnlyList<SubStatus> SubStatuses { get; init; } = [];

    /// <summary>
    /// Gets the activities invoked inside this one — for example an agent's own tools.
    /// </summary>
    public IReadOnlyList<AssistantActivity> Children { get; init; } = [];

    /// <summary>
    /// Gets the token usage attributed to this activity (including its children), when known.
    /// </summary>
    public UsageSummary? Usage { get; init; }
}
