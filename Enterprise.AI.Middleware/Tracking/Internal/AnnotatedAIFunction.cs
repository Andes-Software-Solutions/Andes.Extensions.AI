using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tracking.Internal;

/// <summary>
/// Wraps an <see cref="AIFunction"/> to attach the tracking annotations that identify its
/// <see cref="ToolKind"/> and source display name, leaving invocation behavior untouched.
/// </summary>
internal sealed class AnnotatedAIFunction : DelegatingAIFunction
{
    private readonly IReadOnlyDictionary<string, object?> _additionalProperties;

    public AnnotatedAIFunction(AIFunction innerFunction, ToolKind toolKind, string sourceName)
        : base(innerFunction)
    {
        var merged = new Dictionary<string, object?>(innerFunction.AdditionalProperties, StringComparer.Ordinal)
        {
            [TrackingToolAnnotations.ToolKindKey] = toolKind,
            [TrackingToolAnnotations.SourceNameKey] = sourceName,
        };
        _additionalProperties = merged;
    }

    /// <inheritdoc/>
    public override IReadOnlyDictionary<string, object?> AdditionalProperties => _additionalProperties;
}
