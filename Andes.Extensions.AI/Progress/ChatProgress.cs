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
    /// Opens a nested tool-call scope on the ambient tracker, so that the operation renders as its
    /// own child activity beneath the currently executing tool and any usage reported inside it
    /// (via <see cref="ReportUsage"/>) is attributed to the child in the final <see cref="ChatUsageReport"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns an inactive handle (a safe no-op) when no tracked request is active, or when the
    /// ambient scope was already opened for <paramref name="owner"/> — the case where the tracking
    /// middleware wrapped the function itself — which prevents a duplicate scope for the same
    /// invocation. Nested scopes never carry tool arguments.
    /// </para>
    /// <para>
    /// This method is deliberately not exposed on <see cref="IChatProgressReporter"/>: a captured
    /// reporter may be invoked off the original async flow (for example, an MCP receive loop),
    /// where pushing an ambient scope would have no effect. Dispose the returned handle on the
    /// same async flow that opened it.
    /// </para>
    /// </remarks>
    /// <param name="descriptor">Describes how the nested activity is classified and displayed.
    /// The ambient tracker's <see cref="ToolTrackingOptions.HeaderFormatter"/> applies to the
    /// header; its <see cref="ToolTrackingOptions.ToolClassifier"/> does not.</param>
    /// <param name="owner">The function opening the scope, used to suppress a duplicate scope when
    /// the tracker already opened one for this invocation and to correlate the scope with the
    /// model's function call. Pass <see langword="null"/> to always open a scope.</param>
    /// <returns>A handle that completes the scope when disposed; never <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="descriptor"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code language="csharp">
    /// using ChatProgressToolScope scope = ChatProgress.BeginToolScope(
    ///     new ToolDescriptor { Name = "summarize", DisplayName = "Summarizer", Kind = ToolKind.Agent });
    /// try
    /// {
    ///     ChatProgress.Report("Condensing findings…");
    ///     // ... nested work ...
    /// }
    /// catch
    /// {
    ///     scope.Fail();
    ///     throw;
    /// }
    /// </code>
    /// </example>
    public static ChatProgressToolScope BeginToolScope(ToolDescriptor descriptor, AIFunction? owner = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (AmbientScope.Current is not { } ambient)
        {
            return ChatProgressToolScope.Inactive;
        }

        if (owner is not null && ambient.IsOwnedBy(owner))
        {
            return ChatProgressToolScope.Inactive;
        }

        // The function-invocation call id is trusted only when the current invocation context is
        // for the owner itself (a nested function-invoking loop); otherwise the context still
        // describes the enclosing tool's call and would mis-correlate the child.
        string? callId = null;
        if (owner is not null
            && FunctionInvokingChatClient.CurrentContext is { } context
            && ReferenceEquals(context.Function, owner))
        {
            callId = context.CallContent.CallId;
        }

        RequestTracker tracker = ambient.Tracker;
        ToolScope scope = tracker.BeginToolScope(descriptor, callId, ambient, arguments: null, owner);
        long startTimestamp = tracker.Options.TimeProvider.GetTimestamp();
        AmbientScope.ScopeRestorer restorer = AmbientScope.Push(scope);
        return new ChatProgressToolScope(scope, restorer, startTimestamp);
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
