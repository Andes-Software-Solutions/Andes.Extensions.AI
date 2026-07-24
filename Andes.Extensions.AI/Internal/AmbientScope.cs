namespace Andes.Extensions.AI;

/// <summary>
/// Holds the tool-call scope that is ambient on the current async flow, connecting code running
/// inside a tool implementation (or a nested tracked pipeline) to the tracker that invoked it.
/// </summary>
internal static class AmbientScope
{
    private static readonly AsyncLocal<ToolScope?> _current = new();

    /// <summary>
    /// Gets or sets the scope ambient on the current async flow, or <see langword="null"/> when
    /// no tracked request is active.
    /// </summary>
    public static ToolScope? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }

    /// <summary>
    /// Makes <paramref name="scope"/> ambient and returns a token that restores the previous value on dispose.
    /// </summary>
    public static ScopeRestorer Push(ToolScope scope)
    {
        ToolScope? previous = _current.Value;
        _current.Value = scope;
        return new ScopeRestorer(previous);
    }

    /// <summary>
    /// Restores the previously ambient scope when disposed.
    /// </summary>
    internal readonly struct ScopeRestorer(ToolScope? previous) : IDisposable
    {
        /// <inheritdoc/>
        public void Dispose()
        {
            _current.Value = previous;
        }
    }
}
