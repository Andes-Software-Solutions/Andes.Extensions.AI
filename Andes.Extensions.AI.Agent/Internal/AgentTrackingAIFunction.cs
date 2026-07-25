using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI;

/// <summary>
/// Wraps the function produced by <c>AIAgent.AsAIFunction()</c> so that classification can
/// discover the agent behind it through <see cref="AITool.GetService(Type, object?)"/>.
/// </summary>
/// <remarks>
/// The wrapper answers <see cref="GetService"/> requests with the original agent — never the
/// internal usage-reporting decorator — so callers can also probe for concrete agent types.
/// A user's own <see cref="DelegatingAIFunction"/> around the wrapper still classifies as an
/// agent through the probe chain; wrappers are never unwrapped or bypassed.
/// </remarks>
internal sealed class AgentTrackingAIFunction(AIFunction function, AIAgent agent) : DelegatingAIFunction(function)
{
    /// <summary>
    /// Gets the original agent exposed by this tool, as passed to
    /// <see cref="AgentToolTrackingExtensions.WithTracking"/>.
    /// </summary>
    public AIAgent Agent { get; } = agent;

    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        // typeof(object) is excluded so a generic probe still answers with the outermost tool,
        // per the AITool convention; only agent-typed probes are redirected to the agent.
        return serviceKey is null && serviceType != typeof(object) && serviceType.IsInstanceOfType(Agent)
            ? Agent
            : base.GetService(serviceType, serviceKey);
    }
}
