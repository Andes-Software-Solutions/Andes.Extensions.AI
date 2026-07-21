using Enterprise.AI.Middleware.Tracking;
using Enterprise.AI.Middleware.Tracking.Internal;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Enterprise.AI.Middleware;

/// <summary>
/// Registers the tool-tracking middleware into a <see cref="ChatClientBuilder"/> pipeline.
/// </summary>
public static class ToolTrackingChatClientBuilderExtensions
{
    /// <summary>
    /// Adds tool/agent/MCP activity and token-usage tracking to the pipeline, resolving
    /// <see cref="ToolTrackingOptions"/>, every registered <see cref="IChatActivityObserver"/>,
    /// and logging from the dependency-injection container. Pair with
    /// <see cref="ToolTrackingServiceCollectionExtensions.AddChatActivityTracking"/>.
    /// </summary>
    /// <param name="builder">The chat client pipeline builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Call this <b>before</b> <c>UseFunctionInvocation()</c> so the tracker wraps the tools the
    /// function-invoking client executes.
    /// </remarks>
    public static ChatClientBuilder UseToolTracking(this ChatClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Use((innerClient, services) =>
        {
            ToolTrackingOptions options =
                services.GetService<IOptions<ToolTrackingOptions>>()?.Value ?? new ToolTrackingOptions();
            ILoggerFactory? loggerFactory = services.GetService<ILoggerFactory>();
            IChatActivityObserver? observer = CreateObserver(
                services.GetServices<IChatActivityObserver>(),
                loggerFactory);
            return new ToolTrackingChatClient(innerClient, options, observer, loggerFactory);
        });
    }

    /// <summary>
    /// Adds tool/agent/MCP activity and token-usage tracking to the pipeline with inline
    /// configuration and no dependency-injection requirements.
    /// </summary>
    /// <param name="builder">The chat client pipeline builder.</param>
    /// <param name="configure">Configures the tracking options.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    public static ChatClientBuilder UseToolTracking(
        this ChatClientBuilder builder,
        Action<ToolTrackingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ToolTrackingOptions();
        configure(options);
        return builder.Use(innerClient => new ToolTrackingChatClient(innerClient, options));
    }

    /// <summary>
    /// Adds tool/agent/MCP activity and token-usage tracking to the pipeline with explicit
    /// options, observer, and logging.
    /// </summary>
    /// <param name="builder">The chat client pipeline builder.</param>
    /// <param name="options">The tracking options; a snapshot is taken when the client is built.</param>
    /// <param name="observer">An optional observer receiving out-of-band activity events.</param>
    /// <param name="loggerFactory">An optional logger factory for the middleware's structured logs.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public static ChatClientBuilder UseToolTracking(
        this ChatClientBuilder builder,
        ToolTrackingOptions options,
        IChatActivityObserver? observer = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        return builder.Use(innerClient => new ToolTrackingChatClient(innerClient, options, observer, loggerFactory));
    }

    private static IChatActivityObserver? CreateObserver(
        IEnumerable<IChatActivityObserver> observers,
        ILoggerFactory? loggerFactory)
    {
        IChatActivityObserver[] all = [.. observers];
        return all.Length switch
        {
            0 => null,
            1 => all[0],
            _ => new CompositeChatActivityObserver(
                all,
                loggerFactory?.CreateLogger(typeof(CompositeChatActivityObserver)) ?? NullLogger.Instance),
        };
    }
}
