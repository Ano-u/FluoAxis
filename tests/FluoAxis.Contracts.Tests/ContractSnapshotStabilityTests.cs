namespace FluoAxis.Contracts.Tests;

using System.Text.Json;
using FluoAxis.Contracts;
using FluoAxis.Contracts.Serialization;
using Xunit;

/// <summary>
/// Freezes the JSON wire shape of the key DTOs. Any change to these outputs
/// is a contract change: bump ContractVersion and review plan.md §5 first.
/// </summary>
public class ContractSnapshotStabilityTests
{
    [Fact]
    public void RunSnapshot_JsonShape_IsFrozen()
    {
        string json = JsonSerializer.Serialize(
            TestDtos.FullRunSnapshot(),
            typeof(RunSnapshotDto),
            ContractJsonContext.WireOptions);

        Assert.Equal(
            """
            {"runId":"87654321-4321-4321-4321-cba987654321","revision":3,"domainState":"calibrationPassed","allowedActions":["runControlQc","viewExplorer"],"blockingIssues":[],"warningIssues":[{"code":"CALIBRATION_INSUFFICIENT_WINDOW_POINTS","userMessage":"Well A3 window has 2 points.","recommendedAction":"Add more plateau readings or confirm the method.","wellId":"A3","channel":"fam"}],"methodId":"BRAF-V600E-Research","methodVersion":"1.0","methodSnapshotHash":"a1b2c3d4e5f60718293a4b5c6d7e8f90112233445566778899aabbccddeeff00","sourceFiles":[{"originalFileName":"run-001.xlsx","relativeResourcePath":"data/raw/run-001.xlsx","sha256":"a1b2c3d4e5f60718293a4b5c6d7e8f90112233445566778899aabbccddeeff00","fileSizeBytes":2048,"importedAtUtc":"2026-09-01T12:00:00+00:00"}],"mappingSummary":{"totalWells":96,"assignedWells":12,"sampleIds":["S1","S2"]},"calibrationSummary":{"status":"pass","runFmFam":1000,"runFmRox":800,"runFbFam":100,"runFbRox":90},"controlSummary":{"status":"pass","wtYFamMean":0.01,"wtYRoxMean":0.02,"lqcCall":"positive","lqcMeanDrPrime":0.55},"analysisSummary":{"status":"valid","sampleCount":2,"positiveCount":1,"negativeCount":1,"invalidCount":0},"updatedUtc":"2026-09-01T12:00:00+00:00"}
            """,
            json);
    }

    [Fact]
    public void AppErrorDto_JsonShape_IsFrozen()
    {
        AppErrorDto error = new(
            "SAMPLE_REPLICATE_CLASSIFICATION_INCONSISTENCY",
            "blocking",
            "Replicate wells disagree on direction.",
            recommendedAction: "Repeat the experiment.",
            wellId: "C5",
            channel: "fam");

        string json = JsonSerializer.Serialize(error, typeof(AppErrorDto), ContractJsonContext.WireOptions);

        Assert.Equal(
            """{"code":"SAMPLE_REPLICATE_CLASSIFICATION_INCONSISTENCY","severity":"blocking","userMessage":"Replicate wells disagree on direction.","recommendedAction":"Repeat the experiment.","wellId":"C5","channel":"fam"}""",
            json);
    }

    [Fact]
    public void RunCalibrationCommand_JsonShape_IsFrozen()
    {
        RunCalibrationCommand command = new(
            TestDtos.RequestId,
            TestDtos.RunId,
            expectedRunRevision: 2,
            actorId: "researcher-1",
            issuedUtc: TestDtos.IssuedUtc);

        string json = JsonSerializer.Serialize(command, typeof(RunCalibrationCommand), ContractJsonContext.WireOptions);

        Assert.Equal(
            """{"requestId":"12345678-1234-1234-1234-123456789abc","runId":"87654321-4321-4321-4321-cba987654321","expectedRunRevision":2,"actorId":"researcher-1","issuedUtc":"2026-09-01T12:00:00+00:00","contractVersion":3}""",
            json);
    }

    [Fact]
    public void ConfirmMappingCommand_JsonShape_IsFrozen()
    {
        WellColumnMappingDto[] wells = [new(1, "A1"), new(2, "A2")];
        ConfirmMappingCommand command = new(
            TestDtos.RequestId,
            TestDtos.RunId,
            2,
            "researcher-1",
            TestDtos.IssuedUtc,
            new ChannelRegionMappingDto(ChannelNames.Fam, "FAM", 3, 0, wells),
            new ChannelRegionMappingDto(ChannelNames.Rox, "ROX", 4, 0, wells),
            "run-001.xlsx",
            "data/raw/run-001.xlsx",
            TestDtos.ValidSha256,
            2048,
            SourceFileDto.MediaTypeNames.Xlsx);

        string json = JsonSerializer.Serialize(command, typeof(ConfirmMappingCommand), ContractJsonContext.WireOptions);

        Assert.Equal(
            """{"fam":{"channel":"fam","sheetName":"FAM","headerRowIndex":3,"timeColumnIndex":0,"wellColumns":[{"columnIndex":1,"well":"A1"},{"columnIndex":2,"well":"A2"}]},"rox":{"channel":"rox","sheetName":"ROX","headerRowIndex":4,"timeColumnIndex":0,"wellColumns":[{"columnIndex":1,"well":"A1"},{"columnIndex":2,"well":"A2"}]},"originalFileName":"run-001.xlsx","relativeResourcePath":"data/raw/run-001.xlsx","sourceSha256":"a1b2c3d4e5f60718293a4b5c6d7e8f90112233445566778899aabbccddeeff00","sourceSizeBytes":2048,"sourceMediaType":"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet","requestId":"12345678-1234-1234-1234-123456789abc","runId":"87654321-4321-4321-4321-cba987654321","expectedRunRevision":2,"actorId":"researcher-1","issuedUtc":"2026-09-01T12:00:00+00:00","contractVersion":3}""",
            json);
    }

    [Fact]
    public void ImportMappingV3Fixture_DeserializesThroughSourceGeneratedContract()
    {
        string root = FindRepositoryRoot();
        string json = File.ReadAllText(Path.Combine(root, "samples", "contracts", "import-mapping-v3.json"));
        var context = new ContractJsonContext(new JsonSerializerOptions(ContractJsonContext.WireOptions));

        ConfirmMappingCommand? command = JsonSerializer.Deserialize(json, context.ConfirmMappingCommand);

        Assert.NotNull(command);
        Assert.Equal(ContractVersion.Current, command.ContractVersion);
        Assert.Equal(["A1", "A2"], command.Fam.WellColumns.Select(item => item.Well));
        Assert.Equal(command.Fam.WellColumns.Select(item => item.Well), command.Rox.WellColumns.Select(item => item.Well));
        Assert.Equal("run-001.xlsx", command.OriginalFileName);
        Assert.Equal("data/raw/run-001.xlsx", command.RelativeResourcePath);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "FluoAxis.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
