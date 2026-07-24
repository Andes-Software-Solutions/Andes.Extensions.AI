using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI;

/// <summary>
/// Reports sub-statuses and token usage from inside a tool implementation to the
/// tracking scope that is executing the tool.
/// </summary>
/// <remarks>
/// Obtain an instance from <see cref="ChatProgress.Current"/>. When no tracked request is active,
/// the returned reporter is a no-op, so tool code can call it unconditionally.
/// </remarks>
public interface IChatProgressReporter
{
    /// <summary>
    /// Gets a value indicating whether the reporter is bound to an active tracking scope.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Reports a sub-status message displayed beneath the current tool's header
    /// (for example, "Extracting…").
    /// </summary>
    /// <param name="status">The status text. Must not carry prompt content or tool results.</param>
    void Report(string status);

    /// <summary>
    /// Reports a sub-status message with optional numeric progress values that consumers can
    /// render as a progress indicator.
    /// </summary>
    /// <remarks>
    /// The default implementation discards the numeric values and forwards to
    /// <see cref="Report(string)"/>, so existing implementations keep working unchanged.
    /// </remarks>
    /// <param name="status">The status text. Must not carry prompt content or tool results.</param>
    /// <param name="progress">The amount of work completed so far, if known.</param>
    /// <param name="progressTotal">The total amount of work required, if known.</param>
    void Report(string status, double? progress, double? progressTotal = null) => Report(status);

    /// <summary>
    /// Attributes token usage (for example, from a nested SDK or agent call made inside the tool)
    /// to the current tool-call scope.
    /// </summary>
    /// <param name="usage">The usage to attribute.</param>
    void ReportUsage(UsageDetails usage);
}
