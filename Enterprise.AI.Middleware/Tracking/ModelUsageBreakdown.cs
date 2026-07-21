namespace Enterprise.AI.Middleware.Tracking;

/// <summary>
/// Summarizes the token usage recorded for a single (provider, model) pair during a chat request.
/// </summary>
public sealed class ModelUsageBreakdown
{
    /// <summary>
    /// Gets the name of the provider that served the calls (for example <c>azure.ai.openai</c>),
    /// or <see langword="null"/> when the provider did not expose metadata.
    /// </summary>
    public string? ProviderName { get; init; }

    /// <summary>
    /// Gets the identifier of the model that produced the usage, or <see langword="null"/> when unknown.
    /// </summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// Gets the total number of input (prompt) tokens, or <see langword="null"/> when the
    /// provider did not report input usage.
    /// </summary>
    public long? InputTokenCount { get; init; }

    /// <summary>
    /// Gets the total number of output (completion) tokens, or <see langword="null"/> when the
    /// provider did not report output usage.
    /// </summary>
    public long? OutputTokenCount { get; init; }

    /// <summary>
    /// Gets the total number of tokens, or <see langword="null"/> when the provider did not
    /// report total usage.
    /// </summary>
    public long? TotalTokenCount { get; init; }
}
