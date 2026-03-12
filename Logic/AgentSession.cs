namespace Thio_Universal_Agent.Logic;

/// <summary>Tracks the lifecycle status of an agent session.</summary>
public enum AgentSessionStatus
{
    /// <summary>The agent loop is actively running.</summary>
    Running,

    /// <summary>The goal was achieved (agent issued DONE).</summary>
    Completed,

    /// <summary>The agent declared failure or exceeded the step limit.</summary>
    Failed,

    /// <summary>The session was cancelled by the user.</summary>
    Cancelled,

    /// <summary>An unexpected error terminated the session.</summary>
    Error,
}

/// <summary>
/// Holds all mutable state for a single agent execution session.
/// Created by <see cref="AgentSessionManager"/> and driven by <see cref="AgentLoop"/>.
/// </summary>
public sealed class AgentSession
{
    /// <summary>Unique identifier for this session.</summary>
    public string SessionId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>The user-specified goal the agent is working toward.</summary>
    public required string Goal { get; init; }

    /// <summary>Current lifecycle status.</summary>
    public AgentSessionStatus Status { get; set; } = AgentSessionStatus.Running;

    /// <summary>Ordered history of every completed step.</summary>
    public List<AgentStep> Steps { get; } = [];

    /// <summary>Human-readable result message set when the session terminates.</summary>
    public string? FinalResult { get; set; }

    /// <summary>When the session was created.</summary>
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    /// <summary>Cancellation source that the user or timeout logic can trigger.</summary>
    public CancellationTokenSource Cts { get; } = new();

    /// <summary>
    /// Event raised right after the AI's response is parsed but before the action is executed.
    /// Allows the UI to show the intended action while coordinate resolution or other slow work proceeds.
    /// </summary>
    public event Func<AgentStepPreview, Task>? OnStepStarting;

    /// <summary>Raises <see cref="OnStepStarting"/> for the given preview.</summary>
    internal async Task RaiseStepStartingAsync(AgentStepPreview preview)
    {
        Func<AgentStepPreview, Task>? handler = OnStepStarting;
        if (handler is not null)
            await handler(preview).ConfigureAwait(false);
    }

    /// <summary>
    /// Event raised in real-time during action execution for each intermediate debug entry.
    /// Allows the UI to show coordinate resolution progress without waiting for the full step to complete.
    /// </summary>
    public event Func<AgentSubStep, Task>? OnSubStepUpdate;

    /// <summary>Raises <see cref="OnSubStepUpdate"/> for the given sub-step.</summary>
    internal async Task RaiseSubStepUpdateAsync(AgentSubStep subStep)
    {
        Func<AgentSubStep, Task>? handler = OnSubStepUpdate;
        if (handler is not null)
            await handler(subStep).ConfigureAwait(false);
    }

    /// <summary>
    /// Event raised after each step completes. The <see cref="AgentEndpoints"/> SSE stream subscribes to this.
    /// </summary>
    public event Func<AgentStep, Task>? OnStepCompleted;

    /// <summary>Raises <see cref="OnStepCompleted"/> for the given step.</summary>
    internal async Task RaiseStepCompletedAsync(AgentStep step)
    {
        Func<AgentStep, Task>? handler = OnStepCompleted;
        if (handler is not null)
            await handler(step).ConfigureAwait(false);
    }

    private readonly TaskCompletionSource _terminated = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Completes when the session has fully terminated (status and FinalResult are guaranteed to be set).
    /// Resolved by <see cref="SignalTerminated"/> at the end of <see cref="AgentLoop.RunAsync"/>.
    /// </summary>
    public Task Terminated => _terminated.Task;

    /// <summary>Called by <see cref="AgentLoop"/> once the loop exits to unblock any SSE stream waiters.</summary>
    internal void SignalTerminated() => _terminated.TrySetResult();
}
