namespace FluoAxis.Contracts.Tests;

using System.Text.Json;
using FluoAxis.Contracts;
using FluoAxis.Contracts.Serialization;
using Xunit;

public sealed class FmEvidenceContractTests
{
    [Fact]
    public void OptionalFmSourcePreservesLegacyJsonAndRoundTripsEndpointEvidence()
    {
        var endpoint = new SourcePointEvidenceDto("A1", ChannelNames.Rox, 2, TimeSpan.FromMinutes(120).Ticks, 600, "ROX", 3, 1);
        var legacy = new WellScientificEvidenceDto("A1", ChannelNames.Rox, WellRoleNames.Positive100, StatusNames.Pass, endpoint, null, [new("Fm", 600)], []);
        string oldJson = JsonSerializer.Serialize(legacy, ScientificEvidenceJsonContext.Default.WellScientificEvidenceDto);
        Assert.DoesNotContain("fmSource", oldJson, StringComparison.OrdinalIgnoreCase);
        Assert.Null(JsonSerializer.Deserialize(oldJson, ScientificEvidenceJsonContext.Default.WellScientificEvidenceDto)!.FmSource);
        var current = new WellScientificEvidenceDto("A1", ChannelNames.Rox, WellRoleNames.Positive100, StatusNames.Pass, endpoint, null, [new("Fm", 600)], [], "endpoint");
        string json = JsonSerializer.Serialize(current, ScientificEvidenceJsonContext.Default.WellScientificEvidenceDto);
        Assert.Equal("endpoint", JsonSerializer.Deserialize(json, ScientificEvidenceJsonContext.Default.WellScientificEvidenceDto)!.FmSource);
        Assert.Throws<ArgumentException>(() => new WellScientificEvidenceDto("A1", ChannelNames.Rox, WellRoleNames.Positive100, StatusNames.Pass, endpoint,
            new(90, 120, 1, 2, 2), [], [], "endpoint"));
    }
}
