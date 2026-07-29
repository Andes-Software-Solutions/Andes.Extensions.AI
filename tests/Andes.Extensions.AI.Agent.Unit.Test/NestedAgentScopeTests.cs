using Andes.Extensions.AI.Unit.Test.Infrastructure;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Andes.Extensions.AI.Agent.Unit.Test;

public class NestedAgentScopeTests
{
    [Fact]
    public async Task WithTracking_AgentToolInsideAgent_EmitsChildScopeInOuterStream()
    {
        AIFunction packing = CreatePackingAgent(ScriptedTurn.Text("packed", Usage(100))).WithTracking();
        AIAgent research = CreateResearchAgent(packing);
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Research_Agent", new Dictionary<string, object?> { ["query"] = "pack?" }),
            ScriptedTurn.Text("Done.", Usage(30)));
        IChatClient client = TestPipeline.Build(scripted, options => options.UseAgentToolClassification());

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(
            client,
            new ChatOptions { Tools = [research.WithTracking()] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);

        List<ChatProgressUpdate> invoking = progress.Where(update => update.Kind == ChatProgressKind.ToolInvoking).ToList();
        Assert.Equal(2, invoking.Count);
        ChatProgressUpdate outer = invoking[0];
        ChatProgressUpdate child = invoking[1];
        Assert.Equal("Research_Agent", outer.ToolName);
        Assert.Equal("Packing_Agent", child.ToolName);
        Assert.Equal(ToolKind.Agent, child.ToolKind);
        Assert.Equal("Packing Agent", child.ToolSource);
        Assert.Equal(outer.ScopeId, child.ParentScopeId);
        Assert.Equal(outer.Depth + 1, child.Depth);
        Assert.Equal("inner-call", child.CallId);

        int childCompleted = progress.FindIndex(update =>
            update.Kind == ChatProgressKind.ToolCompleted && update.ScopeId == child.ScopeId);
        int outerCompleted = progress.FindIndex(update =>
            update.Kind == ChatProgressKind.ToolCompleted && update.ScopeId == outer.ScopeId);
        Assert.True(childCompleted >= 0 && outerCompleted >= 0 && childCompleted < outerCompleted);
    }

    [Fact]
    public async Task WithTracking_AgentToolInsideAgent_UsageAttributedToChildScope()
    {
        AIFunction packing = CreatePackingAgent(ScriptedTurn.Text("packed", Usage(100))).WithTracking();
        AIAgent research = CreateResearchAgent(packing);
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Research_Agent", new Dictionary<string, object?> { ["query"] = "pack?" }),
            ScriptedTurn.Text("Done.", Usage(30)));
        IChatClient client = TestPipeline.Build(scripted, options => options.UseAgentToolClassification());

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(
            client,
            new ChatOptions { Tools = [research.WithTracking()] });
        ChatUsageReport report = TestPipeline.ReportOf(updates);

        ToolCallUsage call = Assert.Single(report.ToolCalls);
        ToolCallUsage child = Assert.Single(call.Children);
        Assert.Equal("Packing_Agent", child.ToolName);
        Assert.Equal(ToolKind.Agent, child.Kind);
        Assert.NotNull(child.Usage);
        Assert.Equal(100, child.Usage.TotalTokenCount);
        Assert.NotNull(call.Usage);
        Assert.Equal(150, call.Usage.TotalTokenCount);
        Assert.Equal(180, report.TotalUsage.TotalTokenCount);
    }

    [Fact]
    public async Task WithTracking_InvokedDirectlyInsideFunctionTool_OpensChildScopeWithNullCallId()
    {
        AIFunction packing = CreatePackingAgent(ScriptedTurn.Text("packed", Usage(100))).WithTracking();
        AIFunction planTrip = AIFunctionFactory.Create(
            async (CancellationToken cancellationToken) =>
                (string?)await packing.InvokeAsync(new AIFunctionArguments { ["query"] = "pack?" }, cancellationToken),
            "PlanTrip");
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "PlanTrip"),
            ScriptedTurn.Text("Done.", Usage(30)));
        IChatClient client = TestPipeline.Build(scripted, options => options.UseAgentToolClassification());

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [planTrip] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);
        ChatUsageReport report = TestPipeline.ReportOf(updates);

        List<ChatProgressUpdate> invoking = progress.Where(update => update.Kind == ChatProgressKind.ToolInvoking).ToList();
        Assert.Equal(2, invoking.Count);
        Assert.Equal("PlanTrip", invoking[0].ToolName);
        Assert.Equal(ToolKind.Function, invoking[0].ToolKind);
        Assert.Equal("Packing_Agent", invoking[1].ToolName);
        Assert.Equal(ToolKind.Agent, invoking[1].ToolKind);
        Assert.Equal(invoking[0].ScopeId, invoking[1].ParentScopeId);
        Assert.Null(invoking[1].CallId);

        ToolCallUsage call = Assert.Single(report.ToolCalls);
        ToolCallUsage child = Assert.Single(call.Children);
        Assert.NotNull(child.Usage);
        Assert.Equal(100, child.Usage.TotalTokenCount);
        Assert.NotNull(call.Usage);
        Assert.Equal(100, call.Usage.TotalTokenCount);
        Assert.Equal(130, report.TotalUsage.TotalTokenCount);
    }

    [Fact]
    public async Task WithTracking_WrappedByOuterTracker_OpensExactlyOneScope()
    {
        AIFunction packing = CreatePackingAgent(ScriptedTurn.Text("packed", Usage(100))).WithTracking();
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "Packing_Agent", new Dictionary<string, object?> { ["query"] = "pack?" }),
            ScriptedTurn.Text("Done.", Usage(30)));
        IChatClient client = TestPipeline.Build(scripted, options => options.UseAgentToolClassification());

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [packing] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);
        ChatUsageReport report = TestPipeline.ReportOf(updates);

        Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolInvoking);
        ToolCallUsage call = Assert.Single(report.ToolCalls);
        Assert.Empty(call.Children);
        Assert.NotNull(call.Usage);
        Assert.Equal(100, call.Usage.TotalTokenCount);
        Assert.Equal(130, report.TotalUsage.TotalTokenCount);
    }

    [Fact]
    public async Task WithTracking_NestedAgentRunFails_ChildScopeMarkedFailed()
    {
        AIFunction packing = CreatePackingAgent().WithTracking();
        AIFunction planTrip = AIFunctionFactory.Create(
            async (CancellationToken cancellationToken) =>
            {
                try
                {
                    await packing.InvokeAsync(new AIFunctionArguments { ["query"] = "pack?" }, cancellationToken);
                }
                catch (InvalidOperationException)
                {
                    // The empty script throws; the tool itself still succeeds.
                }

                return "planned";
            },
            "PlanTrip");
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "PlanTrip"),
            ScriptedTurn.Text("Done."));
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [planTrip] });
        List<ChatProgressUpdate> progress = TestPipeline.ProgressOf(updates);
        ChatUsageReport report = TestPipeline.ReportOf(updates);

        Assert.Single(progress, update => update.Kind == ChatProgressKind.ToolFailed && update.ToolName == "Packing_Agent");
        ToolCallUsage call = Assert.Single(report.ToolCalls);
        Assert.True(call.Succeeded);
        ToolCallUsage child = Assert.Single(call.Children);
        Assert.False(child.Succeeded);
    }

    [Fact]
    public async Task WithTracking_NestedAgentWithTrackUsageFalse_ChildScopeHasNoOwnUsage()
    {
        AIFunction packing = CreatePackingAgent(ScriptedTurn.Text("packed", Usage(100))).WithTracking(trackUsage: false);
        AIFunction planTrip = AIFunctionFactory.Create(
            async (CancellationToken cancellationToken) =>
                (string?)await packing.InvokeAsync(new AIFunctionArguments { ["query"] = "pack?" }, cancellationToken),
            "PlanTrip");
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("call-1", "PlanTrip"),
            ScriptedTurn.Text("Done.", Usage(30)));
        IChatClient client = TestPipeline.Build(scripted);

        List<ChatResponseUpdate> updates = await TestPipeline.CollectAsync(client, new ChatOptions { Tools = [planTrip] });
        ChatUsageReport report = TestPipeline.ReportOf(updates);

        ToolCallUsage call = Assert.Single(report.ToolCalls);
        ToolCallUsage child = Assert.Single(call.Children);
        Assert.Null(child.Usage);
        Assert.Equal(30, report.TotalUsage.TotalTokenCount);
    }

    [Fact]
    public async Task WithTracking_InvokedOutsideTrackedRequest_RunsWithoutScope()
    {
        AIFunction packing = CreatePackingAgent(ScriptedTurn.Text("packed")).WithTracking();

        object? result = await packing.InvokeAsync(new AIFunctionArguments { ["query"] = "pack?" });

        Assert.NotNull(result);
    }

    private static AIAgent CreatePackingAgent(params ScriptedTurn[] turns)
    {
        return new ScriptedChatClient(turns).AsAIAgent(
            instructions: "You suggest what to pack.",
            name: "Packing Agent");
    }

    private static AIAgent CreateResearchAgent(AIFunction packingTool)
    {
        var scripted = new ScriptedChatClient(
            ScriptedTurn.FunctionCall("inner-call", "Packing_Agent", new Dictionary<string, object?> { ["query"] = "pack?" }),
            ScriptedTurn.Text("researched", Usage(50)));
        return scripted.AsAIAgent(
            instructions: "You research travel topics.",
            name: "Research Agent",
            description: "Researches travel topics.",
            tools: [packingTool]);
    }

    private static UsageDetails Usage(int total)
    {
        return new UsageDetails { TotalTokenCount = total };
    }
}
