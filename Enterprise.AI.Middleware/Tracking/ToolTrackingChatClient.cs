using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Enterprise.AI.Middleware.Tracking.Internal;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Enterprise.AI.Middleware.Tracking;

/// <summary>
/// A delegating <see cref="IChatClient"/> middleware that tracks every tool invocation — plain
/// functions, Microsoft Agent Framework agents exposed as tools, and MCP tools — emitting
/// hierarchical status updates and granular token usage both in-band (as
/// <see cref="ActivityStatusContent"/> items in the streaming response) and out-of-band (to an
/// <see cref="IChatActivityObserver"/>).
/// </summary>
/// <remarks>
/// <para>
/// Register the middleware <b>before</b> function invocation so it wraps the tool list the
/// <see cref="FunctionInvokingChatClient"/> executes.
/// </para>
/// <para>
/// When a request executes inside an already tracked flow (for example an agent tool running its
/// own tracked pipeline), the middleware joins the ambient request: usage is attributed to the
/// enclosing tool-call scope and no in-band updates are injected into the nested stream.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp">
/// IChatClient client = new ChatClientBuilder(providerClient)
///     .UseToolTracking()
///     .UseFunctionInvocation()
///     .Build(serviceProvider);
/// </code>
/// </example>
public sealed class ToolTrackingChatClient : DelegatingChatClient
{
    private readonly ToolTrackingOptions _options;
    private readonly IChatActivityObserver? _observer;
    private readonly ILogger _logger;
    private readonly ChatClientMetadata? _metadata;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolTrackingChatClient"/> class.
    /// </summary>
    /// <param name="innerClient">The next client in the pipeline.</param>
    /// <param name="options">The tracking options; a snapshot is taken at construction time.</param>
    /// <param name="observer">An optional observer receiving out-of-band activity events.</param>
    /// <param name="loggerFactory">An optional logger factory for the middleware's structured logs.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerClient"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public ToolTrackingChatClient(
        IChatClient innerClient,
        ToolTrackingOptions options,
        IChatActivityObserver? observer = null,
        ILoggerFactory? loggerFactory = null)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Clone();
        _observer = observer;
        _logger = (ILogger?)loggerFactory?.CreateLogger<ToolTrackingChatClient>() ?? NullLogger.Instance;
        _metadata = innerClient.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata;
    }

    /// <inheritdoc/>
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        ChatOptions? effectiveOptions = WrapTools(options);
        (ActivityFlowState flow, bool isRoot) = CreateFlow(ChatActivityScope.CurrentFlow, ReadActivityTag(options));
        ActivityContext context = flow.Context;
        ActivityFlowState? previous = ChatActivityScope.CurrentFlow;
        ChatActivityScope.CurrentFlow = flow;
        if (isRoot)
        {
            context.PublishRequestStarted();
        }

        try
        {
            ChatResponse response = await base.GetResponseAsync(messages, effectiveOptions, cancellationToken)
                .ConfigureAwait(false);

            if (response.Usage is { } usage)
            {
                context.RecordUsage(flow.Scope, usage, response.ModelId ?? DefaultModelId, ProviderName);
            }

            if (isRoot)
            {
                ChatActivityReport report = context.Complete(error: null);
                response.AdditionalProperties ??= new AdditionalPropertiesDictionary();
                response.AdditionalProperties[TrackingToolAnnotations.ActivityReportKey] = report;
            }
            else
            {
                context.CompleteScope(flow.Scope, error: null);
            }

            return response;
        }
        catch (Exception ex)
        {
            if (isRoot)
            {
                context.Complete(ex);
            }
            else
            {
                context.CompleteScope(flow.Scope, ex);
            }

            throw;
        }
        finally
        {
            ChatActivityScope.CurrentFlow = previous;
        }
    }

    /// <inheritdoc/>
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        ChatOptions? effectiveOptions = WrapTools(options);
        ActivityFlowState? ambient = ChatActivityScope.CurrentFlow;
        string? activityTag = ReadActivityTag(options);
        return StreamAsync(messages, effectiveOptions, ambient, activityTag, cancellationToken);
    }

    private async IAsyncEnumerable<ChatResponseUpdate> StreamAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? effectiveOptions,
        ActivityFlowState? ambient,
        string? activityTag,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        (ActivityFlowState flow, bool isRoot) = CreateFlow(ambient, activityTag);
        ActivityContext context = flow.Context;
        Channel<ChatResponseUpdate> channel = Channel.CreateUnbounded<ChatResponseUpdate>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        if (isRoot)
        {
            if (_options.EnableInBandStatusUpdates)
            {
                context.AttachInBandWriter(channel.Writer);
            }

            context.PublishRequestStarted();
        }

        using var pumpCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task pump = PumpInnerResponsesAsync(messages, effectiveOptions, flow, channel.Writer, pumpCancellation.Token);

        Exception? failure = null;
        bool completedNormally = false;
        try
        {
            while (true)
            {
                ChatResponseUpdate? update;
                try
                {
                    if (!await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        completedNormally = true;
                        break;
                    }

                    if (!channel.Reader.TryRead(out update))
                    {
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                    throw;
                }

                yield return update;
            }
        }
        finally
        {
            await pumpCancellation.CancelAsync().ConfigureAwait(false);
            try
            {
                await pump.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The pump's outcome has already been observed through the channel; a secondary
                // failure here must not mask the primary one.
            }

            if (failure is null && !completedNormally)
            {
                failure = new OperationCanceledException("The streaming response was abandoned before completion.");
            }

            if (isRoot)
            {
                context.Complete(failure);
            }
            else
            {
                context.CompleteScope(flow.Scope, failure);
            }
        }

        if (isRoot && _options.EnableFinalUsageUpdate)
        {
            ChatActivityReport report = context.Complete(error: null);
            yield return CreateFinalUsageUpdate(report);
        }
    }

    private async Task PumpInnerResponsesAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? effectiveOptions,
        ActivityFlowState flow,
        ChannelWriter<ChatResponseUpdate> writer,
        CancellationToken cancellationToken)
    {
        ActivityFlowState? previous = ChatActivityScope.CurrentFlow;
        ChatActivityScope.CurrentFlow = flow;
        try
        {
            await foreach (ChatResponseUpdate update in base
                .GetStreamingResponseAsync(messages, effectiveOptions, cancellationToken)
                .ConfigureAwait(false))
            {
                foreach (AIContent content in update.Contents)
                {
                    if (content is UsageContent usageContent)
                    {
                        flow.Context.RecordUsage(
                            flow.Scope,
                            usageContent.Details,
                            update.ModelId ?? DefaultModelId,
                            ProviderName);
                    }
                }

                writer.TryWrite(update);
            }

            writer.TryComplete();
        }
        catch (Exception ex)
        {
            writer.TryComplete(ex);
        }
        finally
        {
            ChatActivityScope.CurrentFlow = previous;
        }
    }

    private (ActivityFlowState Flow, bool IsRoot) CreateFlow(ActivityFlowState? ambient, string? activityTag)
    {
        if (ambient is null)
        {
            var context = new ActivityContext(_options, _observer, _logger, activityTag);
            return (new ActivityFlowState(context, context.RootScope), true);
        }

        ActivityScope nestedScope = ambient.Context.BeginModelScope(ambient.Scope);
        return (new ActivityFlowState(ambient.Context, nestedScope), false);
    }

    private ChatOptions? WrapTools(ChatOptions? options)
    {
        if (options?.Tools is not { Count: > 0 } tools)
        {
            return options;
        }

        var wrapped = new List<AITool>(tools.Count);
        bool changed = false;
        foreach (AITool tool in tools)
        {
            if (tool is AIFunction function and not TrackedAIFunction)
            {
                ToolClassification classification = ToolClassifier.Classify(function, _options);
                wrapped.Add(new TrackedAIFunction(function, classification));
                changed = true;
            }
            else
            {
                wrapped.Add(tool);
            }
        }

        if (!changed)
        {
            return options;
        }

        ChatOptions effective = options.Clone();
        effective.Tools = wrapped;
        return effective;
    }

    private static string? ReadActivityTag(ChatOptions? options)
    {
        if (options?.AdditionalProperties is { } properties &&
            properties.TryGetValue(TrackingToolAnnotations.ActivityTagKey, out object? value) &&
            value is string tag)
        {
            return tag;
        }

        return null;
    }

    private ChatResponseUpdate CreateFinalUsageUpdate(ChatActivityReport report)
    {
        var content = new ActivityStatusContent
        {
            Kind = ChatActivityEventKind.RequestCompleted,
            ScopeId = report.Root.ScopeId,
            Timestamp = _options.TimeProvider.GetUtcNow(),
            Usage = report.TotalUsage,
        };

        return new ChatResponseUpdate(ChatRole.Assistant, [content]);
    }

    private string? ProviderName => _metadata?.ProviderName;

    private string? DefaultModelId => _metadata?.DefaultModelId;
}
