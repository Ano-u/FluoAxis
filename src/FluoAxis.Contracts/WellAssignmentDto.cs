namespace FluoAxis.Contracts;

using FluoAxis.Domain;

/// <summary>
/// Immutable assignment of one plate well as sent from the mapping UI
/// (plan.md §5.1 AssignWellsCommand). Mirrors <see cref="Domain.WellAssignment"/>
/// on the wire; role strings are the WellRole enum serialized in camelCase.
/// </summary>
public sealed record WellAssignmentDto
{
    public string Well { get; }
    public string Role { get; }
    public string? SampleId { get; }
    public string? Group { get; }
    public int? ReplicateIndex { get; }
    public string? Target { get; }
    public string? Notes { get; }

    public WellAssignmentDto(
        string well,
        string role,
        string? sampleId = null,
        string? group = null,
        int? replicateIndex = null,
        string? target = null,
        string? notes = null)
    {
        ContractGuard.RequireNonBlank(well, nameof(well));
        ContractGuard.RequireNonBlank(role, nameof(role));

        // Parses and normalizes ("a1" -> "A1"); throws FormatException
        // outside A1..H12.
        well = WellId.Parse(well).ToString();

        if (!WellRoleNames.ToDomainRole.ContainsKey(role))
        {
            throw new ArgumentException($"'{role}' is not a member of the WellRole contract vocabulary.", nameof(role));
        }

        if (sampleId is not null && string.IsNullOrWhiteSpace(sampleId))
        {
            throw new ArgumentException("SampleId, when present, must not be blank.", nameof(sampleId));
        }

        if (replicateIndex is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(replicateIndex), replicateIndex, "ReplicateIndex is 1-based and must be at least 1.");
        }

        Well = well;
        Role = role;
        SampleId = sampleId;
        Group = group;
        ReplicateIndex = replicateIndex;
        Target = target;
        Notes = notes;
    }

    /// <summary>Converts to the domain value object, reusing its own invariant checks.</summary>
    public Domain.WellAssignment ToDomain() => new(
        WellId.Parse(Well),
        ParseRole(Role),
        SampleId is null
            ? null
            : new SampleAssignment(SampleId, Group, ReplicateIndex ?? 1, Target, Notes));

    private static WellRole ParseRole(string role) => role switch
    {
        WellRoleNames.Ntc => WellRole.Ntc,
        WellRoleNames.Positive100 => WellRole.Positive100,
        WellRoleNames.WildType => WellRole.WildType,
        WellRoleNames.Lqc01Percent => WellRole.Lqc01Percent,
        WellRoleNames.Sample => WellRole.Sample,
        WellRoleNames.Ignore => WellRole.Ignore,
        _ => throw new ArgumentException($"'{role}' is not a member of the WellRole contract vocabulary.", nameof(role)),
    };
}

/// <summary>camelCase wire names of <see cref="Domain.WellRole"/>.</summary>
public static class WellRoleNames
{
    public const string Ntc = "ntc";
    public const string Positive100 = "positive100";
    public const string WildType = "wildType";
    public const string Lqc01Percent = "lqc01Percent";
    public const string Sample = "sample";
    public const string Ignore = "ignore";

    public static IReadOnlyDictionary<string, WellRole> ToDomainRole { get; } = new Dictionary<string, WellRole>
    {
        [Ntc] = WellRole.Ntc,
        [Positive100] = WellRole.Positive100,
        [WildType] = WellRole.WildType,
        [Lqc01Percent] = WellRole.Lqc01Percent,
        [Sample] = WellRole.Sample,
        [Ignore] = WellRole.Ignore,
    };
}
