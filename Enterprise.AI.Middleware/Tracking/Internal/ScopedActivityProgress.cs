using System.Globalization;
using ModelContextProtocol;

namespace Enterprise.AI.Middleware.Tracking.Internal;

/// <summary>
/// Bridges MCP progress notifications into <see cref="ChatActivityEventKind.StatusReported"/>
/// events for one tool invocation.
/// </summary>
/// <remarks>
/// Progress notifications are dispatched on the MCP client's receive loop, where the tool
/// invocation's ambient <see cref="ChatActivityScope"/> flow is not present. The owning context
/// and scope are therefore captured at construction (invocation) time and never resolved
/// ambiently from <see cref="Report"/>. Exceptions must not escape <see cref="Report"/>: the MCP
/// session would swallow them into its own (typically absent) logger, silently killing the
/// notification with no trace in the middleware's logs.
/// </remarks>
internal sealed class ScopedActivityProgress : IProgress<ProgressNotificationValue>
{
    private readonly ActivityContext _context;
    private readonly ActivityScope _scope;

    public ScopedActivityProgress(ActivityContext context, ActivityScope scope)
    {
        _context = context;
        _scope = scope;
    }

    /// <inheritdoc/>
    public void Report(ProgressNotificationValue value)
    {
        try
        {
            string message = value.Message ?? FormatProgress(value);
            _context.PublishStatus(_scope, message, value.Progress, value.Total);
        }
        catch (Exception ex)
        {
            Log.ProgressBridgeThrew(_context.Logger, ex, _scope.Id);
        }
    }

    private static string FormatProgress(ProgressNotificationValue value)
    {
        return value.Total is { } total
            ? string.Create(CultureInfo.InvariantCulture, $"{value.Progress}/{total}")
            : value.Progress.ToString(CultureInfo.InvariantCulture);
    }
}
