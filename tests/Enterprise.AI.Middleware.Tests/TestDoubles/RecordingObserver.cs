using Enterprise.AI.Middleware.Tracking;

namespace Enterprise.AI.Middleware.Tests.TestDoubles;

/// <summary>
/// Captures every activity event and report delivered by the middleware, in order.
/// </summary>
public sealed class RecordingObserver : IChatActivityObserver
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

    /// <summary>
    /// Gets or sets a callback invoked after each event is recorded, for tests that need to
    /// react to event arrival (for example releasing a gate).
    /// </summary>
    public Action<ChatActivityEvent>? EventCallback { get; set; }

    public void OnActivityEvent(ChatActivityEvent activityEvent)
    {
        lock (_gate)
        {
            _events.Add(activityEvent);
        }

        EventCallback?.Invoke(activityEvent);
    }

    public void OnRequestCompleted(ChatActivityReport report)
    {
        lock (_gate)
        {
            _reports.Add(report);
        }
    }

    public IReadOnlyList<ChatActivityEvent> EventsOfKind(ChatActivityEventKind kind)
    {
        lock (_gate)
        {
            return [.. _events.Where(e => e.Kind == kind)];
        }
    }
}
