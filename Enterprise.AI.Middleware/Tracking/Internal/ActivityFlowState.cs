namespace Enterprise.AI.Middleware.Tracking.Internal;

/// <summary>
/// Carries the ambient association between the executing asynchronous flow, its activity scope,
/// and the <see cref="ActivityContext"/> that owns the request.
/// </summary>
/// <param name="Context">The per-request coordinator that owns the scope tree.</param>
/// <param name="Scope">The scope the current flow is executing under.</param>
internal sealed record ActivityFlowState(ActivityContext Context, ActivityScope Scope);
