namespace FluoAxis.Reporting.Tests;

using FluoAxis.Contracts;
using Xunit;

public sealed class ScientificReportEvidenceTests
{
    [Fact]
    public void Stored_stage_values_and_physical_coordinates_survive_report_freeze()
    {
        var renderer = new ResearchReportRenderer();
        var snapshot = renderer.CreateSnapshot(WithStage(), "1.0", ReportFixture.Created);
        string html = renderer.RenderHtml(snapshot);
        var well = Assert.Single(Assert.Single(snapshot.Content.Evidence.ScientificEvidence!.Stages).Wells);
        Assert.Equal(17, well.Endpoint.SourceRowIndex);
        Assert.Equal(3, well.Endpoint.SourceColumnIndex);
        Assert.Equal(123.45678901234567, Assert.Single(well.Values).Value);
        Assert.Contains("123.45678901234567", html, StringComparison.Ordinal);
        Assert.Contains("逐阶段端点来源", html, StringComparison.Ordinal);
        Assert.Contains(new string('c', 64), renderer.GetCanonicalJson(snapshot), StringComparison.Ordinal);
    }

    [Fact]
    public void Report_refuses_stage_identity_from_another_source()
    {
        var renderer = new ResearchReportRenderer();
        Assert.Throws<ArgumentException>(() => renderer.CreateSnapshot(WithStage(new string('e', 64)), "1.0", ReportFixture.Created));
    }

    private static RunEvidenceDto WithStage(string? sourceHash = null)
    {
        RunEvidenceDto baseline = ReportFixture.Evidence();
        var identity = new AnalysisInputIdentityDto(sourceHash ?? baseline.Snapshot.SourceFiles[0].Sha256,
            new string('c', 64), new string('d', 64), baseline.Snapshot.MethodSnapshotHash, "BRAF-ENGINE-1.0", 2);
        var well = new WellScientificEvidenceDto("A1", ChannelNames.Fam, WellRoleNames.Positive100, StatusNames.Pass,
            new SourcePointEvidenceDto("A1", ChannelNames.Fam, 2, 72_000_000_000, 1000, "Signals", 17, 3),
            new SourceWindowEvidenceDto(90, 120, 1, 2, 2), [new("storedValue", 123.45678901234567)], []);
        var evidence = new ScientificEvidenceDto([new AnalysisStageEvidenceDto("calibration", identity, [well], ProvenanceCompletenessNames.StoredComplete)]);
        return new RunEvidenceDto(baseline.ProjectName, baseline.Snapshot, baseline.Mapping, baseline.Assignments,
            baseline.Calibration, baseline.Control, baseline.Samples, baseline.Traces, baseline.ProvenanceCompleteness, evidence);
    }
}
