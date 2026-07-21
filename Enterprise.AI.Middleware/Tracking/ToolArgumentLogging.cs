namespace Enterprise.AI.Middleware.Tracking;

/// <summary>
/// Controls how tool-call arguments are written to structured logs.
/// </summary>
/// <remarks>
/// Tool arguments frequently contain end-user input and therefore potential PII.
/// The default is <see cref="None"/>; more verbose settings should only be enabled
/// in environments where the log sink is approved for such data.
/// </remarks>
public enum ToolArgumentLogging
{
    /// <summary>
    /// Never log tool-call arguments. This is the default.
    /// </summary>
    None = 0,

    /// <summary>
    /// Log argument names only; values are redacted.
    /// </summary>
    Redacted = 1,

    /// <summary>
    /// Log argument names and values verbatim. Use only with approved log sinks.
    /// </summary>
    Full = 2,
}
