namespace Enterprise.AI.Middleware.Tracking;

/// <summary>
/// Identifies the lifecycle stage represented by a <see cref="ChatActivityEvent"/>.
/// </summary>
public enum ChatActivityEventKind
{
    /// <summary>
    /// A top-level chat request started.
    /// </summary>
    RequestStarted = 0,

    /// <summary>
    /// A tool invocation started.
    /// </summary>
    ToolCallStarted = 1,

    /// <summary>
    /// A tool invocation completed successfully.
    /// </summary>
    ToolCallCompleted = 2,

    /// <summary>
    /// A tool invocation threw an exception.
    /// </summary>
    ToolCallFailed = 3,

    /// <summary>
    /// Token usage was reported by the underlying provider for the current scope.
    /// </summary>
    UsageReported = 4,

    /// <summary>
    /// The top-level chat request completed successfully.
    /// </summary>
    RequestCompleted = 5,

    /// <summary>
    /// The top-level chat request terminated with an exception or cancellation.
    /// </summary>
    RequestFailed = 6,
}
