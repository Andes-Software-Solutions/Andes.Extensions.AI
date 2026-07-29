using Andes.Extensions.AI.Unit.Test.Infrastructure;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Unit.Test;

public class ChatProgressToolScopeTests
{
    [Fact]
    public void BeginToolScope_OutsideTrackedRequest_ReturnsInactiveNoOp()
    {
        using ChatProgressToolScope scope = ChatProgress.BeginToolScope(new ToolDescriptor { Name = "nested" });

        Assert.False(scope.IsActive);
        Assert.Null(scope.ScopeId);
        scope.Fail();
        scope.Dispose();
    }

    [Fact]
    public void BeginToolScope_NullDescriptor_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ChatProgress.BeginToolScope(null!));
    }

    [Fact]
    public async Task BeginToolScope_InsideTool_EmitsChildInvokingWithParentScopeAndDepth()
    {
        bool wasActive = false;
        AIFunction tool = AIFunctionFactory.Create(
            () =>
            {
                using ChatProgressToolScope scope = ChatProgress.BeginToolScope(
                    new ToolDescriptor { Name = "nested", DisplayName = "Nested", Kind = ToolKind.Function });
                wasActive = scope.IsActive;
                return "done";
            },
            "Outer");
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Outer"),
            ScriptedTurn.Text("Done."));
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        Assert.True(wasActive);
        List<ChatProgressUpdate> invoking = progress.Where(update => update.Kind == ChatProgressKind.ToolInvoking).ToList();
        Assert.Equal(2, invoking.Count);
        ChatProgressUpdate outer = invoking[0];
        ChatProgressUpdate child = invoking[1];
        Assert.Equal("nested", child.ToolName);
        Assert.Equal(outer.ScopeId, child.ParentScopeId);
        Assert.Equal(outer.Depth + 1, child.Depth);

        ChatProgressUpdate completed = Assert.Single(
            progress,
            update => update.Kind == ChatProgressKind.ToolCompleted && update.ScopeId == child.ScopeId);
        Assert.NotNull(completed.Duration);
    }

    [Fact]
    public async Task BeginToolScope_OwnerMatchesAmbientScope_OpensNoSecondScope()
    {
        AIFunction inner = AIFunctionFactory.Create(() => "done", "Nested");
        var wrapper = new ChildScopeFunction(inner, new ToolDescriptor { Name = "Nested" });
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Nested"),
            ScriptedTurn.Text("Done."));
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [wrapper] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolInvoking);
        Assert.False(wrapper.LastScopeWasActive);
    }

    [Fact]
    public async Task BeginToolScope_OwnerBehindUserDelegatingWrapper_StillDeduplicates()
    {
        AIFunction inner = AIFunctionFactory.Create(() => "done", "Nested");
        var wrapper = new ChildScopeFunction(inner, new ToolDescriptor { Name = "Nested" });
        var userWrapped = new PassThroughFunction(wrapper);
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Nested"),
            ScriptedTurn.Text("Done."));
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [userWrapped] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolInvoking);
        Assert.False(wrapper.LastScopeWasActive);
    }

    [Fact]
    public async Task BeginToolScope_NullOwner_AlwaysOpensScope()
    {
        AIFunction inner = AIFunctionFactory.Create(() => "done", "Nested");
        var wrapper = new ChildScopeFunction(inner, new ToolDescriptor { Name = "Nested" }, passOwner: false);
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Nested"),
            ScriptedTurn.Text("Done."));
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [wrapper] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        Assert.Equal(2, progress.Count(update => update.Kind == ChatProgressKind.ToolInvoking));
        Assert.True(wrapper.LastScopeWasActive);
    }

    [Fact]
    public async Task BeginToolScope_Fail_EmitsToolFailedAndReportMarksChildFailed()
    {
        string? childScopeId = null;
        AIFunction tool = AIFunctionFactory.Create(
            () =>
            {
                using ChatProgressToolScope scope = ChatProgress.BeginToolScope(
                    new ToolDescriptor { Name = "nested", DisplayName = "Nested" });
                childScopeId = scope.ScopeId;
                scope.Fail();
                return "done";
            },
            "Outer");
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Outer"),
            ScriptedTurn.Text("Done."));
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);
        ChatUsageReport report = TestPipeline.ReportOf(updates);

        Assert.NotNull(childScopeId);
        Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolFailed && update.ScopeId == childScopeId);
        ToolCallUsage call = Assert.Single(report.ToolCalls);
        Assert.True(call.Succeeded);
        ToolCallUsage child = Assert.Single(call.Children);
        Assert.False(child.Succeeded);
    }

    [Fact]
    public async Task BeginToolScope_ReportUsageInsideChild_AttributedToChildAndRolledUpToParent()
    {
        AIFunction tool = AIFunctionFactory.Create(
            () =>
            {
                ChatProgress.ReportUsage(new UsageDetails { TotalTokenCount = 2 });
                using (ChatProgress.BeginToolScope(new ToolDescriptor { Name = "nested" }))
                {
                    ChatProgress.ReportUsage(new UsageDetails { TotalTokenCount = 5 });
                }

                return "done";
            },
            "Outer");
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Outer"),
            ScriptedTurn.Text("Done.", new UsageDetails { TotalTokenCount = 20 }));
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });
        ChatUsageReport report = TestPipeline.ReportOf(updates);

        ToolCallUsage call = Assert.Single(report.ToolCalls);
        Assert.NotNull(call.Usage);
        Assert.Equal(7, call.Usage.TotalTokenCount);
        ToolCallUsage child = Assert.Single(call.Children);
        Assert.NotNull(child.Usage);
        Assert.Equal(5, child.Usage.TotalTokenCount);
        Assert.Equal(27, report.TotalUsage.TotalTokenCount);
    }

    [Fact]
    public async Task BeginToolScope_SubStatusInsideChild_AttachesToChildScope()
    {
        string? childScopeId = null;
        AIFunction tool = AIFunctionFactory.Create(
            () =>
            {
                using ChatProgressToolScope scope = ChatProgress.BeginToolScope(new ToolDescriptor { Name = "nested" });
                childScopeId = scope.ScopeId;
                ChatProgress.Report("Working…");
                return "done";
            },
            "Outer");
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Outer"),
            ScriptedTurn.Text("Done."));
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        ChatProgressUpdate childInvoking = Assert.Single(
            progress,
            update => update.Kind == ChatProgressKind.ToolInvoking && update.ScopeId == childScopeId);
        ChatProgressUpdate subStatus = Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolProgress);
        Assert.Equal(childScopeId, subStatus.ScopeId);
        Assert.Equal(childInvoking.Depth + 1, subStatus.Depth);
    }

    [Fact]
    public async Task BeginToolScope_NonStreamingRequest_ReportContainsChildren()
    {
        AIFunction tool = AIFunctionFactory.Create(
            () =>
            {
                using (ChatProgress.BeginToolScope(new ToolDescriptor { Name = "nested" }))
                {
                    ChatProgress.ReportUsage(new UsageDetails { TotalTokenCount = 5 });
                }

                return "done";
            },
            "Outer");
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Outer"),
            ScriptedTurn.Text("Done."));
        IChatClient client = TestPipeline.Build(scripted);

        ChatResponse response = await client.GetResponseAsync("prompt", new ChatOptions { Tools = [tool] });

        ChatUsageReport report = Assert.IsType<ChatUsageReport>(
            response.AdditionalProperties?[ToolTrackingChatClient.UsageReportPropertyName]);
        ToolCallUsage call = Assert.Single(report.ToolCalls);
        ToolCallUsage child = Assert.Single(call.Children);
        Assert.Equal("nested", child.ToolName);
        Assert.NotNull(child.Usage);
        Assert.Equal(5, child.Usage.TotalTokenCount);
    }

    [Fact]
    public async Task BeginToolScope_OwnerNotCurrentFunctionContext_CallIdIsNull()
    {
        AIFunction inner = AIFunctionFactory.Create(() => "leaf", "Nested");
        var nestedFunction = new ChildScopeFunction(inner, new ToolDescriptor { Name = "Nested" });
        AIFunction outerTool = AIFunctionFactory.Create(
            async (CancellationToken cancellationToken) =>
                (string?)await nestedFunction.InvokeAsync(cancellationToken: cancellationToken),
            "Outer");
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Outer"),
            ScriptedTurn.Text("Done."));
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [outerTool] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        List<ChatProgressUpdate> invoking = progress.Where(update => update.Kind == ChatProgressKind.ToolInvoking).ToList();
        Assert.Equal(2, invoking.Count);
        Assert.Equal("call-1", invoking[0].CallId);
        Assert.Equal("Nested", invoking[1].ToolName);
        Assert.Null(invoking[1].CallId);
        Assert.Equal(invoking[0].ScopeId, invoking[1].ParentScopeId);
    }

    [Fact]
    public async Task Dispose_CalledTwiceOnActiveHandle_EmitsSingleCompletion()
    {
        string? childScopeId = null;
        AIFunction tool = AIFunctionFactory.Create(
            () =>
            {
                ChatProgressToolScope scope = ChatProgress.BeginToolScope(new ToolDescriptor { Name = "nested" });
                childScopeId = scope.ScopeId;
                scope.Dispose();
                scope.Dispose();
                return "done";
            },
            "Outer");
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Outer"),
            ScriptedTurn.Text("Done."));
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        Assert.NotNull(childScopeId);
        Assert.Single(
            progress,
            update => update.Kind is ChatProgressKind.ToolCompleted or ChatProgressKind.ToolFailed
                && update.ScopeId == childScopeId);
    }

    [Fact]
    public async Task BeginToolScope_AfterChildDisposed_AmbientRestoredToToolScope()
    {
        string? childScopeId = null;
        AIFunction tool = AIFunctionFactory.Create(
            () =>
            {
                using (ChatProgressToolScope scope = ChatProgress.BeginToolScope(new ToolDescriptor { Name = "nested" }))
                {
                    childScopeId = scope.ScopeId;
                }

                ChatProgress.Report("after");
                return "done";
            },
            "Outer");
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Outer"),
            ScriptedTurn.Text("Done."));
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [tool] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        ChatProgressUpdate outerInvoking = Assert.Single(
            progress,
            update => update.Kind == ChatProgressKind.ToolInvoking && update.ToolName == "Outer");
        ChatProgressUpdate after = Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolProgress);
        Assert.Equal(outerInvoking.ScopeId, after.ScopeId);
        Assert.NotEqual(childScopeId, after.ScopeId);
    }

    [Fact]
    public async Task BeginToolScope_RecursiveSelfInvocation_StaysFlat()
    {
        AIFunction? wrapper = null;
        AIFunction inner = AIFunctionFactory.Create(
            async (bool recurse, CancellationToken cancellationToken) =>
            {
                if (recurse)
                {
                    await wrapper!.InvokeAsync(new AIFunctionArguments { ["recurse"] = false }, cancellationToken);
                }

                return "done";
            },
            "Nested");
        wrapper = new ChildScopeFunction(inner, new ToolDescriptor { Name = "Nested" });
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Nested", new Dictionary<string, object?> { ["recurse"] = true }),
            ScriptedTurn.Text("Done."));
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [wrapper] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        // The recursive invocation's owner matches the ambient scope's owner, so no child opens —
        // the documented recursion limitation.
        Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolInvoking);
    }

    private sealed class ChildScopeFunction(AIFunction innerFunction, ToolDescriptor descriptor, bool passOwner = true)
        : DelegatingAIFunction(innerFunction)
    {
        public bool LastScopeWasActive { get; private set; }

        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            using ChatProgressToolScope scope = ChatProgress.BeginToolScope(descriptor, owner: passOwner ? this : null);
            LastScopeWasActive = scope.IsActive;
            try
            {
                return await base.InvokeCoreAsync(arguments, cancellationToken);
            }
            catch
            {
                scope.Fail();
                throw;
            }
        }
    }

    private sealed class PassThroughFunction(AIFunction innerFunction) : DelegatingAIFunction(innerFunction);
}
