namespace Andes.Extensions.AI;

/// <summary>
/// Identifies the kind of progress event raised while a tracked chat request executes.
/// </summary>
public enum ChatProgressKind
{
    /// <summary>
    /// A developer-constructed status carrying an application-supplied message. Never emitted by
    /// the middleware; construct one with <see cref="ChatProgressUpdate.CreateCustom(string)"/> to
    /// announce your own request-level status outside the tracked pipeline.
    /// </summary>
    Custom = 0,

    /// <summary>
    /// The model is producing reasoning output. Emitted once per model turn when reasoning content
    /// (<see cref="Microsoft.Extensions.AI.TextReasoningContent"/>) is detected on the response —
    /// for example from the OpenAI Responses API. The event never carries the reasoning text
    /// itself; the library provides no factory for it — the middleware raises it when it detects
    /// reasoning content.
    /// </summary>
    Reasoning = 1,

    /// <summary>
    /// A tool invocation is starting; the message carries the display header (for example, "Calling GetWeather Tool").
    /// </summary>
    ToolInvoking = 2,

    /// <summary>
    /// A sub-status reported from inside a tool implementation (for example, "Extracting…").
    /// </summary>
    ToolProgress = 3,

    /// <summary>
    /// A tool invocation completed successfully.
    /// </summary>
    ToolCompleted = 4,

    /// <summary>
    /// A tool invocation threw an exception.
    /// </summary>
    ToolFailed = 5,

    /// <summary>
    /// The tracked request finished and the final usage report is available.
    /// </summary>
    RequestCompleted = 6,

    /// <summary>
    /// The tracked request faulted or was canceled before completing. Raised out-of-band to
    /// observers only; the accompanying usage report may be partial.
    /// </summary>
    RequestFailed = 7,

    /// <summary>
    /// The model finished producing reasoning output for the current model turn — raised when the
    /// first answer text or function call follows detected reasoning, or when the stream ends.
    /// Emitted at most once per turn (a later reasoning burst in the same turn is not re-announced);
    /// carries the elapsed reasoning time in <see cref="ChatProgressUpdate.Duration"/> when the
    /// request streams, and never the reasoning text itself.
    /// </summary>
    ReasoningCompleted = 8,
}
