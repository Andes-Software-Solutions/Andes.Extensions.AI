namespace Andes.Extensions.AI.UI.Unit.Test;

public class AssistantStatusReducerTests
{
    [Fact]
    public void Apply_NestedActivities_BuildsHierarchyWithSubStatuses()
    {
        var reducer = new AssistantStatusReducer();

        reducer.Apply(new AssistantUiEvent { Kind = AssistantUiEventKind.Status, Message = "Reasoning…" });
        reducer.Apply(new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.ActivityStarted,
            ScopeId = "scope-1",
            DisplayName = "Research Agent",
            ToolKind = ToolKind.Agent,
            Source = "Research Agent",
        });
        reducer.Apply(new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.ActivityStarted,
            ScopeId = "scope-2",
            ParentScopeId = "scope-1",
            DisplayName = "SearchDocs",
            ToolKind = ToolKind.Function,
        });
        reducer.Apply(new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.ActivityProgress,
            ScopeId = "scope-2",
            Message = "Summarizing…",
        });
        reducer.Apply(new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.ActivityCompleted,
            ScopeId = "scope-2",
            DurationSeconds = 1.5,
        });
        AssistantStatusSnapshot snapshot = reducer.Apply(new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.ActivityCompleted,
            ScopeId = "scope-1",
            DurationSeconds = 2.1,
        });

        Assert.Equal("Reasoning…", snapshot.AssistantStatus);
        AssistantActivity agent = Assert.Single(snapshot.Activities);
        Assert.Equal("Research Agent", agent.DisplayName);
        Assert.Equal(ToolKind.Agent, agent.Kind);
        Assert.Equal(ActivityState.Completed, agent.State);
        Assert.Equal(2.1, agent.DurationSeconds);

        AssistantActivity child = Assert.Single(agent.Children);
        Assert.Equal("SearchDocs", child.DisplayName);
        Assert.Equal(ActivityState.Completed, child.State);
        Assert.Equal(1.5, child.DurationSeconds);

        SubStatus sub = Assert.Single(child.SubStatuses);
        Assert.Equal("Summarizing…", sub.Message);
    }

    [Fact]
    public void Apply_ActivityFailed_MarksCardFailed()
    {
        var reducer = new AssistantStatusReducer();

        reducer.Apply(new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.ActivityStarted,
            ScopeId = "scope-1",
            DisplayName = "GetForecast",
            ToolKind = ToolKind.Function,
        });
        AssistantStatusSnapshot snapshot = reducer.Apply(new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.ActivityFailed,
            ScopeId = "scope-1",
            DurationSeconds = 0.2,
        });

        AssistantActivity activity = Assert.Single(snapshot.Activities);
        Assert.Equal(ActivityState.Failed, activity.State);
    }

    [Fact]
    public void Apply_FinishedEvent_SetsPhaseAndUsage()
    {
        var reducer = new AssistantStatusReducer();

        AssistantStatusSnapshot snapshot = reducer.Apply(new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.Finished,
            Usage = new UsageSummary { InputTokens = 10, OutputTokens = 20, TotalTokens = 30 },
        });

        Assert.Equal(ActivityState.Completed, snapshot.Phase);
        Assert.Equal(30, snapshot.Usage?.TotalTokens);
    }

    [Fact]
    public void Apply_TextDelta_AccumulatesText()
    {
        var reducer = new AssistantStatusReducer();

        reducer.Apply(new AssistantUiEvent { Kind = AssistantUiEventKind.TextDelta, Text = "Hello " });
        AssistantStatusSnapshot snapshot = reducer.Apply(new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.TextDelta,
            Text = "world",
        });

        Assert.Equal("Hello world", snapshot.Text);
    }

    [Fact]
    public void Apply_ReasoningDelta_AccumulatesReasoningText()
    {
        var reducer = new AssistantStatusReducer();

        reducer.Apply(new AssistantUiEvent { Kind = AssistantUiEventKind.ReasoningDelta, Text = "First part. " });
        AssistantStatusSnapshot snapshot = reducer.Apply(new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.ReasoningDelta,
            Text = "Second part.",
        });

        Assert.Equal("First part. Second part.", snapshot.ReasoningText);
        Assert.Null(snapshot.Text);
    }

    [Fact]
    public void Apply_ReasoningDeltaAcrossActivities_KeepsAccumulating()
    {
        var reducer = new AssistantStatusReducer();

        reducer.Apply(new AssistantUiEvent { Kind = AssistantUiEventKind.ReasoningDelta, Text = "planning the call" });
        reducer.Apply(new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.ActivityStarted,
            ScopeId = "scope-1",
            DisplayName = "GetWeather",
            ToolKind = ToolKind.Function,
        });
        reducer.Apply(new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.ActivityCompleted,
            ScopeId = "scope-1",
            DurationSeconds = 0.4,
        });
        AssistantStatusSnapshot snapshot = reducer.Apply(new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.ReasoningDelta,
            Text = "interpreting the result",
        });

        // Deltas concatenate verbatim across the whole request — no synthetic separators —
        // matching the TypeScript foldAssistantEvents counterpart exactly.
        Assert.Equal("planning the callinterpreting the result", snapshot.ReasoningText);
    }
}
