namespace Enterprise.AI.Middleware.Tracking.Internal;

/// <summary>
/// Identifies the structural role of an <see cref="ActivityScope"/> within the activity tree.
/// </summary>
internal enum ActivityScopeType
{
    /// <summary>
    /// The top-level chat request.
    /// </summary>
    Root = 0,

    /// <summary>
    /// A tool invocation made by the assistant or by a nested agent.
    /// </summary>
    ToolCall = 1,

    /// <summary>
    /// A nested model call made inside a tool invocation (for example by an agent tool).
    /// </summary>
    ModelCall = 2,
}
