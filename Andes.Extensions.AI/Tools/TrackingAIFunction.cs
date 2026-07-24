using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI;

/// <summary>
/// Wraps an <see cref="AIFunction"/> so that its invocations open a tracking scope, emit
/// progress events, and time their execution, while preserving the inner function's identity
/// (name, description, schema) for the function-invocation loop.
/// </summary>
internal sealed class TrackingAIFunction : DelegatingAIFunction
{
    private readonly ToolDescriptor _descriptor;
    private readonly RequestTracker _tracker;

    public TrackingAIFunction(AIFunction innerFunction, ToolDescriptor descriptor, RequestTracker tracker)
        : base(innerFunction)
    {
        _descriptor = descriptor;
        _tracker = tracker;
    }

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        string? callId = FunctionInvokingChatClient.CurrentContext?.CallContent.CallId;
        ToolScope parent = AmbientScope.Current ?? _tracker.RootScope;
        ToolScope scope = _tracker.BeginToolScope(_descriptor, callId, parent, CaptureArguments(arguments));
        long startTimestamp = _tracker.Options.TimeProvider.GetTimestamp();
        using AmbientScope.ScopeRestorer restorer = AmbientScope.Push(scope);
        try
        {
            object? result = await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);
            _tracker.CompleteToolScope(scope, _tracker.Options.TimeProvider.GetElapsedTime(startTimestamp), succeeded: true);
            return result;
        }
        catch
        {
            _tracker.CompleteToolScope(scope, _tracker.Options.TimeProvider.GetElapsedTime(startTimestamp), succeeded: false);
            throw;
        }
    }

    private IReadOnlyDictionary<string, string>? CaptureArguments(AIFunctionArguments arguments)
    {
        if (!_tracker.Options.IncludeToolArguments || arguments.Count == 0)
        {
            return null;
        }

        var captured = new Dictionary<string, string>(arguments.Count);
        foreach (KeyValuePair<string, object?> pair in arguments)
        {
            captured[pair.Key] = pair.Value?.ToString() ?? "null";
        }

        return captured;
    }
}
