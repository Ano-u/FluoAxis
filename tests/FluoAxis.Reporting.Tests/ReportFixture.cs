namespace FluoAxis.Reporting.Tests;

using FluoAxis.Contracts;

internal static class ReportFixture
{
    internal static readonly DateTimeOffset Created = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
    internal static readonly Guid RunId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    internal static readonly string MethodHash = new('a', 64);

    internal static RunEvidenceDto Evidence(string project = "研究项目", string sampleId = "S-1", string notes = "科研复核",
        bool invalid = false, bool tracesAvailable = true, string sampleCall = CallNames.Positive, double distance = 0.4)
    {
        IssueDto[] issues = invalid
            ? [new("SAMPLE_REPLICATE_CLASSIFICATION_INCONSISTENCY", "重复孔分类不一致", "重复实验；不要由均值掩盖冲突")]
            : [];
        var snapshot = new RunSnapshotDto(RunId, 5, invalid ? RunStateNames.RunInvalid : RunStateNames.Analyzed,
            invalid ? [RunAction.ViewExplorer] : [RunAction.Export, RunAction.ViewExplorer], issues, [],
            "BRAF-V600E-Research", "1.0", MethodHash,
            [new SourceFileDto("instrument.csv", "data/raw/instrument.csv", new string('b', 64), 123, Created)],
            new MappingSummaryDto(1, 1, [sampleId]),
            new CalibrationSummaryDto(StatusNames.Pass, 1000, 500, 100, 50),
            new ControlSummaryDto(StatusNames.Pass, 0, 0, CallNames.Positive, 0.6),
            new AnalysisSummaryDto(invalid ? StatusNames.Invalid : StatusNames.Pass, 1, invalid ? 0 : 1, 0, invalid ? 1 : 0), Created);
        return new RunEvidenceDto(project, snapshot,
            new RunMappingDto(new(ChannelNames.Fam, "Signals", 0, 0, [new(1, "A1")]), new(ChannelNames.Rox, "Signals", 0, 0, [new(2, "A1")])),
            [new("A1", WellRoleNames.Sample, sampleId, "group", 1, "BRAF", notes)],
            new CalibrationResultDto(StatusNames.Pass, 1000, 500, 100, 50),
            new ControlQcResultDto(StatusNames.Pass, 0, 0, CallNames.Positive, 0.6),
            [new SampleAnalysisDto(sampleId, invalid ? StatusNames.Invalid : StatusNames.Pass, invalid ? null : sampleCall, invalid ? null : distance,
                [new("A1", 0.4, 0, distance, sampleCall, "trace-1")])],
            tracesAvailable ? [new CalculationTraceDto("trace-1", "BRAF-ENGINE-1.0", MethodHash, "A1", ChannelNames.Fam, 60,
                [new("F_FAM_120", 460), new("Y_FAM", 0.4), new("Y_ROX", 0)], ">=", 0.4, distance, StatusNames.Pass)] : [],
            tracesAvailable ? ProvenanceCompletenessNames.StoredPartial : ProvenanceCompletenessNames.Unavailable);
    }
}
