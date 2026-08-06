namespace Andes.Extensions.AI;

/// <summary>
/// Identifies what a single <see cref="AssistantUiEvent"/> represents in the assistant's activity
/// stream, so a UI can react to each flush without inspecting the rest of the payload.
/// </summary>
public enum AssistantUiEventKind
{
    /// <summary>
    /// A request-level status line, such as "Reasoning…" — carried by <see cref="AssistantUiEvent.Message"/>.
    /// </summary>
    Status,

    /// <summary>
    /// An activity (a function, MCP tool, or agent) started; a new card should appear.
    /// </summary>
    ActivityStarted,

    /// <summary>
    /// A sub-status reported while an activity runs; append it beneath the activity's card.
    /// </summary>
    ActivityProgress,

    /// <summary>
    /// An activity finished successfully.
    /// </summary>
    ActivityCompleted,

    /// <summary>
    /// An activity failed.
    /// </summary>
    ActivityFailed,

    /// <summary>
    /// A chunk of the assistant's answer text, carried by <see cref="AssistantUiEvent.Text"/>.
    /// </summary>
    TextDelta,

    /// <summary>
    /// A chunk of the model's reasoning summary text, carried by <see cref="AssistantUiEvent.Text"/>.
    /// Sourced from in-band <see cref="Microsoft.Extensions.AI.TextReasoningContent"/> on the tracked
    /// stream; encrypted-only reasoning items (empty text) are never surfaced.
    /// </summary>
    ReasoningDelta,

    /// <summary>
    /// The request finished; the total token <see cref="AssistantUiEvent.Usage"/> is available.
    /// </summary>
    Finished,
}
