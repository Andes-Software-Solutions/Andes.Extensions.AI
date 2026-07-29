using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace Andes.Extensions.AI;

/// <summary>
/// Wraps an <see cref="McpClientTool"/> so that tracked invocations carry the owning server's
/// display name and bridge the server's progress notifications into chat progress updates.
/// </summary>
/// <remarks>
/// The wrapper deliberately bridges only the <see cref="McpClientTool"/> it was constructed over.
/// A user's own <see cref="DelegatingAIFunction"/> around an MCP tool is never unwrapped or
/// bypassed; such tools still classify as MCP through the
/// <see cref="AITool.GetService(Type, object?)"/> probe but receive no progress bridge.
/// <para>
/// When invoked while an ambient tracked scope is active that was not opened for this function
/// (the MCP tool is a tool of an agent, or invoked directly inside a tool body), the wrapper
/// opens a child scope via <see cref="ChatProgress.BeginToolScope"/> so the call renders as its
/// own nested activity; the progress bridge then binds to that child scope. When the tracking
/// middleware wrapped this function itself, the owner check makes the call a no-op.
/// </para>
/// </remarks>
internal sealed class McpTrackingAIFunction(McpClientTool tool, string serverName, bool enableProgress) : DelegatingAIFunction(tool)
{
    private readonly McpClientTool _tool = tool;


    /// <summary>
    /// Gets the server display name shown in progress headers ("Calling {ServerName} MCP").
    /// </summary>
    public string ServerName { get; } = serverName;

    /// <summary>
    /// Gets a value indicating whether MCP progress notifications are bridged into chat progress updates.
    /// </summary>
    public bool EnableProgress { get; } = enableProgress;

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        // Opened before the reporter capture below so a nested invocation's bridge binds to the
        // child scope; for invocations the tracker already wrapped, this is an inactive no-op.
        using ChatProgressToolScope scope = ChatProgress.BeginToolScope(CreateDescriptor(), owner: this);
        try
        {
            // Captured here, inside the tracking scope, because MCP progress notifications arrive
            // on the client receive loop where the ambient flow is absent.
            IChatProgressReporter reporter = ChatProgress.Current;
            if (!EnableProgress || !reporter.IsActive)
            {
                return await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);
            }

            // WithProgress is per-invocation by design: the IProgress must capture this invocation's
            // reporter, so a cached progress-enabled tool cannot exist.
            McpClientTool progressTool = _tool.WithProgress(new McpProgressBridge(reporter));
            return await progressTool.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            scope.Fail();
            throw;
        }
    }

    private ToolDescriptor CreateDescriptor()
    {
        // Mirrors UseMcpToolClassification's descriptor; a nested scope cannot consult the
        // tracker's ToolClassifier, so the constructed server name is used directly.
        return new ToolDescriptor
        {
            Name = Name,
            Kind = ToolKind.McpTool,
            Source = ServerName,
        };
    }
}
