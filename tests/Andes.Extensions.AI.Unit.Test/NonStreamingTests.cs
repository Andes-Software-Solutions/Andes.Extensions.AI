using Andes.Extensions.AI.Unit.Test.Infrastructure;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Unit.Test;

public class NonStreamingTests
{
    [Fact]
    public async Task GetResponseAsync_ToolLoop_NotifiesObserversInOrder()
    {
        var observer = new CollectingProgressObserver();
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "GetWeather"),
            ScriptedTurn.Text("It's sunny.", new UsageDetails { TotalTokenCount = 40 }));
        AIFunction tool = AIFunctionFactory.Create(
            () =>
            {
                ChatProgress.Report("Processing...");
                return "sunny";
            },
            "GetWeather");
        IChatClient client = TestPipeline.Build(scripted, options => options.Observers.Add(observer));

        ChatResponse response = await client.GetResponseAsync("prompt", new ChatOptions { Tools = [tool] });

        List<ChatProgressKind> kinds = observer.Updates.Select(update => update.Kind).ToList();
        Assert.Equal(ChatProgressKind.RequestStarted, kinds[0]);
        Assert.Equal(ChatProgressKind.Thinking, kinds[1]);
        Assert.Contains(ChatProgressKind.ToolInvoking, kinds);
        Assert.Contains(ChatProgressKind.ToolProgress, kinds);
        Assert.Contains(ChatProgressKind.ToolCompleted, kinds);
        Assert.Equal(ChatProgressKind.RequestCompleted, kinds[^1]);
        Assert.NotNull(observer.Report);
        Assert.Contains("It's sunny.", response.Text);
    }

    [Fact]
    public async Task GetResponseAsync_Default_AttachesReportToAdditionalProperties()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.Text("Hello!", new UsageDetails { InputTokenCount = 8, OutputTokenCount = 4, TotalTokenCount = 12 }));
        IChatClient client = TestPipeline.Build(scripted);

        ChatResponse response = await client.GetResponseAsync("prompt");

        Assert.NotNull(response.AdditionalProperties);
        var report = Assert.IsType<ChatUsageReport>(response.AdditionalProperties[ToolTrackingChatClient.UsageReportPropertyName]);
        Assert.Equal(12, report.AssistantUsage.TotalTokenCount);
        Assert.Equal(12, report.TotalUsage.TotalTokenCount);
        Assert.Empty(report.Turns);
    }

    [Fact]
    public async Task GetResponseAsync_AttachReportDisabled_LeavesAdditionalPropertiesAlone()
    {
        var scripted = new ScriptedChatClient(ScriptedTurn.Text("Hello!"));
        IChatClient client = TestPipeline.Build(scripted, options => options.AttachReportToResponse = false);

        ChatResponse response = await client.GetResponseAsync("prompt");

        Assert.True(response.AdditionalProperties is null
            || !response.AdditionalProperties.ContainsKey(ToolTrackingChatClient.UsageReportPropertyName));
    }

    [Fact]
    public async Task GetResponseAsync_InnerClientThrows_ObserversStillReceiveAccounting()
    {
        var observer = new CollectingProgressObserver();
        var scripted = new ScriptedChatClient();
        IChatClient client = TestPipeline.Build(scripted, options => options.Observers.Add(observer));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync("prompt"));

        Assert.Contains(observer.Updates, update => update.Kind == ChatProgressKind.RequestFailed);
        Assert.DoesNotContain(observer.Updates, update => update.Kind == ChatProgressKind.RequestCompleted);
        Assert.NotNull(observer.Report);
    }

    [Fact]
    public async Task GetResponseAsync_Always_LeavesMessagesFreeOfSyntheticContent()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "GetWeather"),
            ScriptedTurn.Text("It's sunny."));
        AIFunction tool = AIFunctionFactory.Create(() => "sunny", "GetWeather");
        IChatClient client = TestPipeline.Build(scripted);

        ChatResponse response = await client.GetResponseAsync("prompt", new ChatOptions { Tools = [tool] });

        Assert.Empty(response.Messages.SelectMany(message => message.Contents).OfType<ChatProgressContent>());
        Assert.Empty(response.Messages.SelectMany(message => message.Contents).OfType<UsageReportContent>());
    }
}
