using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tracking.Internal;

/// <summary>
/// Accumulates <see cref="UsageDetails"/> from concurrent writers without losing counts.
/// </summary>
/// <remarks>
/// Null token counts are treated as "unknown", not zero: a count only becomes non-null once at
/// least one recorded <see cref="UsageDetails"/> carried a value for it, which matches the
/// additive semantics of <see cref="UsageDetails.Add"/>.
/// </remarks>
internal sealed class UsageAccumulator
{
    private readonly object _gate = new();
    private readonly UsageDetails _total = new();

    /// <summary>
    /// Adds the given usage to the running total.
    /// </summary>
    /// <param name="usage">The usage to add.</param>
    public void Add(UsageDetails usage)
    {
        lock (_gate)
        {
            _total.Add(usage);
        }
    }

    /// <summary>
    /// Returns a defensive copy of the accumulated total.
    /// </summary>
    /// <returns>A snapshot of the current total usage.</returns>
    public UsageDetails Snapshot()
    {
        lock (_gate)
        {
            var copy = new UsageDetails();
            copy.Add(_total);
            return copy;
        }
    }
}
