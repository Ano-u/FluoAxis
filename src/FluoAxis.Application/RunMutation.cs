namespace FluoAxis.Application;

using FluoAxis.Contracts;
using FluoAxis.Domain;

/// <summary>
/// An atomic, revision-guarded run mutation: one command identity, the
/// revision the caller observed, the state the caller believes the run is
/// in, and the state it must become. The repository applies the whole thing
/// in a single transaction and refuses it if any part no longer holds.
/// </summary>
public sealed record RunMutationRequest
{
    public Guid RequestId { get; }
    public Guid RunId { get; }
    public int ExpectedRunRevision { get; }
    public RunState ExpectedState { get; }
    public RunState TargetState { get; }
    public string ActorId { get; }
    public DateTimeOffset IssuedUtc { get; }
    public int ContractVersion { get; }
    public string EventType { get; }
    public IReadOnlyList<RunState> TransitionPath { get; }

    public bool Matches(RunMutationRequest? other) =>
        other is not null
        && RequestId == other.RequestId
        && RunId == other.RunId
        && ExpectedRunRevision == other.ExpectedRunRevision
        && ExpectedState == other.ExpectedState
        && TargetState == other.TargetState
        && string.Equals(ActorId, other.ActorId, StringComparison.Ordinal)
        && IssuedUtc == other.IssuedUtc
        && ContractVersion == other.ContractVersion
        && string.Equals(EventType, other.EventType, StringComparison.Ordinal);

    public RunMutationRequest(
        Guid requestId,
        Guid runId,
        int expectedRunRevision,
        RunState expectedState,
        RunState targetState,
        string actorId,
        DateTimeOffset issuedUtc,
        int contractVersion,
        string eventType,
        IReadOnlyList<RunState>? transitionPath = null)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("RequestId must not be empty.", nameof(requestId));
        }

        if (runId == Guid.Empty)
        {
            throw new ArgumentException("RunId must not be empty.", nameof(runId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(expectedRunRevision);
        if (!Enum.IsDefined(expectedState))
        {
            throw new ArgumentOutOfRangeException(nameof(expectedState), expectedState, "ExpectedState must be a defined RunState.");
        }

        if (!Enum.IsDefined(targetState))
        {
            throw new ArgumentOutOfRangeException(nameof(targetState), targetState, "TargetState must be a defined RunState.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        RequestId = requestId;
        RunId = runId;
        ExpectedRunRevision = expectedRunRevision;
        ExpectedState = expectedState;
        TargetState = targetState;
        ActorId = actorId;
        IssuedUtc = issuedUtc;
        ContractVersion = contractVersion;
        EventType = eventType;
        TransitionPath = transitionPath is null
            ? RunStateMachine.FindPath(expectedState, targetState) ?? [expectedState, targetState]
            : [.. transitionPath];
    }
}

/// <summary>Why a run mutation was refused, when it was.</summary>
public enum RunMutationFailure
{
    None,

    /// <summary>No run with that id exists.</summary>
    NotFound,

    /// <summary>The run revision moved past <see cref="RunMutationRequest.ExpectedRunRevision"/>.</summary>
    StaleRevision,

    /// <summary>The run is not in <see cref="RunMutationRequest.ExpectedState"/>.</summary>
    WrongState,

    /// <summary><see cref="RunStateMachine"/> rejects ExpectedState -> TargetState.</summary>
    IllegalTransition,

    /// <summary>The same request id was already used with different mutation data.</summary>
    RequestConflict,
}

/// <summary>
/// Outcome of an atomic run mutation. <see cref="Aggregate"/> is the run as
/// it stands after the call: the new state on success, the unchanged state
/// on refusal, and the recorded state on an exact replay.
/// </summary>
public sealed record RunMutationOutcome(
    ProjectRunAggregate? Aggregate,
    RunMutationFailure Failure,
    bool IsReplay)
{
    public bool IsSuccess => Failure == RunMutationFailure.None;

    public static RunMutationOutcome Refused(RunMutationFailure failure) => new(null, failure, IsReplay: false);
}
