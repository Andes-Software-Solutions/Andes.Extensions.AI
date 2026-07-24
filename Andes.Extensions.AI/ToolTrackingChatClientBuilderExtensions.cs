using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI;

/// <summary>
/// Provides <see cref="ChatClientBuilder"/> extensions for registering tool tracking.
/// </summary>
public static class ToolTrackingChatClientBuilderExtensions
{
    /// <summary>
    /// Registers a <see cref="ToolTrackingChatClient"/> in the pipeline, tracking token usage per
    /// request and per tool call and propagating progress events in-band and to observers.
    /// </summary>
    /// <remarks>
    /// Call this <b>before</b> <c>UseFunctionInvocation()</c> so that the tracker wraps the tools
    /// the <see cref="FunctionInvokingChatClient"/> executes and can merge progress events into the
    /// stream while tools run. <see cref="IChatProgressObserver"/> instances registered in the
    /// dependency injection container are resolved and appended to
    /// <see cref="ToolTrackingOptions.Observers"/> automatically.
    /// </remarks>
    /// <param name="builder">The builder to register with.</param>
    /// <param name="configure">An optional callback that configures the <see cref="ToolTrackingOptions"/>.</param>
    /// <returns>The <paramref name="builder"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static ChatClientBuilder UseToolTracking(
        this ChatClientBuilder builder,
        Action<ToolTrackingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Use((innerClient, services) =>
        {
            var options = new ToolTrackingOptions();
            configure?.Invoke(options);

            if (services.GetService(typeof(IEnumerable<IChatProgressObserver>)) is IEnumerable<IChatProgressObserver> observers)
            {
                foreach (IChatProgressObserver observer in observers)
                {
                    options.Observers.Add(observer);
                }
            }

            return new ToolTrackingChatClient(innerClient, options);
        });
    }
}
