namespace FluoAxis.Contracts.Tests;

using System.Text.Json;
using FluoAxis.Contracts;
using Xunit;

public sealed class PlateProfileContractTests
{
    [Fact]
    public void Profile_definition_freezes_roles_and_round_trips_explicit_options()
    {
        PlateProfileWellDto[] wells = [new("a1", WellRoleNames.Ntc), new("A2", WellRoleNames.Positive100),
            new("A3", WellRoleNames.WildType), new("A4", WellRoleNames.Lqc01Percent)];
        var definition = new PlateProfileDefinitionDto("Controls", wells, true, new(timeUnit: "seconds"));
        wells[0] = new("B1", WellRoleNames.Ntc);
        var profile = new PlateProfileDto(Guid.NewGuid(), definition, new string('a', 64), DateTimeOffset.UnixEpoch);
        string json = JsonSerializer.Serialize(profile, PlateProfileJsonContext.Default.PlateProfileDto);
        PlateProfileDto restored = JsonSerializer.Deserialize(json, PlateProfileJsonContext.Default.PlateProfileDto)!;
        Assert.Equal("A1", restored.Definition.Wells[0].Well);
        Assert.Equal("seconds", restored.Definition.Options!.TimeUnit);
        Assert.True(restored.Definition.RemainingWellsAreSamples);
        Assert.DoesNotContain("sampleId", json, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new PlateProfileDto(Guid.NewGuid(), definition, profile.Sha256, DateTimeOffset.UnixEpoch, version: 2));
    }

    [Theory]
    [InlineData("../layout")]
    [InlineData("folder/layout")]
    [InlineData("")]
    public void Profile_name_is_a_label_not_a_storage_path(string name)
    {
        Assert.Throws<ArgumentException>(() => new PlateProfileDefinitionDto(name,
            [new("A1", WellRoleNames.Ntc), new("A2", WellRoleNames.Positive100), new("A3", WellRoleNames.WildType), new("A4", WellRoleNames.Lqc01Percent)], true));
    }
}
