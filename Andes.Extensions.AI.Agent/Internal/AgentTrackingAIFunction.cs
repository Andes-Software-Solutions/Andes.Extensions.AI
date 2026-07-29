using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI;

/// <summary>
/// Wraps the function produced by <c>AIAgent.AsAIFunction()</c> so that classification can
/// discover the agent behind it through <see cref="AITool.GetService(Type, object?)"/>, and so
/// that nested invocations open their own child tracking scope.
/// </summary>
/// <remarks>
/// The wrapper answers <see cref="GetService"/> requests with the original agent — never the
/// internal usage-reporting decorator — so callers can also probe for concrete agent types.
/// A user's own <see cref="DelegatingAIFunction"/> around the wrapper still classifies as an
/// agent through the probe chain; wrappers are never unwrapped or bypassed.
/// <para>
/// When invoked while an ambient tracked scope is active that was not opened for this function
/// (the agent is a tool of another agent, or invoked directly inside a tool body), the wrapper
/// opens a child scope via <see cref="ChatProgress.BeginToolScope"/> — the agent then renders as
/// its own nested activity, and its usage and statuses attach to the child. When the tracking
/// middleware wrapped this function itself, the owner check makes the call a no-op.
/// </para>
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

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        using ChatProgressToolScope scope = ChatProgress.BeginToolScope(CreateDescriptor(), owner: this);
        try
        {
            return await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            scope.Fail();
            throw;
        }
    }

    private ToolDescriptor CreateDescriptor()
    {
        // Mirrors UseAgentToolClassification's descriptor; a nested scope cannot consult the
        // tracker's ToolClassifier (or its displayNameResolver), so the agent name is used directly.
        return new ToolDescriptor
        {
            Name = Name,
            DisplayName = string.IsNullOrEmpty(Agent.Name) ? Name : Agent.Name,
            Kind = ToolKind.Agent,
            Source = string.IsNullOrEmpty(Agent.Name) ? Agent.Id : Agent.Name,
        };
    }
}
