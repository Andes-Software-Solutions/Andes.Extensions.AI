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
        // Captured here, inside the tracking scope pushed by the core middleware, because MCP
        // progress notifications arrive on the client receive loop where the ambient flow is absent.
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
}
