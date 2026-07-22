namespace Enterprise.AI.Middleware.Tracking;

/// <summary>
/// Receives out-of-band activity notifications from the tool-tracking middleware, independent of
/// the chat response stream. Implement this to feed dashboards, SignalR hubs, or SSE endpoints.
/// </summary>
/// <remarks>
/// <para>
/// Implementations are invoked synchronously by the middleware and must therefore be fast and
/// non-blocking. Exceptions thrown by an implementation are caught, logged, and never affect the
/// chat request.
/// </para>
/// <para>
/// Most events are raised on the request's execution path in the order the activities occurred.
/// The exception is <see cref="ChatActivityEventKind.StatusReported"/> events bridged from MCP
/// progress notifications: those are dispatched from the MCP client's receive loop — on a
/// different thread, concurrently with request-path events, and in no guaranteed order relative
/// to them. Implementations must be thread-safe. No event is delivered after
/// <see cref="OnRequestCompleted"/>; late MCP notifications are dropped.
/// </para>
/// </remarks>
public interface IChatActivityObserver
{
    /// <summary>
    /// Receives each activity event; see the type remarks for threading and ordering guarantees.
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
