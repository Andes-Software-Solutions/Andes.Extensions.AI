namespace Andes.Extensions.AI;

/// <summary>
/// Identifies the kind of progress event raised while a tracked chat request executes.
/// </summary>
public enum ChatProgressKind
{
    /// <summary>
    /// A request is starting. Never emitted by the middleware; construct one with
    /// <see cref="ChatProgressUpdate.CreateRequestStarted(string?)"/> to announce your own
    /// request start outside the tracked pipeline.
    /// </summary>
    RequestStarted = 0,

    /// <summary>
    /// The model is producing reasoning output. Emitted once per model turn when reasoning content
    /// (<see cref="Microsoft.Extensions.AI.TextReasoningContent"/>) is detected on the response —
    /// for example from the OpenAI Responses API. The event never carries the reasoning text
    /// itself. Also constructible via <see cref="ChatProgressUpdate.CreateReasoning(string?)"/>
    /// for emission outside the middleware.
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
}
