using System.Globalization;
using ModelContextProtocol;

namespace Andes.Extensions.AI;

/// <summary>
/// Bridges MCP progress notifications into <see cref="ChatProgressKind.ToolProgress"/> updates
/// for one tool invocation.
/// </summary>
/// <remarks>
/// Progress notifications are dispatched on the MCP client's receive loop, where the tool
/// invocation's ambient <see cref="ChatProgress"/> flow is not present. The reporter is therefore
/// captured at construction (invocation) time and never resolved ambiently from
/// <see cref="Report"/>. Exceptions must not escape <see cref="Report"/>: the MCP session would
/// swallow them into its own (typically absent) logger, silently killing the notification with
/// no trace anywhere.
/// </remarks>
internal sealed class McpProgressBridge(IChatProgressReporter reporter) : IProgress<ProgressNotificationValue>
{
    /// <inheritdoc/>
    public void Report(ProgressNotificationValue value)
    {
        try
        {
            string message = value.Message ?? FormatProgress(value);
            reporter.Report(message, value.Progress, value.Total);
        }
        catch
        {
            // Intentionally swallowed: see the class remarks. The reporter only throws on
            // null/empty messages, which FormatProgress rules out, so this is a last-resort
            // guard for the receive loop rather than an expected path.
        }
    }

    private static string FormatProgress(ProgressNotificationValue value)
    {
        return value.Total is { } total
            ? string.Create(CultureInfo.InvariantCulture, $"{value.Progress}/{total}")
            : value.Progress.ToString(CultureInfo.InvariantCulture);
    }
}
