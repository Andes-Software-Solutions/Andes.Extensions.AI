using Enterprise.AI.Middleware.Tracking.Internal;

namespace Enterprise.AI.Middleware.Tracking;

/// <summary>
/// Provides ambient access to the activity scope of the chat request currently executing on the
/// asynchronous call path.
/// </summary>
public static class ChatActivityScope
{
    private static readonly AsyncLocal<ActivityFlowState?> _currentFlow = new();

    /// <summary>
    /// Gets the activity scope for the current asynchronous flow, or <see langword="null"/> when
    /// no tracked chat request is executing.
    /// </summary>
    /// <remarks>
    /// The value flows across <see langword="await"/> boundaries. Inside a tool invocation it is
    /// the tool-call scope; inside a nested agent's model call it is that nested scope.
    /// </remarks>
    public static ActivityScope? Current
    {
        get
        {
            ActivityFlowState? flow = _currentFlow.Value;
            return flow?.Scope;
        }
    }

    /// <summary>
    /// Publishes a custom status line from inside a tool to the current tracked request as a
    /// <see cref="ChatActivityEventKind.StatusReported"/> event.
    /// </summary>
    /// <param name="message">The status text, rendered as the update's subheader.</param>
    /// <param name="progress">The numeric progress reported so far (a percentage or completed-item count), or <see langword="null"/>.</param>
    /// <param name="progressTotal">The total progress required, or <see langword="null"/> when unknown.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The event always reaches the <see cref="IChatActivityObserver"/>; it additionally appears
    /// in-band in the root request's response stream when the root request is streaming and
    /// <see cref="ToolTrackingOptions.EnableInBandStatusUpdates"/> is enabled.
    /// </para>
    /// <para>
    /// The header is derived from the executing scope, so a tool running under an agent renders
    /// under <c>"Calling {Agent} Agent"</c> and an MCP tool under <c>"Calling {Server} MCP"</c>.
    /// When no tracked request is ambient the call is a no-op, so tools remain usable outside
    /// tracked pipelines.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// AIFunction tool = AIFunctionFactory.Create(async (string query) =>
    /// {
    ///     ChatActivityScope.ReportStatus("Searching archives", progress: 2, progressTotal: 5);
    ///     return await SearchAsync(query);
    /// }, "search_archives");
    /// </code>
    /// </example>
    public static void ReportStatus(string message, double? progress = null, double? progressTotal = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_currentFlow.Value is { } flow)
        {
            flow.Context.PublishStatus(flow.Scope, message, progress, progressTotal);
        }
    }

    /// <summary>
    /// Gets or sets the full ambient flow state (scope plus owning context) for internal use.
    /// </summary>
    internal static ActivityFlowState? CurrentFlow
    {
        get => _currentFlow.Value;
        set => _currentFlow.Value = value;
    }
}
