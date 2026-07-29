namespace Andes.Extensions.AI;

/// <summary>
/// An immutable snapshot of everything the assistant is doing at one point in a streamed request:
/// the top-level status line, the hierarchy of activity cards, any answer text so far, and the
/// final token usage. A UI binds to this and re-renders whenever a new snapshot arrives.
/// </summary>
/// <remarks>
/// Produce snapshots with <see cref="AssistantStatusReducer"/> (folding
/// <see cref="AssistantUiEvent"/>s) or with <c>ChatResponseUiExtensions.ToStatusSnapshotsAsync</c>.
/// The type is serialization-friendly via <see cref="AssistantUiJsonContext"/>.
/// </remarks>
public sealed record AssistantStatusSnapshot
{
    /// <summary>
    /// Gets the current request-level status line, such as "Thinking…", or <see langword="null"/>
    /// before the first status arrives.
    /// </summary>
    public string? AssistantStatus { get; init; }

    /// <summary>
    /// Gets the overall state of the request.
    /// </summary>
    public ActivityState Phase { get; init; } = ActivityState.Running;

    /// <summary>
    /// Gets the top-level activity cards, one per activity the assistant started, in order.
    /// </summary>
    public IReadOnlyList<AssistantActivity> Activities { get; init; } = [];

    /// <summary>
    /// Gets the assistant's answer text accumulated so far, when any has streamed.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Gets the total token usage for the request, set once it finishes.
    /// </summary>
    public UsageSummary? Usage { get; init; }
}
