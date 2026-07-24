using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI;

/// <summary>
/// Adds agent tool classification to <see cref="ToolTrackingOptions"/> so agents exposed as
/// function tools render with the <c>"Calling {Agent} Agent"</c> header and report
/// <see cref="ToolKind.Agent"/> in usage reports.
/// </summary>
public static class ToolTrackingOptionsAgentExtensions
{
    /// <summary>
    /// Installs a <see cref="ToolTrackingOptions.ToolClassifier"/> that recognizes agents wrapped
    /// by <see cref="AgentToolTrackingExtensions.WithTracking"/> (including inside user
    /// <see cref="DelegatingAIFunction"/> chains) as <see cref="ToolKind.Agent"/>.
    /// </summary>
    /// <remarks>
    /// The display name shown in headers is resolved in precedence order:
    /// <paramref name="displayNameResolver"/>, then the agent's <see cref="AIAgent.Name"/>, then
    /// the function name. <see cref="ToolDescriptor.Source"/> carries the agent's name, or its
    /// <see cref="AIAgent.Id"/> for unnamed agents. Tools that are not recognized agents are
    /// delegated to the classifier configured before this call, or classified with
    /// <see cref="ToolDescriptor.CreateDefault"/> when there is none.
    /// <para>
    /// The classifier probes for the agent through <see cref="AITool.GetService(Type, object?)"/>;
    /// a function created by a plain <c>AIAgent.AsAIFunction()</c> call does not expose its agent
    /// and therefore classifies as <see cref="ToolKind.Function"/>.
    /// </para>
    /// </remarks>
    /// <param name="options">The options to configure.</param>
    /// <param name="displayNameResolver">
    /// An optional callback resolving the header display name for a recognized agent tool.
    /// Return <see langword="null"/>, empty, or whitespace to fall back to the agent's name.
    /// </param>
    /// <returns>The same <paramref name="options"/> instance, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code language="csharp">
    /// IChatClient client = innerClient
    ///     .AsBuilder()
    ///     .UseToolTracking(options => options.UseAgentToolClassification())
    ///     .UseFunctionInvocation()
    ///     .Build();
    /// </code>
    /// </example>
    public static ToolTrackingOptions UseAgentToolClassification(
        this ToolTrackingOptions options,
        Func<AIFunction, string?>? displayNameResolver = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        Func<AITool, ToolDescriptor>? previous = options.ToolClassifier;
        options.ToolClassifier = tool =>
        {
            if (tool is AIFunction function && function.GetService<AIAgent>() is { } agent)
            {
                string? resolved = displayNameResolver?.Invoke(function);
                string displayName = string.IsNullOrWhiteSpace(resolved)
                    ? string.IsNullOrEmpty(agent.Name) ? function.Name : agent.Name
                    : resolved;

                return new ToolDescriptor
                {
                    Name = tool.Name,
                    DisplayName = displayName,
                    Kind = ToolKind.Agent,
                    Source = string.IsNullOrEmpty(agent.Name) ? agent.Id : agent.Name,
                };
            }

            // Preserve a previous classifier's null result so the core null guard still fires.
            return previous is not null ? previous(tool) : ToolDescriptor.CreateDefault(tool);
        };
        return options;
    }
}
