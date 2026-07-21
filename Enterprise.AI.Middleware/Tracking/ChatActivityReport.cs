using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tracking;

/// <summary>
/// Aggregates everything the tool-tracking middleware observed for a single top-level chat
/// request: the activity scope tree with per-scope token usage, and flat per-model rollups.
/// </summary>
/// <remarks>
/// The report is delivered to <see cref="IChatActivityObserver.OnRequestCompleted"/> for both
/// streaming and non-streaming requests. For non-streaming requests it is additionally attached
/// to <see cref="ChatResponse.AdditionalProperties"/> and can be retrieved with
/// <see cref="FromResponse"/>.
/// </remarks>
public sealed class ChatActivityReport
{
    /// <summary>
    /// Gets the identifier of the chat request this report describes.
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// Gets the time at which the request started.
    /// </summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// Gets the total wall-clock duration of the request.
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets the total token usage across the request and every nested scope.
    /// </summary>
    public required UsageDetails TotalUsage { get; init; }

    /// <summary>
    /// Gets the root of the activity scope tree.
    /// </summary>
    public required ActivityScopeReport Root { get; init; }

    /// <summary>
    /// Gets the token usage rolled up per (provider, model) pair, in first-seen order.
    /// </summary>
    /// <remarks>
    /// Streaming requests attribute usage per <see cref="UsageContent"/> update and therefore
    /// split multi-model tool loops accurately. Non-streaming requests only observe the
    /// aggregated <see cref="ChatResponse.Usage"/>, so a request whose tool-call turns hit
    /// several models is attributed entirely to <see cref="ChatResponse.ModelId"/>.
    /// </remarks>
    public required IReadOnlyList<ModelUsageBreakdown> UsageByModel { get; init; }

    /// <summary>
    /// Retrieves the report attached to a non-streaming <see cref="ChatResponse"/> by the
    /// tool-tracking middleware.
    /// </summary>
    /// <param name="response">The response returned by the tracked chat client pipeline.</param>
    /// <returns>
    /// The attached <see cref="ChatActivityReport"/>, or <see langword="null"/> when the response
    /// did not pass through the tracking middleware.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="response"/> is <see langword="null"/>.</exception>
    public static ChatActivityReport? FromResponse(ChatResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.AdditionalProperties is { } properties &&
            properties.TryGetValue(TrackingToolAnnotations.ActivityReportKey, out object? value) &&
            value is ChatActivityReport report)
        {
            return report;
        }

        return null;
    }
}
