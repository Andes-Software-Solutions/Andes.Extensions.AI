using Andes.Extensions.AI.Unit.Test.Infrastructure;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Unit.Test;

public class StripProgressContentTests
{
    [Fact]
    public async Task StripProgressContent_AfterToChatResponse_RemovesAllSyntheticContent()
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "GetWeather"),
            ScriptedTurn.Text("It's sunny."));
        AIFunction tool = AIFunctionFactory.Create(
            () =>
            {
                ChatProgress.Report("Extracting...");
                return "sunny";
            },
            "GetWeather");
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });
        ChatResponse response = updates.ToChatResponse();

        Assert.NotEmpty(response.Messages.SelectMany(message => message.Contents).OfType<ChatProgressContent>());

        response.StripProgressContent();

        Assert.Empty(response.Messages.SelectMany(message => message.Contents).OfType<ChatProgressContent>());
        Assert.Empty(response.Messages.SelectMany(message => message.Contents).OfType<UsageReportContent>());
        Assert.Contains("It's sunny.", response.Text);
    }

    [Fact]
    public void StripProgressContent_OnMessage_RemovesOnlySyntheticItems()
    {
        var progress = new ChatProgressContent(new ChatProgressUpdate
        {
            Kind = ChatProgressKind.Thinking,
            Message = "Thinking...",
            ScopeId = "scope-1",
        });
        var message = new ChatMessage(ChatRole.Assistant, [new TextContent("keep me"), progress]);

        message.StripProgressContent();

        AIContent remaining = Assert.Single(message.Contents);
        Assert.IsType<TextContent>(remaining);
    }

    [Fact]
    public async Task StripProgressContent_OnNonStreamingResponse_RemovesAttachedReport()
    {
        var scripted = new ScriptedChatClient(ScriptedTurn.Text("Hello!"));
        IChatClient client = TestPipeline.Build(scripted);
        ChatResponse response = await client.GetResponseAsync("prompt");
        Assert.True(response.AdditionalProperties!.ContainsKey(ToolTrackingChatClient.UsageReportPropertyName));

        response.StripProgressContent();

        Assert.False(response.AdditionalProperties.ContainsKey(ToolTrackingChatClient.UsageReportPropertyName));
    }

    [Fact]
    public void StripProgressContent_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => ((ChatResponse)null!).StripProgressContent());
        Assert.Throws<ArgumentNullException>(() => ((ChatMessage)null!).StripProgressContent());
    }
}
