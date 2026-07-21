using Enterprise.AI.Middleware.Tracking.Internal;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tracking;

/// <summary>
/// Exposes Microsoft Agent Framework agents as function tools annotated for activity tracking,
/// so the middleware renders them as <c>"Calling {AgentName} Agent"</c> and attributes their
/// nested activity and token usage to the agent's tool-call scope.
/// </summary>
public static class AgentToolTrackingExtensions
{
    /// <summary>
    /// Converts an agent to a function tool (equivalent to <c>agent.AsAIFunction()</c>) and
    /// annotates it with <see cref="ToolKind.Agent"/> and the agent's display name.
    /// </summary>
    /// <param name="agent">The agent to expose as a tool.</param>
    /// <param name="options">Optional metadata used to customize the function representation.</param>
    /// <param name="session">
    /// Optional session used across invocations; when omitted a new session is created per call.
    /// </param>
    /// <returns>An annotated function tool that runs the agent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code language="csharp">
    /// ChatOptions chatOptions = new()
    /// {
    ///     Tools = [researchAgent.AsTrackedAIFunction()],
    /// };
    /// </code>
    /// </example>
    public static AIFunction AsTrackedAIFunction(
        this AIAgent agent,
        AIFunctionFactoryOptions? options = null,
        AgentSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(agent);

        AIFunction function = agent.AsAIFunction(options, session);
        string displayName = agent.Name ?? function.Name;
        return new AnnotatedAIFunction(function, ToolKind.Agent, displayName);
    }
}
