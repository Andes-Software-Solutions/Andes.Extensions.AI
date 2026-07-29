namespace Andes.Extensions.AI;

/// <summary>
/// Represents a nested tool-call scope opened with
/// <see cref="ChatProgress.BeginToolScope(ToolDescriptor, Microsoft.Extensions.AI.AIFunction?)"/>.
/// Disposing the handle completes the scope — emitting a completion (or failure) event with the
/// elapsed duration — and restores the previously ambient scope.
/// </summary>
/// <remarks>
/// Dispose the handle on the same async flow that opened it, and dispose nested handles in
/// nesting order (the <see langword="using"/> pattern guarantees both); disposing out of order
/// leaves a completed scope ambient, so later reports attach to a finished activity. A handle
/// that is never disposed leaves the scope open, and it is then reported as succeeded with no
/// duration. An inactive handle (returned when no tracked request is active, or when the
/// tracking middleware already opened a scope for the same invocation) is a safe no-op.
/// </remarks>
public sealed class ChatProgressToolScope : IDisposable
{
    internal static readonly ChatProgressToolScope Inactive = new();

    private readonly ToolScope? _scope;
    private readonly AmbientScope.ScopeRestorer _restorer;
    private readonly long _startTimestamp;
    private bool _failed;
    private int _disposed;

    private ChatProgressToolScope()
    {
    }

    internal ChatProgressToolScope(ToolScope scope, AmbientScope.ScopeRestorer restorer, long startTimestamp)
    {
        _scope = scope;
        _restorer = restorer;
        _startTimestamp = startTimestamp;
    }

    /// <summary>
    /// Gets a value indicating whether the handle is bound to an open tracking scope.
    /// </summary>
    public bool IsActive => _scope is not null;

    /// <summary>
    /// Gets the identifier of the opened scope, or <see langword="null"/> when the handle is inactive.
    /// </summary>
    public string? ScopeId => _scope?.ScopeId;

    /// <summary>
    /// Marks the scope as failed, so that disposal emits a failure event instead of a completion.
    /// Must be called before <see cref="Dispose"/>; once the handle is disposed the completion
    /// event has already been emitted and a later call has no effect.
    /// </summary>
    /// <remarks>
    /// No exception details are recorded — progress events never carry error messages, matching
    /// the library's privacy invariant.
    /// </remarks>
    public void Fail()
    {
        if (_scope is null)
        {
            return;
        }

        _failed = true;
    }

    /// <summary>
    /// Completes the scope and restores the previously ambient scope. Idempotent — the completion
    /// event is emitted exactly once, even under concurrent disposal.
    /// </summary>
    public void Dispose()
    {
        if (_scope is null || Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        TimeSpan elapsed = _scope.Tracker.Options.TimeProvider.GetElapsedTime(_startTimestamp);
        _scope.Tracker.CompleteToolScope(_scope, elapsed, succeeded: !_failed);
        _restorer.Dispose();
    }
}
