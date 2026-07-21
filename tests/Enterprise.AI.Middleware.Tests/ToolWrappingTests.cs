using Enterprise.AI.Middleware.Tests.TestDoubles;
using Enterprise.AI.Middleware.Tracking;
using Enterprise.AI.Middleware.Tracking.Internal;
using Microsoft.Extensions.AI;

namespace Enterprise.AI.Middleware.Tests;

public sealed class ToolWrappingTests
{
    private sealed class DummyHostedTool : AITool
    {
    }

    [Fact]
    public async Task GetStreamingResponseAsync_NonFunctionTool_PassesThroughUnwrapped()
    {
        var hostedTool = new DummyHostedTool();
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.TextUpdate("done"));
        IChatClient client = TestPipeline.Build(inner, new ToolTrackingOptions { EnableFinalUsageUpdate = false });

        await TestPipeline.DrainAsync(client.GetStreamingResponseAsync(
            TestPipeline.UserMessage("hi"),
            new ChatOptions { Tools = [hostedTool] }));

        ChatOptions? observed = Assert.Single(inner.ObservedOptions);
        Assert.Same(hostedTool, Assert.Single(observed!.Tools!));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_OriginalOptions_ToolsListLeftUnmodified()
    {
        AIFunction tool = AIFunctionFactory.Create(() => "ok", "original_tool");
        var originalOptions = new ChatOptions { Tools = [tool] };
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.TextUpdate("done"));
        IChatClient client = TestPipeline.Build(inner, new ToolTrackingOptions { EnableFinalUsageUpdate = false });

        await TestPipeline.DrainAsync(client.GetStreamingResponseAsync(
            TestPipeline.UserMessage("hi"),
            originalOptions));

        Assert.Same(tool, Assert.Single(originalOptions.Tools!));

        ChatOptions? observed = Assert.Single(inner.ObservedOptions);
        Assert.NotSame(originalOptions, observed);
        Assert.IsType<TrackedAIFunction>(Assert.Single(observed!.Tools!));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_AlreadyTrackedFunction_NotWrappedAgain()
    {
        AIFunction plain = AIFunctionFactory.Create(() => "ok", "once_tool");
        var alreadyTracked = new TrackedAIFunction(plain, new ToolClassification(ToolKind.Function, null));
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.FunctionCallUpdate("call-1", "once_tool"))
            .AddTurn(ScriptedChatClient.TextUpdate("done"));
        var observer = new RecordingObserver();
        IChatClient client = TestPipeline.Build(inner, observer: observer);

        await TestPipeline.DrainAsync(client.GetStreamingResponseAsync(
            TestPipeline.UserMessage("hi"),
            new ChatOptions { Tools = [alreadyTracked] }));

        Assert.Single(observer.EventsOfKind(ChatActivityEventKind.ToolCallStarted));

        ChatOptions? observed = inner.ObservedOptions[0];
        Assert.Same(alreadyTracked, Assert.Single(observed!.Tools!));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_NonStringActivityTagValue_Ignored()
    {
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.TextUpdate("done"));
        var observer = new RecordingObserver();
        IChatClient client = TestPipeline.Build(inner, observer: observer);
        var options = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [TrackingToolAnnotations.ActivityTagKey] = 42,
            },
        };

        await TestPipeline.DrainAsync(client.GetStreamingResponseAsync(
            TestPipeline.UserMessage("hi"),
            options));

        Assert.All(observer.Events, e => Assert.Null(e.ActivityTag));
    }

    [Fact]
    public async Task UseToolTracking_OptionsMutatedAfterBuild_SnapshotIsUnaffected()
    {
        var options = new ToolTrackingOptions();
        AIFunction tool = AIFunctionFactory.Create(() => "ok", "snapshot_tool");
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.FunctionCallUpdate("call-1", "snapshot_tool"))
            .AddTurn(ScriptedChatClient.TextUpdate("done"));
        var observer = new RecordingObserver();
        IChatClient client = TestPipeline.Build(inner, options, observer);

        options.Templates.PlainToolHeader = "Mutated After Build";
        options.EnableInBandStatusUpdates = false;

        await TestPipeline.DrainAsync(client.GetStreamingResponseAsync(
            TestPipeline.UserMessage("hi"),
            new ChatOptions { Tools = [tool] }));

        ChatActivityEvent started = observer.EventsOfKind(ChatActivityEventKind.ToolCallStarted).Single();
        Assert.Equal("Calling Tool(s)", started.Header);
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ToolTrackingChatClient(new ScriptedChatClient(), null!));
    }

    [Fact]
    public async Task GetResponseAsync_NullMessages_Throws()
    {
        IChatClient client = TestPipeline.Build(new ScriptedChatClient());

        await Assert.ThrowsAsync<ArgumentNullException>(() => client.GetResponseAsync((IEnumerable<ChatMessage>)null!));
    }

    [Fact]
    public void GetStreamingResponseAsync_NullMessages_ThrowsEagerly()
    {
        ToolTrackingChatClient client = new(new ScriptedChatClient(), new ToolTrackingOptions());

        Assert.Throws<ArgumentNullException>(() => client.GetStreamingResponseAsync(null!));
    }
}
