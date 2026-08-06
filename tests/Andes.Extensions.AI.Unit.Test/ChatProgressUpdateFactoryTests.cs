using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Unit.Test;

public class ChatProgressUpdateFactoryTests
{
    [Fact]
    public void CreateRequestStarted_Default_PopulatesWellKnownFields()
    {
        DateTimeOffset before = DateTimeOffset.UtcNow;
        ChatProgressUpdate update = ChatProgressUpdate.CreateRequestStarted();
        DateTimeOffset after = DateTimeOffset.UtcNow;

        Assert.Equal(ChatProgressKind.RequestStarted, update.Kind);
        Assert.Equal("Starting request", update.Message);
        Assert.Equal(ChatProgressUpdate.ExternalScopeId, update.ScopeId);
        Assert.Equal(0, update.Depth);
        Assert.InRange(update.Timestamp, before, after);
    }

    [Fact]
    public void CreateRequestStarted_CustomMessage_UsesIt()
    {
        ChatProgressUpdate update = ChatProgressUpdate.CreateRequestStarted("Warming up…");

        Assert.Equal(ChatProgressKind.RequestStarted, update.Kind);
        Assert.Equal("Warming up…", update.Message);
    }

    [Fact]
    public void CreateReasoning_Default_PopulatesWellKnownFields()
    {
        DateTimeOffset before = DateTimeOffset.UtcNow;
        ChatProgressUpdate update = ChatProgressUpdate.CreateReasoning();
        DateTimeOffset after = DateTimeOffset.UtcNow;

        Assert.Equal(ChatProgressKind.Reasoning, update.Kind);
        Assert.Equal("Reasoning...", update.Message);
        Assert.Equal(ChatProgressUpdate.ExternalScopeId, update.ScopeId);
        Assert.Equal(0, update.Depth);
        Assert.InRange(update.Timestamp, before, after);
    }

    [Fact]
    public void CreateReasoning_CustomMessage_UsesIt()
    {
        ChatProgressUpdate update = ChatProgressUpdate.CreateReasoning("Pondering deeply…");

        Assert.Equal(ChatProgressKind.Reasoning, update.Kind);
        Assert.Equal("Pondering deeply…", update.Message);
    }

    [Fact]
    public void ToResponseUpdate_Always_WrapsSingleProgressContent()
    {
        ChatProgressUpdate update = ChatProgressUpdate.CreateRequestStarted();

        ChatResponseUpdate wrapped = update.ToResponseUpdate();

        Assert.Null(wrapped.Role);
        ChatProgressContent content = Assert.IsType<ChatProgressContent>(Assert.Single(wrapped.Contents));
        Assert.Same(update, content.Progress);
        Assert.Empty(wrapped.Text);
    }

    [Fact]
    public void ToResponseUpdate_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((ChatProgressUpdate)null!).ToResponseUpdate());
    }
}
