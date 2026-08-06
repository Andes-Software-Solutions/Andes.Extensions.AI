using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI;

/// <summary>
/// Helpers for sending developer-constructed <see cref="ChatProgressUpdate"/> instances through the
/// same in-band shape the middleware emits.
/// </summary>
public static class ChatProgressUpdateExtensions
{
    /// <summary>
    /// Wraps the update in a synthetic role-less <see cref="ChatResponseUpdate"/> carrying a single
    /// <see cref="ChatProgressContent"/> item, ready to interleave into a stream consumed by
    /// progress-aware helpers such as the UI package's <c>ToStatusSnapshotsAsync()</c>.
    /// </summary>
    /// <param name="update">The progress update to wrap.</param>
    /// <returns>The synthetic update; it contains no text, so text-accumulation helpers are unaffected.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="update"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code language="csharp">
    /// async IAsyncEnumerable&lt;ChatResponseUpdate&gt; StreamTurn()
    /// {
    ///     yield return ChatProgressUpdate.CreateRequestStarted().ToResponseUpdate();
    ///
    ///     await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(history, options))
    ///     {
    ///         yield return update;
    ///     }
    /// }
    /// </code>
    /// </example>
    public static ChatResponseUpdate ToResponseUpdate(this ChatProgressUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        return new ChatResponseUpdate(role: null, contents: [new ChatProgressContent(update)]);
    }
}
