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

    [Fact]
    public void Apply_MetadataOnStatusEvent_CarriesOntoSnapshot()
    {
        var reducer = new AssistantStatusReducer();

        AssistantStatusSnapshot snapshot = reducer.Apply(new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.Status,
            Message = "Working…",
            Metadata = new Dictionary<string, string> { ["traceId"] = "trace-1" },
        });

        Assert.Equal("Working…", snapshot.AssistantStatus);
        Assert.NotNull(snapshot.Metadata);
        Assert.Equal("trace-1", snapshot.Metadata!["traceId"]);
    }

    [Fact]
    public void Apply_MetadataAcrossEvents_MergesWithLastWriteWinningPerKey()
    {
        var reducer = new AssistantStatusReducer();

        reducer.Apply(new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.TextDelta,
            Text = "Hello",
            Metadata = new Dictionary<string, string> { ["traceId"] = "trace-1", ["messageId"] = "m1" },
        });
        AssistantStatusSnapshot snapshot = reducer.Apply(new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.Finished,
            Metadata = new Dictionary<string, string> { ["messageId"] = "m2" },
        });

        Assert.NotNull(snapshot.Metadata);
        Assert.Equal(2, snapshot.Metadata!.Count);
        Assert.Equal("trace-1", snapshot.Metadata["traceId"]);
        Assert.Equal("m2", snapshot.Metadata["messageId"]);
    }

    [Fact]
    public void Apply_NullOrEmptyMetadata_LeavesAccumulatedMetadataUnchanged()
    {
        var reducer = new AssistantStatusReducer();

        reducer.Apply(new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.Status,
            Message = "Working…",
            Metadata = new Dictionary<string, string> { ["traceId"] = "trace-1" },
        });
        AssistantStatusSnapshot afterNull = reducer.Apply(new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.TextDelta,
            Text = "Hello",
        });
        AssistantStatusSnapshot afterEmpty = reducer.Apply(new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.TextDelta,
            Text = " world",
            Metadata = new Dictionary<string, string>(),
        });

        Assert.NotNull(afterEmpty.Metadata);
        Assert.Equal("trace-1", afterEmpty.Metadata!["traceId"]);
        // Null and empty bags are no-ops that reuse the accumulated instance — no copy is made.
        Assert.Same(afterNull.Metadata, afterEmpty.Metadata);
    }

    [Fact]
    public void Apply_EmptyMetadataOnly_LeavesSnapshotMetadataNull()
    {
        var reducer = new AssistantStatusReducer();

        AssistantStatusSnapshot snapshot = reducer.Apply(new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.TextDelta,
            Text = "Hello",
            Metadata = new Dictionary<string, string>(),
        });

        // An empty bag must not materialize an empty dictionary — the TypeScript fold's empty
        // guard keeps the same shape, and JSON omission (WhenWritingNull) depends on it.
        Assert.Null(snapshot.Metadata);
    }

    [Fact]
    public void Apply_UnknownKind_StillMergesMetadata()
    {
        var reducer = new AssistantStatusReducer();

        AssistantStatusSnapshot snapshot = reducer.Apply(new AssistantUiEvent
        {
            Kind = (AssistantUiEventKind)999,
            Metadata = new Dictionary<string, string> { ["messageId"] = "m1" },
        });

        // Matches the TypeScript fold's default arm: an unrecognized kind changes nothing else
        // but still contributes its application-supplied metadata.
        Assert.NotNull(snapshot.Metadata);
        Assert.Equal("m1", snapshot.Metadata!["messageId"]);
        Assert.Null(snapshot.AssistantStatus);
        Assert.Equal(ActivityState.Running, snapshot.Phase);
        Assert.Empty(snapshot.Activities);
        Assert.Null(snapshot.Text);
        Assert.Null(snapshot.Usage);
    }

    [Fact]
    public void Apply_LaterMetadataEvent_DoesNotMutateEarlierSnapshot()
    {
        var reducer = new AssistantStatusReducer();

        AssistantStatusSnapshot first = reducer.Apply(new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.Status,
            Message = "Working…",
            Metadata = new Dictionary<string, string> { ["messageId"] = "m1" },
        });
        AssistantStatusSnapshot second = reducer.Apply(new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.Finished,
            Metadata = new Dictionary<string, string> { ["messageId"] = "m2", ["conversationName"] = "Weather chat" },
        });

        // Neither the overwritten key nor the newly added one bleeds into the earlier snapshot.
        Assert.Equal("m1", first.Metadata!["messageId"]);
        Assert.Single(first.Metadata);
        Assert.Equal("m2", second.Metadata!["messageId"]);
        Assert.Equal("Weather chat", second.Metadata["conversationName"]);
    }

    [Fact]
    public void Apply_CallerMutatedEventDictionary_SnapshotKeepsOriginalValues()
    {
        var reducer = new AssistantStatusReducer();
        var eventMetadata = new Dictionary<string, string> { ["messageId"] = "m1" };

        AssistantStatusSnapshot snapshot = reducer.Apply(new AssistantUiEvent
        {
            Kind = AssistantUiEventKind.Finished,
            Metadata = eventMetadata,
        });
        eventMetadata["messageId"] = "mutated";

        Assert.Equal("m1", snapshot.Metadata!["messageId"]);
    }
}
