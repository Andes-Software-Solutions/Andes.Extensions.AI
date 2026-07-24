using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI;

/// <summary>
/// A delegating chat client that tracks token usage per request and per tool call, and propagates
/// progress events both in-band (as <see cref="ChatProgressContent"/> items in the streamed response)
/// and out-of-band (to <see cref="IChatProgressObserver"/> implementations).
/// </summary>
/// <remarks>
/// <para>
/// Register the client with
/// <see cref="ToolTrackingChatClientBuilderExtensions.UseToolTracking(ChatClientBuilder, Action{ToolTrackingOptions})"/>
/// <b>before</b> <c>UseFunctionInvocation()</c>: the tracker must sit outside the
/// <see cref="FunctionInvokingChatClient"/> so that it can wrap the <see cref="AIFunction"/> tools the
/// inner client executes and merge progress events into the stream while tools run.
/// </para>
/// <para>
/// Progress events and reports never carry prompt content, tool arguments, or tool results unless
/// <see cref="ToolTrackingOptions.IncludeToolArguments"/> is explicitly enabled.
/// </para>
/// </remarks>
public sealed class ToolTrackingChatClient : DelegatingChatClient
{
    /// <summary>
    /// The key under which the <see cref="ChatUsageReport"/> is attached to
    /// <see cref="ChatResponse.AdditionalProperties"/> when
    /// <see cref="ToolTrackingOptions.AttachReportToResponse"/> is enabled.
    /// </summary>
    public const string UsageReportPropertyName = "andes.ai.usage_report";

    private readonly ToolTrackingOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolTrackingChatClient"/> class.
    /// </summary>
    /// <param name="innerClient">The inner client to delegate to.</param>
    /// <param name="options">The tracking options, or <see langword="null"/> to use defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerClient"/> is <see langword="null"/>.</exception>
    public ToolTrackingChatClient(IChatClient innerClient, ToolTrackingOptions? options = null)
        : base(innerClient)
    {
        _options = options ?? new ToolTrackingOptions();
    }

    /// <inheritdoc/>
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        ToolScope? parentScope = AmbientScope.Current;
        var tracker = new RequestTracker(_options, GetMetadata(), writer: null);
        ChatOptions? effectiveOptions = WrapTools(options, tracker);

        tracker.EmitRequestStarted();
        tracker.EmitThinking();

        ChatResponse response;
        ToolScope? previous = AmbientScope.Current;
        AmbientScope.Current = tracker.RootScope;
        try
        {
            response = await base.GetResponseAsync(messages, effectiveOptions, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            NotifyRequestFailed(tracker, parentScope);
            throw;
        }
        finally
        {
            AmbientScope.Current = previous;
        }

        if (response.Usage is { } usage)
        {
            tracker.RecordAssistantUsage(usage, response.ResponseId, response.ModelId);
        }

        ChatUsageReport report = tracker.BuildReport();
        tracker.EmitRequestCompleted(report.Duration);
        tracker.NotifyRequestCompleted(report);
        parentScope?.AddUsage(report.TotalUsage);

        if (_options.AttachReportToResponse)
        {
            (response.AdditionalProperties ??= [])[UsageReportPropertyName] = report;
        }

        return response;
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        ToolScope? parentScope = AmbientScope.Current;
        var channel = Channel.CreateUnbounded<ChatResponseUpdate>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        var tracker = new RequestTracker(_options, GetMetadata(), channel.Writer);
        ChatOptions? effectiveOptions = WrapTools(options, tracker);

        tracker.EmitRequestStarted();
        tracker.EmitThinking();

        using var pumpCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken pumpToken = pumpCancellation.Token;

        // The ExecutionContext captured by Task.Run carries the root scope into the inner pipeline,
        // where the function-invocation loop executes the wrapped tools.
        Task pumpTask;
        ToolScope? previous = AmbientScope.Current;
        AmbientScope.Current = tracker.RootScope;
        try
        {
            pumpTask = Task.Run(
                () => PumpAsync(messages, effectiveOptions, tracker, channel.Writer, pumpToken),
                CancellationToken.None);
        }
        finally
        {
            AmbientScope.Current = previous;
        }

        bool drainedToCompletion = false;
        try
        {
            await foreach (ChatResponseUpdate update in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return update;
            }

            drainedToCompletion = true;
        }
        finally
        {
            // Stops the inner stream if the consumer abandoned the iterator; a no-op on normal completion.
            pumpCancellation.Cancel();
            await ObserveAsync(pumpTask).ConfigureAwait(false);

            // On fault, cancellation, or abandonment the request still consumed tokens: observers
            // must receive their once-per-request accounting and nested parents their rollup.
            if (!drainedToCompletion)
            {
                NotifyRequestFailed(tracker, parentScope);
            }
        }

        ChatUsageReport report = tracker.BuildReport();
        ChatProgressUpdate completed = tracker.CreateRequestCompletedUpdate(report.Duration);
        tracker.NotifyObservers(completed);
        tracker.NotifyRequestCompleted(report);
        parentScope?.AddUsage(report.TotalUsage);

        if (_options.EmitProgressContent)
        {
            yield return new ChatResponseUpdate(role: null, contents: [new ChatProgressContent(completed)]);
        }

        if (_options.EmitUsageReportContent)
        {
            yield return new ChatResponseUpdate(role: null, contents: [new UsageReportContent(report)]);
        }
    }

    private static void NotifyRequestFailed(RequestTracker tracker, ToolScope? parentScope)
    {
        ChatUsageReport report = tracker.BuildReport();
        tracker.NotifyObservers(tracker.CreateRequestFailedUpdate(report.Duration));
        tracker.NotifyRequestCompleted(report);
        parentScope?.AddUsage(report.TotalUsage);
    }

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // A pump failure has already been surfaced to the consumer through the channel completion.
        }
    }

    private async Task PumpAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        RequestTracker tracker,
        ChannelWriter<ChatResponseUpdate> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (ChatResponseUpdate update in InnerClient.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
            {
                bool completedToolRoundTrip = Inspect(update, tracker);
                writer.TryWrite(update);
                if (completedToolRoundTrip)
                {
                    tracker.AdvanceIteration();
                }
            }

            writer.TryComplete();
        }
        catch (Exception ex)
        {
            writer.TryComplete(ex);
        }
    }

    private static bool Inspect(ChatResponseUpdate update, RequestTracker tracker)
    {
        bool sawFunctionResult = false;
        foreach (AIContent content in update.Contents)
        {
            switch (content)
            {
                case UsageContent usage:
                    tracker.RecordAssistantTurnUsage(usage.Details, update.ResponseId, update.ModelId);
                    break;

                case FunctionCallContent call:
                    tracker.OnFunctionCall(call.CallId, call.Name);
                    break;

                case FunctionResultContent:
                    sawFunctionResult = true;
                    break;

                default:
                    break;
            }
        }

        return sawFunctionResult;
    }

    private ChatOptions? WrapTools(ChatOptions? options, RequestTracker tracker)
    {
        if (options?.Tools is not { Count: > 0 } originalTools)
        {
            return options;
        }

        ChatOptions cloned = options.Clone();
        var tools = new List<AITool>(originalTools.Count);
        foreach (AITool tool in originalTools)
        {
            if (tool is AIFunction function)
            {
                ToolDescriptor descriptor = Classify(function);

                // Register the function's real name: FunctionCallContent in the stream carries the
                // wrapped function's identity, not the (possibly renamed) descriptor name.
                tracker.RegisterWrappedTool(function.Name);
                tools.Add(new TrackingAIFunction(function, descriptor, tracker));
            }
            else
            {
                tools.Add(tool);
            }
        }

        cloned.Tools = tools;
        return cloned;
    }

    private ToolDescriptor Classify(AITool tool)
    {
        if (_options.ToolClassifier is { } classifier)
        {
            return classifier(tool)
                ?? throw new InvalidOperationException($"{nameof(ToolTrackingOptions.ToolClassifier)} returned null.");
        }

        return ToolDescriptor.CreateDefault(tool);
    }

    private ChatClientMetadata? GetMetadata()
    {
        return InnerClient.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata;
    }
}
