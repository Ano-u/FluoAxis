namespace FluoAxis.Contracts;

using System.Text.Json.Serialization;

/// <summary>
/// Fields every run-mutating command must carry (plan.md §5.2). The backend
/// compares <see cref="ExpectedRunRevision"/> against the stored revision
/// and returns a stale-revision error on mismatch instead of silently
/// overwriting. A command with a <see cref="ContractVersion"/> other than
/// current stays constructible: versioning policy is a backend decision
/// (reject or tolerate), not a DTO construction failure.
/// </summary>
public abstract record CommandBase
{
    public Guid RequestId { get; }
    public Guid RunId { get; }
    public int ExpectedRunRevision { get; }
    public string ActorId { get; }
    public DateTimeOffset IssuedUtc { get; }
    public int ContractVersion { get; }

    protected CommandBase(
        Guid requestId,
        Guid runId,
        int expectedRunRevision,
        string actorId,
        DateTimeOffset issuedUtc,
        int contractVersion)
    {
        ContractGuard.RequireNonEmptyGuid(requestId, nameof(requestId));
        ContractGuard.RequireNonEmptyGuid(runId, nameof(runId));
        ContractGuard.RequireNonNegative(expectedRunRevision, nameof(expectedRunRevision));
        ContractGuard.RequireNonBlank(actorId, nameof(actorId));
        ContractGuard.RequireUtc(issuedUtc, nameof(issuedUtc));

        RequestId = requestId;
        RunId = runId;
        ExpectedRunRevision = expectedRunRevision;
        ActorId = actorId;
        IssuedUtc = issuedUtc;
        ContractVersion = contractVersion;
    }

    /// <summary>Derived flag, never part of the wire payload.</summary>
    [JsonIgnore]
    public bool IsCurrentVersion => ContractVersion == Contracts.ContractVersion.Current;
}

/// <summary>
/// Read-side run query (plan.md §5.1): only identification and contract
/// version; a query cannot create stale-revision conflicts and therefore
/// carries no revision or actor fields.
/// </summary>
public sealed record GetRunQuery
{
    public Guid RequestId { get; }
    public Guid RunId { get; }
    public int ContractVersion { get; }

    public GetRunQuery(Guid requestId, Guid runId, int contractVersion = Contracts.ContractVersion.Current)
    {
        ContractGuard.RequireNonEmptyGuid(requestId, nameof(requestId));
        ContractGuard.RequireNonEmptyGuid(runId, nameof(runId));

        RequestId = requestId;
        RunId = runId;
        ContractVersion = contractVersion;
    }

    /// <summary>Derived flag, never part of the wire payload.</summary>
    [JsonIgnore]
    public bool IsCurrentVersion => ContractVersion == Contracts.ContractVersion.Current;
}
