using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace Enterprise.AI.Middleware.Tracking.Internal;

/// <summary>
/// Instruments an <see cref="AIFunction"/> so that every invocation opens a tool-call activity
/// scope, publishes started/completed/failed events at the moment they happen, and makes the
/// scope ambiently available to nested instrumented chat clients.
/// </summary>
internal sealed class TrackedAIFunction : DelegatingAIFunction
{
    private readonly ToolClassification _classification;

    public TrackedAIFunction(AIFunction innerFunction, ToolClassification classification)
        : base(innerFunction)
    {
        _classification = classification;
    }

    /// <summary>
    /// Gets the classification computed for the wrapped function at wrap time.
    /// </summary>
    public ToolClassification Classification => _classification;

    /// <inheritdoc/>
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        ActivityFlowState? flow = ChatActivityScope.CurrentFlow;
        if (flow is null)
        {
            return await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);
        }

        ActivityContext context = flow.Context;
        string? toolCallId = FunctionInvokingChatClient.CurrentContext?.CallContent.CallId;
        ActivityScope scope = context.BeginToolScope(
            flow.Scope,
            _classification.Kind,
            Name,
            _classification.SourceName,
            toolCallId);
        LogArguments(context, arguments);

        ChatActivityScope.CurrentFlow = new ActivityFlowState(context, scope);
        try
        {
            object? result = await InvokeTargetAsync(context, scope, arguments, cancellationToken)
                .ConfigureAwait(false);
            context.CompleteScope(scope, error: null);
            return result;
        }
        catch (Exception ex)
        {
            context.CompleteScope(scope, ex);
            throw;
        }
        finally
        {
            ChatActivityScope.CurrentFlow = flow;
        }
    }

    private ValueTask<object?> InvokeTargetAsync(
        ActivityContext context,
        ActivityScope scope,
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        if (context.Options.EnableMcpProgress && ResolveMcpTool() is { } mcpTool)
        {
            // Progress notifications arrive on the MCP client's receive loop, outside this
            // invocation's async flow, so the context and scope are bound into the IProgress
            // rather than resolved ambiently at report time.
            McpClientTool progressTool = mcpTool.WithProgress(new ScopedActivityProgress(context, scope));
            return progressTool.InvokeAsync(arguments, cancellationToken);
        }

        return base.InvokeCoreAsync(arguments, cancellationToken);
    }

    private McpClientTool? ResolveMcpTool()
    {
        // Only the shapes this library produces are unwrapped; a user's own DelegatingAIFunction
        // around an MCP tool may add invocation behavior that must not be bypassed.
        return InnerFunction switch
        {
            McpClientTool direct => direct,
            AnnotatedAIFunction { Inner: McpClientTool annotated } => annotated,
            _ => null,
        };
    }

    private void LogArguments(ActivityContext context, AIFunctionArguments arguments)
    {
        switch (context.Options.ArgumentLogging)
        {
            case ToolArgumentLogging.Redacted:
                Log.ToolCallArguments(context.Logger, Name, string.Join(", ", arguments.Keys));
                break;

            case ToolArgumentLogging.Full:
                Log.ToolCallArguments(
                    context.Logger,
                    Name,
                    string.Join(", ", arguments.Select(pair => $"{pair.Key}={pair.Value}")));
                break;

            default:
                break;
        }
    }
}
