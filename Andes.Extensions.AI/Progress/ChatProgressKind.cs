namespace Andes.Extensions.AI;

/// <summary>
/// Identifies the kind of progress event raised while a tracked chat request executes.
/// </summary>
public enum ChatProgressKind
{
    /// <summary>
    /// The tracked request has started and no work has been forwarded to the inner client yet.
    /// </summary>
    RequestStarted = 0,

    /// <summary>
    /// A model turn is beginning; emitted before the first inner call and again after each tool round-trip.
    /// </summary>
    Thinking = 1,

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
