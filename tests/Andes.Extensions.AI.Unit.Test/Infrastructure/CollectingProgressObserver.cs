namespace Andes.Extensions.AI.Unit.Test.Infrastructure;

/// <summary>
/// Records every out-of-band progress event and the final report, thread-safely.
/// </summary>
internal sealed class CollectingProgressObserver : IChatProgressObserver
{
    private readonly Lock _lock = new();
    private readonly List<ChatProgressUpdate> _updates = [];
    private ChatUsageReport? _report;

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
    }

    public void OnRequestCompleted(ChatUsageReport report)
    {
        lock (_lock)
        {
            _report = report;
        }
    }
}
