using System.Text.Json.Serialization;

namespace Andes.Extensions.AI;

/// <summary>
/// A source-generated <see cref="JsonSerializerContext"/> for the UI contract, configured to match
/// the shipped TypeScript interface: camelCase property names, enums serialized as strings, and
/// <see langword="null"/> values omitted. Use it for trim- and AOT-safe serialization, including in
/// Blazor WebAssembly.
/// </summary>
/// <example>
/// <code language="csharp">
/// string json = JsonSerializer.Serialize(uiEvent, AssistantUiJsonContext.Default.AssistantUiEvent);
/// AssistantStatusSnapshot? snapshot = JsonSerializer.Deserialize(
///     json, AssistantUiJsonContext.Default.AssistantStatusSnapshot);
/// </code>
/// </example>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AssistantUiEvent))]
[JsonSerializable(typeof(AssistantStatusSnapshot))]
public sealed partial class AssistantUiJsonContext : JsonSerializerContext
{
}
