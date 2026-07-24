using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI;

/// <summary>
/// Provides addition over <see cref="UsageDetails"/> instances with nullable token counts,
/// treating absent counts as "not reported" rather than zero.
/// </summary>
internal static class UsageMath
{
    /// <summary>
    /// Adds <paramref name="source"/> into <paramref name="target"/>, mutating <paramref name="target"/> only.
    /// </summary>
    public static void AddTo(UsageDetails target, UsageDetails source)
    {
        target.InputTokenCount = AddNullable(target.InputTokenCount, source.InputTokenCount);
        target.OutputTokenCount = AddNullable(target.OutputTokenCount, source.OutputTokenCount);
        target.TotalTokenCount = AddNullable(target.TotalTokenCount, source.TotalTokenCount);

        if (source.AdditionalCounts is { Count: > 0 } counts)
        {
            target.AdditionalCounts ??= new AdditionalPropertiesDictionary<long>();
            foreach (KeyValuePair<string, long> pair in counts)
            {
                target.AdditionalCounts[pair.Key] =
                    target.AdditionalCounts.TryGetValue(pair.Key, out long existing) ? existing + pair.Value : pair.Value;
            }
        }
    }

    /// <summary>
    /// Returns a copy of <paramref name="source"/> that can be mutated or exposed without aliasing it.
    /// </summary>
    public static UsageDetails Clone(UsageDetails source)
    {
        var copy = new UsageDetails();
        AddTo(copy, source);
        return copy;
    }

    private static long? AddNullable(long? left, long? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return left + right;
    }
}
