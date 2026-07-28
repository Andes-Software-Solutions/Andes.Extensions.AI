namespace Andes.Extensions.AI;

/// <summary>
/// A serialization-friendly, flattened view of token usage projected from
/// <see cref="Microsoft.Extensions.AI.UsageDetails"/>. Each count is nullable because a provider
/// may report only some of them.
/// </summary>
/// <example>
/// <code language="csharp">
/// var summary = new UsageSummary { InputTokens = 120, OutputTokens = 45, TotalTokens = 165 };
/// </code>
/// </example>
public sealed record UsageSummary
{
    /// <summary>
    /// Gets the number of input (prompt) tokens, when reported.
    /// </summary>
    public long? InputTokens { get; init; }

    /// <summary>
    /// Gets the number of output (completion) tokens, when reported.
    /// </summary>
    public long? OutputTokens { get; init; }

    /// <summary>
    /// Gets the total number of tokens, when reported.
    /// </summary>
    public long? TotalTokens { get; init; }
}
