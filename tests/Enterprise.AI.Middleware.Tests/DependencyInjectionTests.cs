using Enterprise.AI.Middleware.Tests.TestDoubles;
using Enterprise.AI.Middleware.Tracking;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Enterprise.AI.Middleware.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public async Task AddChatActivityTracking_UseToolTracking_ResolvesOptionsAndAllObservers()
    {
        var firstObserver = new RecordingObserver();
        var secondObserver = new RecordingObserver();
        ServiceProvider provider = new ServiceCollection()
            .AddChatActivityTracking(options => options.Templates.PlainToolHeader = "Custom Header")
            .AddSingleton<IChatActivityObserver>(firstObserver)
            .AddSingleton<IChatActivityObserver>(secondObserver)
            .BuildServiceProvider();

        AIFunction tool = AIFunctionFactory.Create(() => "ok", "di_tool");
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.FunctionCallUpdate("call-1", "di_tool"))
            .AddTurn(ScriptedChatClient.TextUpdate("done"));
        IChatClient client = new ChatClientBuilder(inner)
            .UseToolTracking()
            .UseFunctionInvocation()
            .Build(provider);

        await client.GetResponseAsync(
            TestPipeline.UserMessage("go"),
            new ChatOptions { Tools = [tool] });

        foreach (RecordingObserver observer in new[] { firstObserver, secondObserver })
        {
            ChatActivityEvent started = observer.EventsOfKind(ChatActivityEventKind.ToolCallStarted).Single();
            Assert.Equal("Custom Header", started.Header);
            Assert.Single(observer.Reports);
        }
    }

    [Fact]
    public async Task UseToolTracking_ConfigureOverloadWithoutDI_UsesConfiguredOptions()
    {
        ScriptedChatClient inner = new ScriptedChatClient()
            .AddTurn(ScriptedChatClient.TextUpdate("hello"));
        IChatClient client = new ChatClientBuilder(inner)
            .UseToolTracking(options => options.EnableFinalUsageUpdate = false)
            .UseFunctionInvocation()
            .Build();

        List<ChatResponseUpdate> updates = await TestPipeline.DrainAsync(
            client.GetStreamingResponseAsync(TestPipeline.UserMessage("hi")));

        Assert.Empty(TestPipeline.StatusContents(updates));
    }

    [Fact]
    public void UseToolTracking_BeforeFunctionInvocation_TrackerIsOutermost()
    {
        ScriptedChatClient inner = new ScriptedChatClient();

        IChatClient client = new ChatClientBuilder(inner)
            .UseToolTracking(new ToolTrackingOptions())
            .UseFunctionInvocation()
            .Build();

        Assert.IsType<ToolTrackingChatClient>(client);
    }

    [Fact]
    public void WithActivityTag_SetsWellKnownAdditionalProperty()
    {
        ChatOptions options = new ChatOptions().WithActivityTag("tag-1");

        Assert.Equal("tag-1", options.AdditionalProperties![TrackingToolAnnotations.ActivityTagKey]);
    }

    [Fact]
    public void RemoveActivityContent_MixedMessages_StripsOnlyActivityContent()
    {
        List<ChatMessage> messages =
        [
            new ChatMessage(ChatRole.Assistant,
            [
                new TextContent("hello"),
                new ActivityStatusContent { Kind = ChatActivityEventKind.ToolCallStarted, Header = "Calling Tool(s)" },
            ]),
            new ChatMessage(ChatRole.User, [new TextContent("hi")]),
        ];

        messages.RemoveActivityContent();

        Assert.Single(messages[0].Contents);
        Assert.IsType<TextContent>(messages[0].Contents[0]);
        Assert.Single(messages[1].Contents);
    }
}
