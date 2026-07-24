namespace Andes.Extensions.AI;

/// <summary>
/// Receives out-of-band progress events and the final usage report for tracked chat requests,
/// independently of the streamed response.
/// </summary>
/// <remarks>
/// Observers are invoked synchronously on arbitrary threads while the request is executing.
/// Implementations must be fast and must not throw; exceptions escaping an observer are swallowed
/// so that a faulty observer cannot corrupt the response stream.
/// </remarks>
public interface IChatProgressObserver
{
    /// <summary>
    /// Handles a progress event raised during a tracked chat request.
    /// </summary>
    /// <param name="update">The progress event.</param>
    void OnProgress(ChatProgressUpdate update);

    /// <summary>
    /// Handles completion of a tracked chat request. Called exactly once per request, including
    /// when the request faults or is canceled — a <see cref="ChatProgressKind.RequestFailed"/>
    /// progress event precedes this call in that case, and the report may be partial.
    /// </summary>
    /// <param name="report">The aggregated usage report for the request.</param>
    void OnRequestCompleted(ChatUsageReport report);
}
