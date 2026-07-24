namespace Andes.Extensions.AI.Mcp.Unit.Test.Infrastructure;

/// <summary>
/// Records every out-of-band progress event thread-safely and invokes an optional callback per
/// event, letting tests release gates (for example, the fixture's ProgressAck) once an expected
/// number of events has been observed.
/// </summary>
internal sealed class NotifyingProgressObserver : IChatProgressObserver
{
    private readonly Lock _lock = new();
    private readonly List<ChatProgressUpdate> _updates = [];
    private ChatUsageReport? _report;

    public Action<ChatProgressUpdate>? Callback { get; init; }

    public IReadOnlyList<ChatProgressUpdate> Updates
    {
        get
        {
            lock (_lock)
            {
                return [.. _updates];
            }
        }
    }

    public ChatUsageReport? Report
    {
        get
        {
            lock (_lock)
            {
                return _report;
            }
        }
    }

    public void OnProgress(ChatProgressUpdate update)
    {
        lock (_lock)
        {
            _updates.Add(update);
        }

        Callback?.Invoke(update);
    }

    public void OnRequestCompleted(ChatUsageReport report)
    {
        lock (_lock)
        {
            _report = report;
        }
    }
}
