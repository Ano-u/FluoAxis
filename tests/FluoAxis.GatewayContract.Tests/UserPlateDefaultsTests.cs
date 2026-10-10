namespace FluoAxis.GatewayContract.Tests;

using FluoAxis.Contracts;
using FluoAxis.Presentation;
using Xunit;

public sealed class UserPlateDefaultsTests
{
    [Fact]
    public void Four_corner_controls_match_the_users_explicit_roles()
    {
        IReadOnlyList<WellAssignmentDto> assignments = UserPlateDefaults.CreateAssignments();
        Assert.Equal(96, assignments.Count);
        Assert.Equal(96, assignments.Select(item => item.Well).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("A1", Assert.Single(assignments, item => item.Role == WellRoleNames.Positive100).Well);
        Assert.Equal("A12", Assert.Single(assignments, item => item.Role == WellRoleNames.Ntc).Well);
        Assert.Equal("H1", Assert.Single(assignments, item => item.Role == WellRoleNames.WildType).Well);
        Assert.Equal("H12", Assert.Single(assignments, item => item.Role == WellRoleNames.Lqc01Percent).Well);
    }

    [Fact]
    public void Only_the_B2_through_G11_rectangle_contains_technical_sample_ids()
    {
        IReadOnlyList<WellAssignmentDto> assignments = UserPlateDefaults.CreateAssignments();
        string[] expected = [.. from row in "BCDEFG" from column in Enumerable.Range(2, 10)
            select string.Concat(row, column.ToString(System.Globalization.CultureInfo.InvariantCulture))];
        WellAssignmentDto[] samples = [.. assignments.Where(item => item.Role == WellRoleNames.Sample)];
        Assert.Equal(expected, samples.Select(item => item.Well));
        Assert.All(samples, item => { Assert.Equal(item.Well, item.SampleId); Assert.Equal(1, item.ReplicateIndex); Assert.Null(item.Group); Assert.Null(item.Target); Assert.Null(item.Notes); });
        Assert.Equal(32, assignments.Count(item => item.Role == WellRoleNames.Ignore));
        foreach (string boundary in new[] { "A2", "A11", "B1", "B12", "G1", "G12", "H2", "H11" })
            Assert.Equal(WellRoleNames.Ignore, Assert.Single(assignments, item => item.Well == boundary).Role);
    }

    [Fact]
    public void Fixed_profile_allows_missing_samples_but_requires_every_corner_control()
    {
        var definition = new PlateProfileDefinitionDto("四角对照 · B2–G11样本区",
            UserPlateDefaults.CreateAssignments().Select(item => new PlateProfileWellDto(item.Well, item.Role)).ToArray(), false);
        var profile = new PlateProfileDto(Guid.NewGuid(), definition, new string('a', 64), DateTimeOffset.UnixEpoch, true);
        string[] subset = ["A1", "A12", "H1", "H12", "B2", "G11", "B1"];
        GatewayResult<IReadOnlyList<WellAssignmentDto>> applied = PlateProfileApplication.Apply(profile, subset);
        Assert.True(applied.IsSuccess);
        Assert.Equal(2, applied.Value!.Count(item => item.Role == WellRoleNames.Sample));
        Assert.Equal(WellRoleNames.Ignore, Assert.Single(applied.Value!, item => item.Well == "B1").Role);
        Assert.Null(profile.Definition.Options);
        foreach (string control in new[] { "A1", "A12", "H1", "H12" })
        {
            GatewayResult<IReadOnlyList<WellAssignmentDto>> missing = PlateProfileApplication.Apply(profile, subset.Where(well => well != control).ToArray());
            Assert.False(missing.IsSuccess);
            Assert.Equal("PLATE_PROFILE_CONTROL_MISSING", Assert.Single(missing.Errors).Code);
        }
    }
}
