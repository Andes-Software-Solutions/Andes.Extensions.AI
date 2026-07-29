namespace Andes.Extensions.AI.Demo;

/// <summary>
/// Builds the persistent end-of-turn snapshot: activity cards with per-activity token usage come
/// from the final <see cref="ChatUsageReport"/> (live snapshots never carry usage), while the
/// streamed answer text and sub-status lines come from the last live snapshot.
/// </summary>
internal static class FinalSnapshot
{
    public static AssistantStatusSnapshot? Merge(ChatUsageReport? report, AssistantStatusSnapshot? live)
    {
        if (report is null)
        {
            return live;
        }

        AssistantStatusSnapshot final = report.ToSnapshot();
        return final with
        {
            Text = live?.Text ?? final.Text,
            Activities = MergeActivities(final.Activities, live?.Activities ?? []),
        };
    }

    private static IReadOnlyList<AssistantActivity> MergeActivities(
        IReadOnlyList<AssistantActivity> fromReport,
        IReadOnlyList<AssistantActivity> fromLive)
    {
        // Pairing positionally assumes every tool in the request is tracker-wrapped (true in this
        // demo). A live tree can also contain best-effort cards for tools the tracker could not
        // wrap (hosted/declaration-only tools), which the report omits — those would shift the
        // pairing, and no key-based join exists (live ids are scope-N; report ids fall back to
        // CallId/ToolName). Keep report values when a live activity is missing at an index.
        var merged = new List<AssistantActivity>(fromReport.Count);
        for (int i = 0; i < fromReport.Count; i++)
        {
            AssistantActivity reportActivity = fromReport[i];
            AssistantActivity? liveActivity = i < fromLive.Count ? fromLive[i] : null;
            merged.Add(reportActivity with
            {
                SubStatuses = liveActivity?.SubStatuses ?? reportActivity.SubStatuses,
                Children = MergeActivities(reportActivity.Children, liveActivity?.Children ?? []),
            });
        }

        return merged;
    }
}
