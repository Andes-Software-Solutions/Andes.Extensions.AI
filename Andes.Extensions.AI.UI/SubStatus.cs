namespace Andes.Extensions.AI;

/// <summary>
/// A single sub-status line reported while an activity runs — for example "Extracting…" or an MCP
/// server's "step 2 of 5" — optionally with numeric progress for a progress bar.
/// </summary>
/// <example>
/// <code language="csharp">
/// var line = new SubStatus { Message = "Downloading", Progress = 2, ProgressTotal = 5 };
/// </code>
/// </example>
public sealed record SubStatus
{
    /// <summary>
    /// Gets the human-readable status message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the numeric progress (the numerator), when the reporter supplied one.
    /// </summary>
    /// <remarks>
    /// Sources that report progress as single-precision values (for example MCP servers) may
    /// produce float-to-double widening artifacts for fractional values; integer step counts are
    /// unaffected. Format with a rounding specifier such as <c>"0.#"</c> before display.
    /// </remarks>
    public double? Progress { get; init; }

    /// <summary>
    /// Gets the total amount of work — the denominator for <see cref="Progress"/> — when known.
    /// </summary>
    public double? ProgressTotal { get; init; }
}
