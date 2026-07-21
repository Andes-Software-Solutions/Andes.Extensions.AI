using Microsoft.Extensions.Logging;

namespace Enterprise.AI.Middleware.Tracking.Internal;

/// <summary>
/// Fans activity notifications out to multiple observers, isolating each observer's failures so
/// one faulty observer can neither break the chat request nor starve the other observers.
/// </summary>
internal sealed class CompositeChatActivityObserver : IChatActivityObserver
{
    private readonly IChatActivityObserver[] _observers;
    private readonly ILogger _logger;

    public CompositeChatActivityObserver(IEnumerable<IChatActivityObserver> observers, ILogger logger)
    {
        _observers = [.. observers];
        _logger = logger;
    }

    /// <inheritdoc/>
    public void OnActivityEvent(ChatActivityEvent activityEvent)
    {
        foreach (IChatActivityObserver observer in _observers)
        {
            try
            {
                observer.OnActivityEvent(activityEvent);
            }
            catch (Exception ex)
            {
                Log.ObserverThrew(_logger, ex, observer.GetType().FullName ?? observer.GetType().Name);
            }
        }
    }

    /// <inheritdoc/>
    public void OnRequestCompleted(ChatActivityReport report)
    {
        foreach (IChatActivityObserver observer in _observers)
        {
            try
            {
                observer.OnRequestCompleted(report);
            }
            catch (Exception ex)
            {
                Log.ObserverThrew(_logger, ex, observer.GetType().FullName ?? observer.GetType().Name);
            }
        }
    }
}
