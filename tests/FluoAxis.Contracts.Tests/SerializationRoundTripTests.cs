namespace FluoAxis.Contracts.Tests;

using System.Text.Json;
using FluoAxis.Contracts;
using FluoAxis.Contracts.Serialization;
using Xunit;

public class SerializationRoundTripTests
{
    [Fact]
    public void RunSnapshot_RoundTrips()
    {
        RunSnapshotDto snapshot = TestDtos.FullRunSnapshot();
        AssertJsonRoundTrip(snapshot);
    }

    [Fact]
    public void ProjectSnapshot_RoundTrips()
    {
        ProjectSnapshotDto snapshot = TestDtos.ProjectSnapshot();
        AssertJsonRoundTrip(snapshot);
    }

    [Fact]
    public void Commands_RoundTrip()
    {
        RunCalibrationCommand command = new(
            TestDtos.RequestId,
            TestDtos.RunId,
            expectedRunRevision: 2,
            actorId: "researcher-1",
            issuedUtc: TestDtos.IssuedUtc);

        AssertJsonRoundTrip(command);
    }

    [Fact]
    public void ConfirmMappingCommand_RoundTrips()
    {
        WellColumnMappingDto[] wells = [new(1, "A1"), new(2, "A2")];
        ConfirmMappingCommand command = TestDtos.ConfirmMapping(
            new ChannelRegionMappingDto(ChannelNames.Fam, "FAM", 3, 0, wells),
            new ChannelRegionMappingDto(ChannelNames.Rox, "ROX", 4, 0, wells));

        AssertJsonRoundTrip(command);
    }

    [Fact]
    public void ConfirmMappingCommand_UsesSourceGeneratedContractMetadata()
    {
        WellColumnMappingDto[] wells = [new(1, "A1")];
        ConfirmMappingCommand command = TestDtos.ConfirmMapping(
            new ChannelRegionMappingDto(ChannelNames.Fam, "FAM", 3, 0, wells),
            new ChannelRegionMappingDto(ChannelNames.Rox, "ROX", 4, 0, wells));

        var context = new ContractJsonContext(new JsonSerializerOptions(ContractJsonContext.WireOptions));
        string json = JsonSerializer.Serialize(command, context.ConfirmMappingCommand);
        ConfirmMappingCommand? roundTripped = JsonSerializer.Deserialize(json, context.ConfirmMappingCommand);

        Assert.NotNull(roundTripped);
        Assert.Equal(ChannelNames.Fam, roundTripped.Fam.Channel);
        Assert.Equal("A1", Assert.Single(roundTripped.Rox.WellColumns).Well);
    }

    [Fact]
    public void WellAssignmentDto_RoundTrips()
    {
        WellAssignmentDto dto = new("B4", "sample", sampleId: "S1", group: "G1", replicateIndex: 2, target: "BRAF", notes: "note");
        AssertJsonRoundTrip(dto);
    }

    [Fact]
    public void AppErrorDto_RoundTrips()
    {
        AppErrorDto error = new(
            "SAMPLE_REPLICATE_CLASSIFICATION_INCONSISTENCY",
            "blocking",
            "Replicate wells disagree on direction.",
            recommendedAction: "Repeat the experiment.",
            wellId: "C5",
            channel: "fam");
        AssertJsonRoundTrip(error);
    }

    [Fact]
    public void AnalysisResultDtos_RoundTrip()
    {
        CalibrationResultDto calibration = new(
            StatusNames.Pass,
            runFmFam: 1000,
            runFmRox: 800,
            runFbFam: 100,
            runFbRox: 90,
            perWellDiagnostics: [new WellDiagnosticDto("A1", "fam", "CALIBRATION_INSUFFICIENT_WINDOW_POINTS", "Only one point.", 1.0)]);

        ControlQcResultDto control = new(
            StatusNames.Pass,
            wtYFamMean: 0.01,
            wtYRoxMean: 0.02,
            lqcCall: CallNames.Positive,
            lqcMeanDrPrime: 0.55,
            issues: [new IssueDto("CONTROL_WT_MEAN_EXCEEDED", "WT mean exceeded.", "Re-run WT controls.")]);

        SampleAnalysisDto sample = new(
            "S1",
            StatusNames.Valid,
            call: CallNames.Positive,
            meanDrPrime: 0.51,
            perWell: [new PerWellResultDto("D6", yFam: 0.3, yRox: 0.41, drPrime: 0.51, call: CallNames.Positive, traceId: Guid.NewGuid().ToString())]);

        // IReadOnlyList properties compare by reference in record equality;
        // assert JSON symmetry instead, which is the actual wire guarantee.
        AssertJsonRoundTrip(calibration);
        AssertJsonRoundTrip(control);
        AssertJsonRoundTrip(sample);
    }

    private static void AssertJsonRoundTrip<T>(T value)
        where T : class
    {
        string json = JsonSerializer.Serialize(value, typeof(T), ContractJsonContext.WireOptions);
        T? deserialized = (T?)JsonSerializer.Deserialize(json, typeof(T), ContractJsonContext.WireOptions);
        string roundTripped = JsonSerializer.Serialize(deserialized, typeof(T), ContractJsonContext.WireOptions);
        Assert.Equal(json, roundTripped);
    }

    [Fact]
    public void ImportPreview_RoundTrips()
    {
        ImportPreviewDto preview = new(
            "xlsx",
            sheetNames: ["FAM", "ROX"],
            candidateRegions:
            [
                new RegionCandidateDto("FAM", 3, 0, [new WellColumnMappingDto(1, "A1")], ChannelNames.Fam, 121),
                new RegionCandidateDto("ROX", 4, 0, [new WellColumnMappingDto(1, "A1")], ChannelNames.Rox, 121),
            ],
            validationIssues: [new IssueDto("IMPORT_DUPLICATE_TIME", "Duplicate time found.", "Fix the source file.")]);

        AssertJsonRoundTrip(preview);
    }

    [Fact]
    public void OperationProgress_RoundTrips()
    {
        OperationProgressDto progress = new(ImportStageNames.ReadingSheet, percentComplete: 42.5, message: "Reading FAM sheet.");
        AssertJsonRoundTrip(progress);
    }

    [Fact]
    public void CamelCaseAndNullOmission_AreFrozenInOutput()
    {
        AppErrorDto minimal = new("SYSTEM_UNEXPECTED", "fatal", "An unexpected condition occurred.");

        string json = JsonSerializer.Serialize(minimal, typeof(AppErrorDto), ContractJsonContext.WireOptions);

        // camelCase keys and null members omitted (frozen wire contract).
        Assert.Contains("\"code\":\"SYSTEM_UNEXPECTED\"", json);
        Assert.Contains("\"severity\":\"fatal\"", json);
        Assert.DoesNotContain("recommendedAction", json);
        Assert.DoesNotContain("fieldPath", json);
    }

    [Fact]
    public void GatewayResult_SerializesWithEnvelope()
    {
        GatewayResult<RunSnapshotDto> result = GatewayResult.Ok(TestDtos.FullRunSnapshot());

        string json = JsonSerializer.Serialize(result, typeof(GatewayResult<RunSnapshotDto>), ContractJsonContext.WireOptions);

        Assert.Contains("\"isSuccess\":true", json);
        Assert.Contains("\"value\":", json);
    }
}
