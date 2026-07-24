using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI;

/// <summary>
/// Aggregates token usage for a completed tracked chat request: the main assistant's usage,
/// per-turn breakdowns, and per-tool-call attributions.
/// </summary>
public sealed class ChatUsageReport
{
    /// <summary>
    /// Gets the provider response identifier of the last model turn, if reported.
    /// </summary>
    public string? ResponseId { get; init; }

    /// <summary>
    /// Gets the model identifier observed on the response, falling back to the inner client's
    /// default model when the provider did not report one.
    /// </summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// Gets the provider name from the inner client's <see cref="ChatClientMetadata"/>, if available.
    /// </summary>
    public string? ProviderName { get; init; }

    /// <summary>
    /// Gets the token usage of the main assistant only, summed across all model turns.
    /// Usage attributed to tools is excluded.
    /// </summary>
    public required UsageDetails AssistantUsage { get; init; }

    /// <summary>
    /// Gets the per-turn usage breakdown. Populated for streaming requests only; empty for
    /// non-streaming requests, whose providers report a single aggregate.
    /// </summary>
    public IReadOnlyList<AssistantTurnUsage> Turns { get; init; } = [];

    /// <summary>
    /// Gets the top-level tool invocations made during the request, each carrying its own usage rollup.
    /// </summary>
    public IReadOnlyList<ToolCallUsage> ToolCalls { get; init; } = [];

    /// <summary>
    /// Gets the total usage for the request: <see cref="AssistantUsage"/> plus the rollups of all
    /// top-level <see cref="ToolCalls"/>.
    /// </summary>
    public required UsageDetails TotalUsage { get; init; }

    /// <summary>
    /// Gets the wall-clock duration of the request.
    /// </summary>
    public TimeSpan Duration { get; init; }
}
