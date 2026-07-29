using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI;

/// <summary>
/// Wraps Microsoft Agent Framework agents as function tools with first-class tool tracking: the
/// wrapper makes the agent discoverable to
/// <see cref="ToolTrackingOptionsAgentExtensions.UseAgentToolClassification"/> (rendering the
/// <c>"Calling {Agent} Agent"</c> header) and, by default, attributes each run's token usage to
/// the calling tool's scope.
/// </summary>
/// <remarks>
/// The function produced by <c>AIAgent.AsAIFunction()</c> exposes neither its agent nor the run's
/// <see cref="AgentResponse.Usage"/>, so the tool-tracking middleware cannot discover either on
/// its own. Apply <see cref="WithTracking"/> when registering an agent as a tool; an agent exposed
/// through a plain <c>AsAIFunction()</c> call still works but classifies as
/// <see cref="ToolKind.Function"/> and reports no usage.
/// </remarks>
public static class AgentToolTrackingExtensions
{
    /// <summary>
    /// Wraps an agent as a function tool whose invocations are tracked with agent classification,
    /// usage attribution, and optional reporting of the agent's own function calls.
    /// </summary>
    /// <remarks>
    /// The agent runs in-process on the caller's async flow, so statuses its function tools report
    /// through <see cref="ChatProgress.Report(string)"/> already surface beneath the agent's header
    /// without any configuration here.
    /// <para>
    /// Nested invocations open their own child scope: when the wrapped function is used as a tool
    /// of another agent, or invoked directly inside a tool body, the agent renders as its own
    /// child activity (with its usage attributed to that child) instead of flat sub-statuses on
    /// the enclosing tool. A recursive self-invocation (the same wrapped function invoked from
    /// within its own run) is indistinguishable from the tracker's own delegation and stays flat.
    /// </para>
    /// <para>
    /// Pass <paramref name="trackUsage"/> as <see langword="false"/> when the agent's own chat
    /// client pipeline uses <c>UseToolTracking()</c>: the nested pipeline already rolls its total
    /// usage up into the calling tool's scope, and reporting <see cref="AgentResponse.Usage"/> on
    /// top would double-count.
    /// </para>
    /// <para>
    /// Name your agents: the function name is derived from the agent's name, and an unnamed agent
    /// falls back to <see cref="AIFunctionFactory"/>'s default unless
    /// <paramref name="functionOptions"/> provides a name.
    /// </para>
    /// </remarks>
    /// <param name="agent">The agent to expose as a tracked function tool.</param>
    /// <param name="functionOptions">Optional metadata for the created function, forwarded to <c>AsAIFunction</c>.</param>
    /// <param name="session">
    /// An optional session shared by all invocations, forwarded to <c>AsAIFunction</c>. A
    /// session-bound function is stateful: avoid registering it where parallel function calls
    /// could invoke it concurrently, as concurrent runs would share the session.
    /// </param>
    /// <param name="trackUsage">
    /// <see langword="true"/> to attribute the <see cref="AgentResponse.Usage"/> of each run to the
    /// calling tool's scope; <see langword="false"/> to only carry the agent identity.
    /// </param>
    /// <param name="reportFunctionCalls">
    /// <see langword="true"/> to report a <c>"Calling {Function} Tool"</c> status each time the
    /// agent invokes one of its function tools (function names only). Requires an agent whose
    /// pipeline performs local function invocation, such as an agent over an
    /// <see cref="IChatClient"/>; agents whose tools run service-side reject the interception.
    /// </param>
    /// <returns>A wrapped function that runs the agent unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code language="csharp">
    /// AIAgent weatherAgent = weatherChatClient.AsAIAgent(
    ///     instructions: "You answer questions about the weather.",
    ///     name: "Weather Agent",
    ///     description: "Answers questions about the weather.",
    ///     tools: [AIFunctionFactory.Create(GetWeather)]);
    ///
    /// var options = new ChatOptions { Tools = [weatherAgent.WithTracking()] };
    /// </code>
    /// </example>
    public static AIFunction WithTracking(
        this AIAgent agent,
        AIFunctionFactoryOptions? functionOptions = null,
        AgentSession? session = null,
        bool trackUsage = true,
        bool reportFunctionCalls = false)
    {
        ArgumentNullException.ThrowIfNull(agent);

        AIAgent effective = agent;
        if (reportFunctionCalls)
        {
            effective = new AIAgentBuilder(effective).Use(ReportFunctionCallAsync).Build();
        }

        if (trackUsage)
        {
            effective = new UsageReportingAIAgent(effective);
        }

        return new AgentTrackingAIFunction(effective.AsAIFunction(functionOptions, session), agent);
    }

    private static async ValueTask<object?> ReportFunctionCallAsync(
        AIAgent agent,
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
        CancellationToken cancellationToken)
    {
        // Function name only — progress events never carry tool arguments or results.
        ChatProgress.Report($"Calling {context.Function.Name} Tool");
        return await next(context, cancellationToken).ConfigureAwait(false);
    }
}
