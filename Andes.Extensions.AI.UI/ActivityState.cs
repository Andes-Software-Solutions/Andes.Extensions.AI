namespace Andes.Extensions.AI;

/// <summary>
/// Represents the lifecycle state of the assistant request or one of its activities, for driving
/// spinners, checkmarks, and error styling in a UI.
/// </summary>
public enum ActivityState
{
    /// <summary>
    /// The work is still in progress.
    /// </summary>
    Running,

    /// <summary>
    /// The work finished successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// The work failed.
    /// </summary>
    Failed,
}
