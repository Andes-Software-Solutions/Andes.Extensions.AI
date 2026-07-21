namespace Enterprise.AI.Middleware.Tracking;

/// <summary>
/// Receives out-of-band activity notifications from the tool-tracking middleware, independent of
/// the chat response stream. Implement this to feed dashboards, SignalR hubs, or SSE endpoints.
/// </summary>
/// <remarks>
/// Implementations are invoked synchronously on the request's execution path and must therefore
/// be fast and non-blocking. Exceptions thrown by an implementation are caught, logged, and never
/// affect the chat request.
/// </remarks>
public interface IChatActivityObserver
{
    /// <summary>
    /// Receives each activity event in the order the activity occurred.
    /// </summary>
    /// <param name="activityEvent">The event that occurred.</param>
    void OnActivityEvent(ChatActivityEvent activityEvent);

    /// <summary>
    /// Receives the final aggregated report exactly once per top-level request, after the request
    /// completed, failed, or was canceled.
    /// </summary>
    /// <param name="report">The aggregated activity report for the request.</param>
    void OnRequestCompleted(ChatActivityReport report);
}
