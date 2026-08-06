namespace Andes.Extensions.AI;

/// <summary>
/// A single, flat, serialization-friendly delta that an API flushes to a UI on each stream event.
/// The <see cref="Kind"/> discriminator tells the UI which fields are meaningful, so the same shape
/// maps cleanly to a TypeScript discriminated union.
/// </summary>
/// <remarks>
/// Fold a sequence of these into an <see cref="AssistantStatusSnapshot"/> with
/// <see cref="AssistantStatusReducer"/>, or consume them directly. Project them from a tracked chat
/// stream with <c>ChatResponseUiExtensions.ToUiEventsAsync</c>. Like the core progress contract,
/// events never carry prompt content, tool arguments, or tool results. Reasoning summary text
/// appears only on <see cref="AssistantUiEventKind.ReasoningDelta"/> events, sourced from in-band
/// model content — never from progress metadata.
/// </remarks>
public sealed record AssistantUiEvent
{
    /// <summary>
    /// Gets what this event represents.
    /// </summary>
    public required AssistantUiEventKind Kind { get; init; }

    /// <summary>
    /// Gets the message text for the event: the request status line for
    /// <see cref="AssistantUiEventKind.Status"/>, or the sub-status for
    /// <see cref="AssistantUiEventKind.ActivityProgress"/>.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets the identifier of the activity scope this event belongs to, when it targets an activity.
    /// </summary>
    public string? ScopeId { get; init; }

    /// <summary>
    /// Gets the identifier of the parent scope, or <see langword="null"/> when the activity is
    /// top-level (its parent is the request root, which has no card).
    /// </summary>
    public string? ParentScopeId { get; init; }

    /// <summary>
    /// Gets the display depth: 0 for request-level events, 1 for a top-level activity, 2 for a
    /// sub-status, and deeper for nested activities.
    /// </summary>
    public int Depth { get; init; }

    /// <summary>
    /// Gets the category of the activity this event targets, when applicable.
    /// </summary>
    public ToolKind ToolKind { get; init; }

    /// <summary>
    /// Gets the clean display name of the activity (no "Calling" prefix, no kind word appended),
    /// when the event targets an activity.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Gets the origin of the activity — an MCP server name or agent identifier — when applicable.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Gets the numeric progress (the numerator) for an
    /// <see cref="AssistantUiEventKind.ActivityProgress"/> event, when supplied.
    /// </summary>
    public double? Progress { get; init; }

    /// <summary>
    /// Gets the total amount of work — the denominator for <see cref="Progress"/> — when known.
    /// </summary>
    public double? ProgressTotal { get; init; }

    /// <summary>
    /// Gets the elapsed time in seconds for a completion event, or the request duration for
    /// <see cref="AssistantUiEventKind.Finished"/>.
    /// </summary>
    public double? DurationSeconds { get; init; }

    /// <summary>
    /// Gets the answer text chunk for a <see cref="AssistantUiEventKind.TextDelta"/> event, or the
    /// reasoning summary chunk for a <see cref="AssistantUiEventKind.ReasoningDelta"/> event.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Gets the token usage for a <see cref="AssistantUiEventKind.Finished"/> event.
    /// </summary>
    public UsageSummary? Usage { get; init; }

    /// <summary>
    /// Gets the time at which the underlying progress event was raised. Only meaningful for
    /// status and activity events; <see cref="AssistantUiEventKind.TextDelta"/>,
    /// <see cref="AssistantUiEventKind.ReasoningDelta"/>, and
    /// <see cref="AssistantUiEventKind.Finished"/> events, which have no source progress event,
    /// leave it at its default. Consume events in stream order rather than sorting by this value.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }
}
