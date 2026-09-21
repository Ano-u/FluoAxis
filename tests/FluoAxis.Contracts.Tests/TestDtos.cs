namespace FluoAxis.Contracts.Tests;

using System.Text.Json;
using FluoAxis.Contracts;
using FluoAxis.Contracts.Serialization;
using FluoAxis.Domain;

/// <summary>Shared fixtures for contract tests.</summary>
internal static class TestDtos
{
    public static readonly Guid RequestId = Guid.Parse("12345678-1234-1234-1234-123456789abc");
    public static readonly Guid RunId = Guid.Parse("87654321-4321-4321-4321-cba987654321");
    public static readonly Guid ProjectId = Guid.Parse("abcdefab-cdef-cdef-cdef-abcdefabcdef");
    public static readonly DateTimeOffset IssuedUtc = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    public const string ValidSha256 = "a1b2c3d4e5f60718293a4b5c6d7e8f90112233445566778899aabbccddeeff00";

    public static SourceFileDto SourceFile() => new(
        "run-001.xlsx",
        "data/raw/run-001.xlsx",
        ValidSha256,
        2048,
        IssuedUtc);

    public static SourceFileDto SourceFile(string relativeResourcePath) => new(
        "run-001.xlsx",
        relativeResourcePath,
        ValidSha256,
        2048,
        IssuedUtc);

    /// <summary>
    /// Builds a mapping command with the staged-source identity the V3
    /// contract requires. Tests that care only about the mapping pass the
    /// regions they want and inherit a valid default resource identity.
    /// </summary>
    public static ConfirmMappingCommand ConfirmMapping(
        ChannelRegionMappingDto fam,
        ChannelRegionMappingDto rox,
        int expectedRunRevision = 2) => new(
            RequestId,
            RunId,
            expectedRunRevision,
            "researcher-1",
            IssuedUtc,
            fam,
            rox,
            originalFileName: "run-001.xlsx",
            relativeResourcePath: "data/raw/run-001.xlsx",
            sourceSha256: ValidSha256,
            sourceSizeBytes: 2048,
            sourceMediaType: SourceFileDto.MediaTypeNames.Xlsx);

    public static RunSnapshotDto FullRunSnapshot() => new(
        RunId,
        revision: 3,
        domainState: RunStateNames.CalibrationPassed,
        allowedActions: [RunAction.RunControlQc, RunAction.ViewExplorer],
        blockingIssues: [],
        warningIssues: [new IssueDto("CALIBRATION_INSUFFICIENT_WINDOW_POINTS", "Well A3 window has 2 points.", "Add more plateau readings or confirm the method.", "A3", "fam")],
        methodId: "BRAF-V600E-Research",
        methodVersion: "1.0",
        methodSnapshotHash: ValidSha256,
        sourceFiles: [SourceFile()],
        mappingSummary: new MappingSummaryDto(totalWells: 96, assignedWells: 12, sampleIds: ["S1", "S2"]),
        calibrationSummary: new CalibrationSummaryDto(
            StatusNames.Pass,
            runFmFam: 1000.0,
            runFmRox: 800.0,
            runFbFam: 100.0,
            runFbRox: 90.0),
        controlSummary: new ControlSummaryDto(
            StatusNames.Pass,
            wtYFamMean: 0.01,
            wtYRoxMean: 0.02,
            lqcCall: CallNames.Positive,
            lqcMeanDrPrime: 0.55),
        analysisSummary: new AnalysisSummaryDto(
            StatusNames.Valid,
            sampleCount: 2,
            positiveCount: 1,
            negativeCount: 1,
            invalidCount: 0),
        updatedUtc: IssuedUtc);

    public static ProjectSnapshotDto ProjectSnapshot() => new(
        ProjectId,
        "Research Alpha",
        RunId,
        runRevision: 0,
        runDomainState: RunStateNames.New,
        createdAtUtc: IssuedUtc);

    public static T RoundTrip<T>(T value)
        where T : class
    {
        string json = JsonSerializer.Serialize(value, typeof(T), ContractJsonContext.WireOptions);
        return (T)JsonSerializer.Deserialize(json, typeof(T), ContractJsonContext.WireOptions)!;
    }
}
