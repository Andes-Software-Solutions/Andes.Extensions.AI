using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Unit.Test;

public class ChatProgressUpdateFactoryTests
{
    [Fact]
    public void CreateCustom_Message_PopulatesWellKnownFields()
    {
        DateTimeOffset before = DateTimeOffset.UtcNow;
        ChatProgressUpdate update = ChatProgressUpdate.CreateCustom("Warming up…");
        DateTimeOffset after = DateTimeOffset.UtcNow;

        Assert.Equal(ChatProgressKind.Custom, update.Kind);
        Assert.Equal("Warming up…", update.Message);
        Assert.Equal(ChatProgressUpdate.ExternalScopeId, update.ScopeId);
        Assert.Equal(0, update.Depth);
        Assert.InRange(update.Timestamp, before, after);
    }

    [Fact]
    public void CreateCustom_NullMessage_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ChatProgressUpdate.CreateCustom(null!));
    }

    [Fact]
    public void CreateCustom_EmptyMessage_Throws()
    {
        Assert.Throws<ArgumentException>(() => ChatProgressUpdate.CreateCustom(string.Empty));
    }

    [Fact]
    public void ToResponseUpdate_Always_WrapsSingleProgressContent()
    {
        ChatProgressUpdate update = ChatProgressUpdate.CreateCustom("Starting request");

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
