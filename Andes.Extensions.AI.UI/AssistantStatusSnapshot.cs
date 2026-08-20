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
    /// Gets the current request-level status line, such as "Reasoning…", or <see langword="null"/>
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
    /// Gets the model's reasoning summary text accumulated so far, when the provider streams
    /// reasoning content (for example the OpenAI Responses API); otherwise <see langword="null"/>.
    /// Deltas accumulate verbatim across the whole request, including across tool round-trips.
    /// </summary>
    public string? ReasoningText { get; init; }

    /// <summary>
    /// Gets the total token usage for the request, set once it finishes.
    /// </summary>
    public UsageSummary? Usage { get; init; }

    /// <summary>
    /// Gets application-supplied values attached by the API that produced it; the package neither
    /// reads nor interprets them. When the snapshot comes from <see cref="AssistantStatusReducer"/>
    /// (or the TypeScript <c>foldAssistantEvents</c>), this accumulates every folded event's
    /// <see cref="AssistantUiEvent.Metadata"/> regardless of kind, last write per key winning.
    /// Accumulated keys are compared ordinally regardless of the comparer on the supplied
    /// dictionary, matching the TypeScript fold's object-key semantics. The built-in projections
    /// (<c>ChatResponseUiExtensions.ToStatusSnapshotsAsync</c> and <c>ToSnapshot</c>) always leave
    /// it <see langword="null"/> — values appear only when the application attaches them to the
    /// events it folds.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
