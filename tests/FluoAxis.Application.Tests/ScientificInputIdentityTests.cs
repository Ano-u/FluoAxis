namespace FluoAxis.Application.Tests;

using FluoAxis.Application;
using FluoAxis.Contracts;
using FluoAxis.Domain;
using Xunit;

public sealed class ScientificInputIdentityTests
{
    [Fact]
    public void InputIdentityBindsRawBitsMappingOptionsBoundsAndAssignments()
    {
        Guid runId = Guid.NewGuid();
        ProjectRunAggregate run = new() { ProjectId = Guid.NewGuid(), ProjectName = "Identity", CreatedAtUtc = DateTimeOffset.UnixEpoch,
            RunId = runId, RunRevision = 2, RunState = RunState.Mapped, Method = BrafMethodSnapshot.Default, UpdatedUtc = DateTimeOffset.UnixEpoch };
        PreparedSourceIdentity source = new("source.csv", RelativePath.Parse("data/raw/source.csv"), new string('a', 64), 100, SourceFileDto.MediaTypeNames.Csv);
        WellId well = WellId.Parse("A1");
        PreparedDataMapping mapping = Mapping(runId, 3, new ImportOptionsDto());
        SignalSeries fam = SignalSeries.Create(runId, well, FluorescenceChannel.Fam, [new(TimeSpan.Zero, 100, 1, 1), new(TimeSpan.FromMinutes(120), 200, 3, 1)]);
        SignalSeries rox = SignalSeries.Create(runId, well, FluorescenceChannel.Rox, [new(TimeSpan.Zero, 50, 1, 2), new(TimeSpan.FromMinutes(120), 50, 3, 2)]);
        WellAssignment[] roles = [new(well, WellRole.Ntc, null)];
        AnalysisInputIdentityDto original = ScientificInputIdentity.Capture(run, source, mapping, roles, [fam, rox]);
        AnalysisInputIdentityDto reordered = ScientificInputIdentity.Capture(run with { RunRevision = 3 }, source, mapping, roles, [rox, fam]);

        Assert.Equal(original.InputSnapshotSha256, reordered.InputSnapshotSha256);
        Assert.NotEqual(original.InputRevision, reordered.InputRevision);
        Assert.NotEqual(original.MappingSha256, ScientificInputIdentity.Capture(run, source, Mapping(runId, null, new ImportOptionsDto()), roles, [fam, rox]).MappingSha256);
        Assert.NotEqual(original.MappingSha256, ScientificInputIdentity.Capture(run, source, Mapping(runId, 3, new ImportOptionsDto(timeUnit: "seconds")), roles, [fam, rox]).MappingSha256);
        SignalSeries changed = SignalSeries.Create(runId, well, FluorescenceChannel.Fam, [new(TimeSpan.Zero, 100, 1, 1), new(TimeSpan.FromMinutes(120), Math.BitIncrement(200), 3, 1)]);
        Assert.NotEqual(original.InputSnapshotSha256, ScientificInputIdentity.Capture(run, source, mapping, roles, [changed, rox]).InputSnapshotSha256);
        Assert.NotEqual(original.InputSnapshotSha256, ScientificInputIdentity.Capture(run, source, mapping, [new(well, WellRole.WildType, null)], [fam, rox]).InputSnapshotSha256);
    }

    [Fact]
    public void EvaluatedPointDigestBindsStoredNormalizationAndCoordinates()
    {
        var point = new PersistedScientificPoint("calibration", "A1", "fam", 1, 100, 100, 0.01, 2, 1);
        using var first = new ScientificPointDigest(); first.Append(point);
        using var changed = new ScientificPointDigest(); changed.Append(point with { Normalized = 0.02 });
        using var moved = new ScientificPointDigest(); moved.Append(point with { SourceRowIndex = 3 });
        string hash = first.Complete();
        Assert.NotEqual(hash, changed.Complete()); Assert.NotEqual(hash, moved.Complete());
    }

    [Fact]
    public void TraceProvenanceCannotClaimAnotherWellPrimaryPointOrAlgorithm()
    {
        string hash = new('a', 64);
        var identity = new AnalysisInputIdentityDto(hash, hash, hash, hash, "engine", 1);
        var wrongWell = new TraceProvenanceDto(identity,
            [new("A2", ChannelNames.Fam, 3, 100, 2, "FAM", 4, 1), new("A2", ChannelNames.Rox, 3, 100, 1, "ROX", 4, 1)], []);
        Assert.Throws<ArgumentException>(() => new CalculationTraceDto("id", "engine", hash, "A1", ChannelNames.Fam, 3,
            [], ">=", 0.4, 0.5, StatusNames.Pass, ProvenanceCompletenessNames.StoredComplete, wrongWell));
        var wrongPoint = new TraceProvenanceDto(identity,
            [new("A1", ChannelNames.Fam, 4, 100, 2, "FAM", 5, 1), new("A1", ChannelNames.Rox, 4, 100, 1, "ROX", 5, 1)], []);
        Assert.Throws<ArgumentException>(() => new CalculationTraceDto("id", "engine", hash, "A1", ChannelNames.Fam, 3,
            [], ">=", 0.4, 0.5, StatusNames.Pass, ProvenanceCompletenessNames.StoredComplete, wrongPoint));
        Assert.Throws<ArgumentException>(() => new CalculationTraceDto("id", "different-engine", hash, "A1", ChannelNames.Fam, 4,
            [], ">=", 0.4, 0.5, StatusNames.Pass, ProvenanceCompletenessNames.StoredComplete, wrongPoint));
    }

    private static PreparedDataMapping Mapping(Guid runId, int? end, ImportOptionsDto options) => new(runId,
        new(FluorescenceChannel.Fam, "text-body", 0, 0, new Dictionary<int, WellId> { [1] = WellId.Parse("A1") }, end),
        new(FluorescenceChannel.Rox, "text-body", 0, 0, new Dictionary<int, WellId> { [2] = WellId.Parse("A1") }, end), options);
}
