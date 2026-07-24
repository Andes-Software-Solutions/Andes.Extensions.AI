using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI;

/// <summary>
/// Carries the final <see cref="ChatUsageReport"/> in-band as the last synthetic update of a
/// streamed tracked response.
/// </summary>
/// <remarks>
/// Because this type is not part of the <see cref="AIContent"/> polymorphic serialization contract,
/// call <see cref="ChatResponseProgressExtensions.StripProgressContent(ChatResponse)"/> before
/// persisting or serializing responses that may contain it.
/// </remarks>
public sealed class UsageReportContent : AIContent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UsageReportContent"/> class.
    /// </summary>
    /// <param name="report">The usage report to carry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="report"/> is <see langword="null"/>.</exception>
    public UsageReportContent(ChatUsageReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Report = report;
    }

    /// <summary>
    /// Gets the usage report carried by this content item.
    /// </summary>
    public ChatUsageReport Report { get; }
}
