using Enterprise.AI.Middleware.Tracking;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers chat activity tracking services into an <see cref="IServiceCollection"/>.
/// </summary>
public static class ToolTrackingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ToolTrackingOptions"/> for the tool-tracking middleware. Pair with
    /// <c>builder.UseToolTracking()</c> on the chat client pipeline, and register any number of
    /// <see cref="IChatActivityObserver"/> implementations to receive out-of-band events.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configure">Optionally configures the tracking options.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code language="csharp">
    /// builder.Services.AddChatActivityTracking(options =>
    /// {
    ///     options.Templates.AgentHeader = "Delegating to {0}";
    /// });
    /// builder.Services.AddSingleton&lt;IChatActivityObserver, SignalRActivityObserver&gt;();
    /// builder.Services.AddChatClient(services => providerClient
    ///     .AsBuilder()
    ///     .UseToolTracking()
    ///     .UseFunctionInvocation()
    ///     .Build(services));
    /// </code>
    /// </example>
    public static IServiceCollection AddChatActivityTracking(
        this IServiceCollection services,
        Action<ToolTrackingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is null)
        {
            services.AddOptions<ToolTrackingOptions>();
        }
        else
        {
            services.Configure(configure);
        }

        return services;
    }
}
