using System.Text.Json;
using Enterprise.AI.Middleware.Tracking;
using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware;

/// <summary>
/// Provides JSON serialization helpers for the Enterprise.AI middleware content types.
/// </summary>
public static class EnterpriseAIJsonUtilities
{
    /// <summary>
    /// The <c>$type</c> discriminator id under which <see cref="ActivityStatusContent"/> is
    /// registered for polymorphic <see cref="AIContent"/> serialization.
    /// </summary>
    public const string ActivityStatusContentTypeId = "enterprise.ai.activityStatus";

    /// <summary>
    /// Registers <see cref="ActivityStatusContent"/> as a polymorphic <see cref="AIContent"/>
    /// subtype on the given serializer options, enabling round-trip serialization of streaming
    /// updates that contain in-band activity status (for example when forwarding them over SSE).
    /// </summary>
    /// <param name="options">The serializer options to register the content type on.</param>
    /// <returns>The same <paramref name="options"/> instance, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code language="csharp">
    /// JsonSerializerOptions jsonOptions = new(AIJsonUtilities.DefaultOptions);
    /// jsonOptions.AddActivityStatusContent();
    /// string json = JsonSerializer.Serialize(update, jsonOptions);
    /// </code>
    /// </example>
    public static JsonSerializerOptions AddActivityStatusContent(this JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AddAIContentType<ActivityStatusContent>(ActivityStatusContentTypeId);
        return options;
    }
}
