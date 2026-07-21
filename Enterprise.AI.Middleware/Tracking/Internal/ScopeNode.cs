namespace Enterprise.AI.Middleware.Tracking.Internal;

/// <summary>
/// Holds the mutable per-scope state tracked while a request is in flight: timing, directly
/// recorded usage, and child scopes.
/// </summary>
internal sealed class ScopeNode
{
    private readonly object _gate = new();
    private readonly List<ScopeNode> _children = [];
    private TimeSpan _duration;

    public ScopeNode(ActivityScope scope, long startTimestamp)
    {
        Scope = scope;
        StartTimestamp = startTimestamp;
    }

    public ActivityScope Scope { get; }

    public long StartTimestamp { get; }

    public UsageAccumulator OwnUsage { get; } = new();

    public TimeSpan Duration
    {
        get
        {
            lock (_gate)
            {
                return _duration;
            }
        }
        set
        {
            lock (_gate)
            {
                _duration = value;
            }
        }
    }

    public void AddChild(ScopeNode child)
    {
        lock (_gate)
        {
            _children.Add(child);
        }
    }

    public ScopeNode[] SnapshotChildren()
    {
        lock (_gate)
        {
            return [.. _children];
        }
    }
}
