using Enterprise.AI.Middleware.Tracking;

namespace Enterprise.AI.Middleware.IntegrationTests;

/// <summary>
/// Thread-safe observer that records events and reports for assertions.
/// </summary>
public sealed class CollectingObserver : IChatActivityObserver
{
    private readonly object _gate = new();
    private readonly List<ChatActivityEvent> _events = [];
    private readonly List<ChatActivityReport> _reports = [];

    public IReadOnlyList<ChatActivityEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    public IReadOnlyList<ChatActivityReport> Reports
    {
        get
        {
            lock (_gate)
            {
                return [.. _reports];
            }
        }
    }

    public void OnActivityEvent(ChatActivityEvent activityEvent)
    {
        lock (_gate)
        {
            _events.Add(activityEvent);
        }
    }

    public void OnRequestCompleted(ChatActivityReport report)
    {
        lock (_gate)
        {
            _reports.Add(report);
        }
    }
}
