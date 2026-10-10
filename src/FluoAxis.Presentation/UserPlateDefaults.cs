namespace FluoAxis.Presentation;

using FluoAxis.Contracts;

/// <summary>The user's explicitly confirmed reusable layout, separate from scientific Method defaults.</summary>
public static class UserPlateDefaults
{
    public static IReadOnlyList<WellAssignmentDto> CreateAssignments()
    {
        List<WellAssignmentDto> assignments = new(96);
        for (char row = 'A'; row <= 'H'; row++)
        for (int column = 1; column <= 12; column++)
        {
            string well = string.Concat(row, column.ToString(System.Globalization.CultureInfo.InvariantCulture));
            string role = well switch
            {
                "A1" => WellRoleNames.Positive100,
                "A12" => WellRoleNames.Ntc,
                "H1" => WellRoleNames.WildType,
                "H12" => WellRoleNames.Lqc01Percent,
                _ => row is >= 'B' and <= 'G' && column is >= 2 and <= 11 ? WellRoleNames.Sample : WellRoleNames.Ignore,
            };
            assignments.Add(new WellAssignmentDto(well, role, role == WellRoleNames.Sample ? well : null,
                replicateIndex: role == WellRoleNames.Sample ? 1 : null));
        }
        return assignments.AsReadOnly();
    }
}
