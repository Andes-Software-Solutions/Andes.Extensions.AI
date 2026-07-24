using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI;

/// <summary>
/// Decorates an <see cref="AIAgent"/> so that each run's token usage is attributed to the ambient
/// tool scope via <see cref="ChatProgress.ReportUsage"/>.
/// </summary>
/// <remarks>
/// The ambient scope is resolved at run time — the inverse of the MCP progress bridge's
/// capture-at-invocation: the agent runs in-process on the caller's async flow, so the scope
/// pushed by the tracking middleware is present inside the run, whereas decoration happens at
/// startup where no scope exists. Outside a tracked request the report is a safe no-op, so the
/// decorated agent behaves normally anywhere. Usage is reported only after a successful run; a
/// faulted run attributes nothing and the exception propagates untouched.
/// <para>
/// The streaming override is currently unreachable through this package's public surface —
/// <c>AsAIFunction()</c> runs agents non-streaming — but is implemented so the decorator stays
/// correct if that ever changes; per-update <see cref="UsageContent"/> reports sum correctly on
/// the tool scope.
/// </para>
/// </remarks>
internal sealed class UsageReportingAIAgent(AIAgent innerAgent) : DelegatingAIAgent(innerAgent)
{
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        CancellationToken cancellationToken)
    {
        AgentResponse response = await base.RunCoreAsync(messages, session, options, cancellationToken).ConfigureAwait(false);
        if (response.Usage is { } usage)
        {
            ChatProgress.ReportUsage(usage);
        }

        return response;
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (AgentResponseUpdate update in base.RunCoreStreamingAsync(messages, session, options, cancellationToken).ConfigureAwait(false))
        {
            foreach (UsageContent usageContent in update.Contents.OfType<UsageContent>())
            {
                ChatProgress.ReportUsage(usageContent.Details);
            }

            yield return update;
        }
    }
}
