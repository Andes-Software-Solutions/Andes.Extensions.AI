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
    /// Gets or sets the full ambient flow state (scope plus owning context) for internal use.
    /// </summary>
    internal static ActivityFlowState? CurrentFlow
    {
        get => _currentFlow.Value;
        set => _currentFlow.Value = value;
    }
}
