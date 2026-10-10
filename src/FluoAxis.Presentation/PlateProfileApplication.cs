namespace FluoAxis.Presentation;

using FluoAxis.Contracts;

/// <summary>Applies explicitly selected role geometry, without using fluorescence or copying experiment identities.</summary>
public static class PlateProfileApplication
{
    public static GatewayResult<IReadOnlyList<WellAssignmentDto>> Apply(PlateProfileDto profile, IReadOnlyList<string> mappedWells,
        IReadOnlyList<WellAssignmentDto>? explicitAssignments = null)
    {
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(mappedWells);
        var mapped = mappedWells.ToHashSet(StringComparer.Ordinal);
        if (mapped.Count is < 1 or > 96 || mapped.Count != mappedWells.Count)
            return Fail("PLATE_PROFILE_WELLS_INVALID", "文件孔位集合无效。");
        foreach (PlateProfileWellDto expected in profile.Definition.Wells)
            if (IsControl(expected.Role) && !mapped.Contains(expected.Well))
                return Fail("PLATE_PROFILE_CONTROL_MISSING", $"文件缺少方案要求的对照孔 {expected.Well}，自动分析已停止。");
        var definitions = profile.Definition.Wells.ToDictionary(well => well.Well, StringComparer.Ordinal);
        List<WellAssignmentDto> assignments = new(mapped.Count);
        foreach (string well in mappedWells)
        {
            string? role = definitions.TryGetValue(well, out PlateProfileWellDto? expected) ? expected.Role
                : profile.Definition.RemainingWellsAreSamples ? WellRoleNames.Sample : null;
            if (role is null) return Fail("PLATE_PROFILE_WELL_UNASSIGNED", $"方案未定义孔 {well}，请人工分配角色。");
            // The visible well ID is a fresh technical sample ID, never an old
            // experiment's SampleId/replicate/group/target/notes.
            assignments.Add(new(well, role, role == WellRoleNames.Sample ? well : null,
                replicateIndex: role == WellRoleNames.Sample ? 1 : null));
        }
        if (explicitAssignments is { Count: > 0 })
        {
            if (explicitAssignments.Select(item => item.Well).Distinct(StringComparer.Ordinal).Count() != explicitAssignments.Count
                || explicitAssignments.Any(item => !assignments.Any(expected => expected.Well == item.Well && expected.Role == item.Role)))
                return Fail("PLATE_AUTO_PROFILE_ROLE_CONFLICT", "文件中的明确孔角色与已选方案冲突，请人工复核。");
            var identities = explicitAssignments.ToDictionary(item => item.Well, StringComparer.Ordinal);
            assignments = assignments.Select(item => identities.GetValueOrDefault(item.Well, item)).ToList();
        }
        return GatewayResult.Ok<IReadOnlyList<WellAssignmentDto>>(assignments.AsReadOnly());
    }

    private static bool IsControl(string role) => role is WellRoleNames.Ntc or WellRoleNames.Positive100 or WellRoleNames.WildType or WellRoleNames.Lqc01Percent;
    private static GatewayResult<IReadOnlyList<WellAssignmentDto>> Fail(string code, string message) => GatewayResult.Fail<IReadOnlyList<WellAssignmentDto>>(
        new AppErrorDto(code, SeverityNames.Blocking, message, "核对文件与孔位方案；不会推断、忽略或替换缺失对照。"));
}
