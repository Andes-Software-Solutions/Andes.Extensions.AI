using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Andes.Extensions.AI.Mcp.Unit.Test.Infrastructure;

/// <summary>
/// An <see cref="McpClient"/> that exposes a chosen <see cref="McpClient.ServerInfo"/> and nothing
/// else, for identities <see cref="InMemoryMcpFixture"/> cannot produce: the SDK's server always
/// sends one, so a real client never observes a missing or empty identity here.
/// </summary>
#pragma warning disable MCPEXP002 // The protected McpClient constructor is experimental.
public sealed class ServerInfoOnlyMcpClient(Implementation? serverInfo) : McpClient
#pragma warning restore MCPEXP002
{
    public override Implementation? ServerInfo { get; } = serverInfo;

    public override ServerCapabilities ServerCapabilities => throw new NotSupportedException();

    public override string? ServerInstructions => throw new NotSupportedException();

    public override Task<ClientCompletionDetails> Completion => throw new NotSupportedException();

    public override string? SessionId => throw new NotSupportedException();

    public override string? NegotiatedProtocolVersion => throw new NotSupportedException();

    public override ValueTask<IDictionary<string, InputResponse>> ResolveInputRequestsAsync(
        IDictionary<string, InputRequest> inputRequests,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public override Task<JsonRpcResponse> SendRequestAsync(
        JsonRpcRequest request,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public override Task SendMessageAsync(
        JsonRpcMessage message,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public override IAsyncDisposable RegisterNotificationHandler(
        string method,
        Func<JsonRpcNotification, CancellationToken, ValueTask> handler) => throw new NotSupportedException();

    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
