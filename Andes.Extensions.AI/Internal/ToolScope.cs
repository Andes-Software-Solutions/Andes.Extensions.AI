using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI;

/// <summary>
/// A node in the per-request scope tree: the root scope represents the request itself and each
/// child represents one tool invocation, nesting further for tools invoked inside other tools.
/// </summary>
internal sealed class ToolScope(
    RequestTracker tracker,
    string scopeId,
    ToolScope? parent,
    ToolDescriptor? descriptor,
    string? callId,
    int depth)
{
    private readonly Lock _lock = new();
    private readonly List<ToolScope> _children = [];
    private UsageDetails? _usage;

    public RequestTracker Tracker { get; } = tracker;

    public string ScopeId { get; } = scopeId;

    public ToolScope? Parent { get; } = parent;

    /// <summary>
    /// Gets the descriptor of the tool this scope tracks, or <see langword="null"/> for the request root.
    /// </summary>
    public ToolDescriptor? Descriptor { get; } = descriptor;

    public string? CallId { get; } = callId;

    public int Depth { get; } = depth;

    public TimeSpan Duration { get; set; }

    public bool Succeeded { get; set; } = true;

    public void AddChild(ToolScope child)
    {
        lock (_lock)
        {
            _children.Add(child);
        }
    }

    /// <summary>
    /// Accumulates usage attributed to this scope. Safe to call from concurrent tool invocations
    /// and from nested tracked pipelines rolling up their totals.
    /// </summary>
    public void AddUsage(UsageDetails usage)
    {
        lock (_lock)
        {
            _usage ??= new UsageDetails();
            UsageMath.AddTo(_usage, usage);
        }
    }

    public IReadOnlyList<ToolScope> SnapshotChildren()
    {
        lock (_lock)
        {
            return [.. _children];
        }
    }

    public UsageDetails? SnapshotUsage()
    {
        lock (_lock)
        {
            return _usage is null ? null : UsageMath.Clone(_usage);
        }
    }
}
