using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI;

/// <summary>
/// Provides the ambient progress and usage reporter available to tool implementations running
/// inside a tracked chat request, without requiring any change to the tool's signature.
/// </summary>
/// <remarks>
/// All members are safe no-ops when no tracked request is active on the current async flow,
/// so tools remain usable outside of a <see cref="ToolTrackingChatClient"/> pipeline.
/// </remarks>
public static class ChatProgress
{
    /// <summary>
    /// Gets a value indicating whether a tracked chat request is active on the current async flow.
    /// </summary>
    public static bool IsActive => AmbientScope.Current is not null;

    /// <summary>
    /// Gets a reporter bound to the current tracking scope, or a no-op reporter when no tracked
    /// request is active. Useful for services that prefer passing a reporter over calling statics.
    /// </summary>
    public static IChatProgressReporter Current =>
        AmbientScope.Current is { } scope ? new ScopeReporter(scope) : NoopReporter.Instance;

    /// <summary>
    /// Reports a sub-status message displayed beneath the currently executing tool's header
    /// (for example, "Extracting…").
    /// </summary>
    /// <param name="status">The status text. Must not carry prompt content or tool results.</param>
    /// <exception cref="ArgumentNullException"><paramref name="status"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="status"/> is empty.</exception>
    public static void Report(string status)
    {
        ArgumentException.ThrowIfNullOrEmpty(status);

        if (AmbientScope.Current is { } scope)
        {
            scope.Tracker.ReportToolProgress(scope, status);
        }
    }

    /// <summary>
    /// Reports a sub-status message with numeric progress values that consumers can render as a
    /// progress indicator (for example, "step 2 of 5" with <c>2</c> and <c>5</c>).
    /// </summary>
    /// <param name="status">The status text. Must not carry prompt content or tool results.</param>
    /// <param name="progress">The amount of work completed so far, if known.</param>
    /// <param name="progressTotal">The total amount of work required, if known.</param>
    /// <exception cref="ArgumentNullException"><paramref name="status"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="status"/> is empty.</exception>
    public static void Report(string status, double? progress, double? progressTotal = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(status);

        if (AmbientScope.Current is { } scope)
        {
            scope.Tracker.ReportToolProgress(scope, status, progress, progressTotal);
        }
    }

    /// <summary>
    /// Attributes token usage (for example, from a nested SDK or agent call made inside the tool)
    /// to the currently executing tool's scope.
    /// </summary>
    /// <param name="usage">The usage to attribute.</param>
    /// <exception cref="ArgumentNullException"><paramref name="usage"/> is <see langword="null"/>.</exception>
    public static void ReportUsage(UsageDetails usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        if (AmbientScope.Current is { } scope)
        {
            RequestTracker.ReportToolUsage(scope, usage);
        }
    }

    private sealed class ScopeReporter(ToolScope scope) : IChatProgressReporter
    {
        public bool IsActive => true;

        public void Report(string status)
        {
            ArgumentException.ThrowIfNullOrEmpty(status);
            scope.Tracker.ReportToolProgress(scope, status);
        }

        public void Report(string status, double? progress, double? progressTotal)
        {
            ArgumentException.ThrowIfNullOrEmpty(status);
            scope.Tracker.ReportToolProgress(scope, status, progress, progressTotal);
        }

        public void ReportUsage(UsageDetails usage)
        {
            ArgumentNullException.ThrowIfNull(usage);
            RequestTracker.ReportToolUsage(scope, usage);
        }
    }

    private sealed class NoopReporter : IChatProgressReporter
    {
        public static readonly NoopReporter Instance = new();

        public bool IsActive => false;

        public void Report(string status)
        {
        }

        public void Report(string status, double? progress, double? progressTotal)
        {
        }

        public void ReportUsage(UsageDetails usage)
        {
        }
    }
}
