using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI;

/// <summary>
/// Records the token usage reported by the model for a single assistant turn within a tracked request.
/// </summary>
/// <remarks>
/// A request that invokes tools produces multiple turns: one per model iteration of the function-invocation
/// loop. Turn breakdowns are only available for streaming requests; non-streaming requests report an
/// aggregate in <see cref="ChatUsageReport.AssistantUsage"/> with an empty <see cref="ChatUsageReport.Turns"/> list.
/// </remarks>
public sealed class AssistantTurnUsage
{
    /// <summary>
    /// Gets the zero-based model iteration within the function-invocation loop.
    /// </summary>
    public required int Iteration { get; init; }

    /// <summary>
    /// Gets the provider response identifier for the turn, if reported.
    /// </summary>
    public string? ResponseId { get; init; }

    /// <summary>
    /// Gets the model identifier for the turn, if reported.
    /// </summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// Gets the token usage reported for the turn.
    /// </summary>
    public required UsageDetails Usage { get; init; }
}
