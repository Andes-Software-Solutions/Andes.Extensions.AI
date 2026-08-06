/**
 * Andes.Extensions.AI.UI — TypeScript contract.
 *
 * A 1:1 mirror of the C# `Andes.Extensions.AI.UI` records, matching the JSON produced by
 * `AssistantUiJsonContext` exactly: camelCase property names, string enum values, and omitted
 * `null`s (so nullable members are optional here). Import these types in any SPA that consumes the
 * assistant-status stream, and use `foldAssistantEvents` to reconstruct the same activity tree the
 * C# `AssistantStatusReducer` builds.
 *
 * Token counts are `number` (JavaScript float64); typical usage values are well within safe range.
 */

/** The category of an activity, for a badge or icon. Mirrors the core `ToolKind` enum. */
export type ToolKind = "Unknown" | "Function" | "McpTool" | "Agent";

/** What a single {@link AssistantUiEvent} represents. */
export type AssistantUiEventKind =
  | "Status"
  | "ActivityStarted"
  | "ActivityProgress"
  | "ActivityCompleted"
  | "ActivityFailed"
  | "TextDelta"
  | "Finished";

/** The lifecycle state of the request or one of its activities. */
export type ActivityState = "Running" | "Completed" | "Failed";

/** A flattened view of token usage. Each count is optional because a provider may report only some. */
export interface UsageSummary {
  /** The number of input (prompt) tokens, when reported. */
  inputTokens?: number;
  /** The number of output (completion) tokens, when reported. */
  outputTokens?: number;
  /** The total number of tokens, when reported. */
  totalTokens?: number;
}

/** A single sub-status line reported while an activity runs, optionally with numeric progress. */
export interface SubStatus {
  /** The human-readable status message. */
  message: string;
  /** The numeric progress (numerator), when supplied. */
  progress?: number;
  /** The total amount of work (denominator for {@link progress}), when known. */
  progressTotal?: number;
}

/**
 * One activity the assistant performed — a function, MCP tool, or agent — rendered as a card.
 * The label is {@link displayName} (clean name only) plus {@link kind} as a separate badge; there
 * is never a pre-composed "Calling … MCP/Agent/Tool" string, so the kind word is not repeated.
 */
export interface AssistantActivity {
  /** The tracked scope identifier, stable across all of this activity's events. */
  scopeId: string;
  /** The clean display name (no "Calling" prefix, no kind word), e.g. "Andes Test MCP". */
  displayName: string;
  /** The activity category, for a badge or icon. */
  kind: ToolKind;
  /** The origin — an MCP server name or agent identifier — when applicable. */
  source?: string;
  /** Whether the activity is still running, completed, or failed. */
  state: ActivityState;
  /** The elapsed time in seconds, set when the activity completes or fails. */
  durationSeconds?: number;
  /** The sub-status lines reported while the activity ran, oldest first. */
  subStatuses: SubStatus[];
  /** The activities invoked inside this one — for example an agent's own tools. */
  children: AssistantActivity[];
  /** The token usage attributed to this activity (including its children), when known. */
  usage?: UsageSummary;
}

/**
 * An immutable snapshot of everything the assistant is doing at one point in a streamed request.
 * Bind to this and re-render whenever a new snapshot arrives.
 */
export interface AssistantStatusSnapshot {
  /** The current request-level status line, such as "Reasoning…". */
  assistantStatus?: string;
  /** The overall state of the request. */
  phase: ActivityState;
  /** The top-level activity cards, one per activity the assistant started, in order. */
  activities: AssistantActivity[];
  /** The assistant's answer text accumulated so far. */
  text?: string;
  /** The total token usage for the request, set once it finishes. */
  usage?: UsageSummary;
}

/**
 * A flat, per-flush delta an API sends the UI on each stream event. The {@link kind} discriminator
 * tells the UI which fields are meaningful. Fold a sequence with {@link foldAssistantEvents}.
 */
export interface AssistantUiEvent {
  /** What this event represents. */
  kind: AssistantUiEventKind;
  /** The status line ("Status") or the sub-status message ("ActivityProgress"). */
  message?: string;
  /** The identifier of the activity scope this event targets, when applicable. */
  scopeId?: string;
  /** The parent scope identifier, or absent when the activity is top-level. */
  parentScopeId?: string;
  /** The display depth: 0 request-level, 1 top-level activity, 2 sub-status, deeper for nesting. */
  depth: number;
  /** The category of the targeted activity, when applicable. */
  toolKind: ToolKind;
  /** The clean display name of the activity (no "Calling" prefix, no kind word). */
  displayName?: string;
  /** The origin — MCP server name or agent identifier — when applicable. */
  source?: string;
  /** The numeric progress (numerator) for an "ActivityProgress" event, when supplied. */
  progress?: number;
  /** The total amount of work (denominator for {@link progress}), when known. */
  progressTotal?: number;
  /** The elapsed seconds for a completion event, or the request duration for "Finished". */
  durationSeconds?: number;
  /** The answer text chunk for a "TextDelta" event. */
  text?: string;
  /** The token usage for a "Finished" event. */
  usage?: UsageSummary;
  /** The ISO 8601 time at which the underlying progress event was raised. */
  timestamp: string;
}

/** Returns the empty starting snapshot to fold events into. */
export function createInitialSnapshot(): AssistantStatusSnapshot {
  return { phase: "Running", activities: [] };
}

/**
 * Folds one {@link AssistantUiEvent} into a snapshot and returns a new snapshot, reconstructing the
 * activity hierarchy from `scopeId`/`parentScopeId`. The C# counterpart is `AssistantStatusReducer`.
 *
 * @example
 * let snapshot = createInitialSnapshot();
 * for (const event of events) {
 *   snapshot = foldAssistantEvents(snapshot, event);
 *   render(snapshot);
 * }
 */
export function foldAssistantEvents(
  snapshot: AssistantStatusSnapshot,
  event: AssistantUiEvent,
): AssistantStatusSnapshot {
  switch (event.kind) {
    case "Status":
      return { ...snapshot, assistantStatus: event.message };

    case "ActivityStarted": {
      const activity: AssistantActivity = {
        scopeId: event.scopeId ?? "",
        displayName: event.displayName ?? event.source ?? event.scopeId ?? "",
        kind: event.toolKind,
        source: event.source,
        state: "Running",
        subStatuses: [],
        children: [],
      };
      return { ...snapshot, activities: addActivity(snapshot.activities, event.parentScopeId, activity) };
    }

    case "ActivityProgress":
      return {
        ...snapshot,
        activities: updateActivity(snapshot.activities, event.scopeId, (activity) => ({
          ...activity,
          subStatuses: [
            ...activity.subStatuses,
            { message: event.message ?? "", progress: event.progress, progressTotal: event.progressTotal },
          ],
        })),
      };

    case "ActivityCompleted":
    case "ActivityFailed":
      return {
        ...snapshot,
        activities: updateActivity(snapshot.activities, event.scopeId, (activity) => ({
          ...activity,
          state: event.kind === "ActivityFailed" ? "Failed" : "Completed",
          durationSeconds: event.durationSeconds,
        })),
      };

    case "TextDelta":
      return { ...snapshot, text: (snapshot.text ?? "") + (event.text ?? "") };

    case "Finished":
      return { ...snapshot, phase: "Completed", usage: event.usage };

    default:
      return snapshot;
  }
}

function addActivity(
  activities: AssistantActivity[],
  parentScopeId: string | undefined,
  activity: AssistantActivity,
): AssistantActivity[] {
  // A top-level activity's parent is the request root, which has no card, so it becomes a root.
  if (parentScopeId != null) {
    const inserted = tryInsertUnder(activities, parentScopeId, activity);
    if (inserted != null) {
      return inserted;
    }
  }
  return [...activities, activity];
}

function tryInsertUnder(
  activities: AssistantActivity[],
  parentScopeId: string,
  activity: AssistantActivity,
): AssistantActivity[] | null {
  let found = false;
  const next = activities.map((current) => {
    if (found) {
      return current;
    }
    if (current.scopeId === parentScopeId) {
      found = true;
      return { ...current, children: [...current.children, activity] };
    }
    const nested = tryInsertUnder(current.children, parentScopeId, activity);
    if (nested != null) {
      found = true;
      return { ...current, children: nested };
    }
    return current;
  });
  return found ? next : null;
}

function updateActivity(
  activities: AssistantActivity[],
  scopeId: string | undefined,
  update: (activity: AssistantActivity) => AssistantActivity,
): AssistantActivity[] {
  if (scopeId == null) {
    return activities;
  }
  return activities.map((current) =>
    current.scopeId === scopeId
      ? update(current)
      : { ...current, children: updateActivity(current.children, scopeId, update) },
  );
}
