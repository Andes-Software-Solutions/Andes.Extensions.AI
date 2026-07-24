using Andes.Extensions.AI.Unit.Test.Infrastructure;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Unit.Test;

public class BuilderAndOptionsTests
{
    [Fact]
    public async Task GetStreamingResponseAsync_CallerChatOptions_AreNotMutated()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "GetWeather"),
            ScriptedTurn.Text("Done."));
        AIFunction tool = AIFunctionFactory.Create(() => "sunny", "GetWeather");
        var options = new ChatOptions { Tools = [tool] };
        IChatClient client = TestPipeline.Build(scripted);

        await TestPipeline.CollectAsync(client, options);

        AITool remaining = Assert.Single(options.Tools!);
        Assert.Same(tool, remaining);
    }

    [Fact]
    public async Task UseToolTracking_ObserversRegisteredInServices_AreResolved()
    {
        var observer = new CollectingProgressObserver();
        var services = new SingleServiceProvider(
            typeof(IEnumerable<IChatProgressObserver>),
            new IChatProgressObserver[] { observer });
        var scripted = new ScriptedChatClient(ScriptedTurn.Text("Hello!"));
        IChatClient client = TestPipeline.Build(scripted, services: services);

        await TestPipeline.CollectAsync(client);

        Assert.Contains(observer.Updates, update => update.Kind == ChatProgressKind.RequestCompleted);
        Assert.NotNull(observer.Report);
    }

    [Fact]
    public void GetService_ThroughPipeline_ReachesBothMiddlewares()
    {
        var scripted = new ScriptedChatClient(ScriptedTurn.Text("Hello!"));
        IChatClient client = TestPipeline.Build(scripted);

        Assert.NotNull(client.GetService(typeof(ToolTrackingChatClient)));
        Assert.NotNull(client.GetService(typeof(FunctionInvokingChatClient)));
        Assert.NotNull(client.GetService(typeof(ChatClientMetadata)));
    }

    [Fact]
    public void UseToolTracking_NullBuilder_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((ChatClientBuilder)null!).UseToolTracking());
    }

    [Fact]
    public async Task PrivacyDefault_ToolArguments_NeverAppearInProgress()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "GetWeather", new Dictionary<string, object?> { ["city"] = "Quito" }),
            ScriptedTurn.Text("Done."));
        AIFunction tool = AIFunctionFactory.Create((string city) => $"sunny in {city}", "GetWeather");
        var observer = new CollectingProgressObserver();
        IChatClient client = TestPipeline.Build(scripted, options => options.Observers.Add(observer));

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });

        List<ChatProgressUpdate> allProgress = [.. TestPipeline.ProgressOf(updates), .. observer.Updates];
        Assert.All(allProgress, update => Assert.Null(update.Arguments));
        Assert.DoesNotContain(allProgress, update => update.Message.Contains("Quito", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IncludeToolArguments_Enabled_ExposesStringifiedArguments()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "GetWeather", new Dictionary<string, object?> { ["city"] = "Quito" }),
            ScriptedTurn.Text("Done."));
        AIFunction tool = AIFunctionFactory.Create((string city) => $"sunny in {city}", "GetWeather");
        IChatClient client = TestPipeline.Build(scripted, options => options.IncludeToolArguments = true);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        ChatProgressUpdate invoking = Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolInvoking);
        Assert.NotNull(invoking.Arguments);
        Assert.Contains("Quito", invoking.Arguments["city"], StringComparison.Ordinal);
    }

    private sealed class SingleServiceProvider(Type serviceType, object value) : IServiceProvider
    {
        public object? GetService(Type requested)
        {
            return requested == serviceType ? value : null;
        }
    }
}
