namespace FluoAxis.Contracts.Tests;

using FluoAxis.Contracts;
using FluoAxis.Domain;
using Xunit;

public class ConstructionGuardTests
{
    [Theory]
    [MemberData(nameof(InvalidManagedPaths))]
    public void SourceFileDto_RejectsInvalidRelativePaths(string path)
    {
        Assert.ThrowsAny<Exception>(() => TestDtos.SourceFile(path));
    }

    public static TheoryData<string> InvalidManagedPaths() => new()
    {
        string.Concat("C", ":", "\\", "data", "\\", "file.xlsx"),
        string.Concat("/", "rooted/file.xlsx"),
        "../escape.xlsx",
        "a/../../escape.xlsx",
        string.Concat("file", ":", "///", "c", ":", "/x.xlsx"),
        string.Empty,
    };

    [Fact]
    public void SourceFileDto_RejectsShortHash()
    {
        Assert.Throws<ArgumentException>(() => new SourceFileDto("a.xlsx", "data/raw/a.xlsx", "abc", 10, TestDtos.IssuedUtc));
    }

    [Fact]
    public void SourceFileDto_RejectsFileNameWithPathSeparator()
    {
        Assert.Throws<ArgumentException>(() => new SourceFileDto("dir/file.xlsx", "data/raw/a.xlsx", TestDtos.ValidSha256, 10, TestDtos.IssuedUtc));
    }

    [Theory]
    [InlineData("run.xlsx", "data/raw/run.csv")]
    [InlineData("run.csv", "data/raw/run.tsv")]
    [InlineData("run.bin", "data/raw/run.bin")]
    public void SourceFileDto_RejectsMismatchedOrUnsupportedFormats(string originalName, string resourcePath)
    {
        Assert.Throws<ArgumentException>(() => new SourceFileDto(
            originalName, resourcePath, TestDtos.ValidSha256, 10, TestDtos.IssuedUtc));
    }

    [Theory]
    [InlineData("xlsx", SourceFileDto.MediaTypeNames.Xlsx)]
    [InlineData("csv", SourceFileDto.MediaTypeNames.Csv)]
    [InlineData("tsv", SourceFileDto.MediaTypeNames.Tsv)]
    [InlineData("txt", SourceFileDto.MediaTypeNames.Txt)]
    public void SourceFileDto_RoundTripsEverySupportedMediaType(string extension, string mediaType)
    {
        var source = new SourceFileMetadata(
            $"run.{extension}",
            RelativePath.Parse($"data/raw/source-001.{extension}"),
            new HexHash(TestDtos.ValidSha256),
            42,
            TestDtos.IssuedUtc,
            mediaType);

        SourceFileDto dto = SourceFileDto.FromDomain(source);
        SourceFileMetadata reconstructed = dto.ToDomain();

        Assert.Equal(source, reconstructed);
        Assert.Equal(mediaType, reconstructed.MediaType);
    }

    [Fact]
    public void SourceFileDto_FromDomainRejectsMediaTypeThatContradictsExtension()
    {
        var source = new SourceFileMetadata(
            "run.csv",
            RelativePath.Parse("data/raw/source-001.csv"),
            new HexHash(TestDtos.ValidSha256),
            42,
            TestDtos.IssuedUtc,
            SourceFileDto.MediaTypeNames.Xlsx);

        Assert.Throws<ArgumentException>(() => SourceFileDto.FromDomain(source));
    }

    [Theory]
    [InlineData("lowercase_code")]
    [InlineData("NO_PREFIX")]
    [InlineData("IMPORT-with-dash")]
    [InlineData("")]
    public void AppErrorDto_RejectsNonContractCodes(string code)
    {
        Assert.Throws<ArgumentException>(() => new AppErrorDto(code, "info", "message"));
    }

    [Theory]
    [InlineData("info")]
    [InlineData("warning")]
    [InlineData("blocking")]
    [InlineData("fatal")]
    public void AppErrorDto_AcceptsSeverityVocabulary(string severity)
    {
        AppErrorDto error = new("SYSTEM_UNEXPECTED", severity, "message");
        Assert.Equal(severity, error.Severity);
    }

    [Theory]
    [InlineData("loud")]
    [InlineData("BLOCKING")]
    public void AppErrorDto_RejectsNonVocabularySeverity(string severity)
    {
        Assert.Throws<ArgumentException>(() => new AppErrorDto("SYSTEM_UNEXPECTED", severity, "message"));
    }

    [Fact]
    public void CommandBase_Validation()
    {
        Assert.ThrowsAny<ArgumentException>(() => new RunCalibrationCommand(
            Guid.Empty, TestDtos.RunId, 0, "actor", TestDtos.IssuedUtc));
        Assert.ThrowsAny<ArgumentException>(() => new RunCalibrationCommand(
            TestDtos.RequestId, Guid.Empty, 0, "actor", TestDtos.IssuedUtc));
        Assert.ThrowsAny<ArgumentException>(() => new RunCalibrationCommand(
            TestDtos.RequestId, TestDtos.RunId, -1, "actor", TestDtos.IssuedUtc));
        Assert.Throws<ArgumentException>(() => new RunCalibrationCommand(
            TestDtos.RequestId, TestDtos.RunId, 0, " ", TestDtos.IssuedUtc));
        Assert.Throws<ArgumentException>(() => new RunCalibrationCommand(
            TestDtos.RequestId, TestDtos.RunId, 0, "actor", TestDtos.IssuedUtc.ToOffset(TimeSpan.FromHours(8))));
    }

    [Fact]
    public void OldContractVersion_StaysConstructible_AndFlagsIsCurrentVersion()
    {
        RunCalibrationCommand legacy = new(
            TestDtos.RequestId,
            TestDtos.RunId,
            expectedRunRevision: 0,
            actorId: "actor",
            issuedUtc: TestDtos.IssuedUtc,
            contractVersion: 0);

        Assert.False(legacy.IsCurrentVersion);
        Assert.Equal(0, legacy.ContractVersion);
    }

    [Fact]
    public void ChannelRegionMappingDto_RejectsInvalidCoordinatesAndDuplicateBindings()
    {
        Assert.Throws<ArgumentException>(() =>
            new ChannelRegionMappingDto(null!, "FAM", 0, 0, [new WellColumnMappingDto(1, "A1")]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ChannelRegionMappingDto(ChannelNames.Fam, "FAM", -1, 0, [new WellColumnMappingDto(1, "A1")]));
        Assert.Throws<ArgumentException>(() =>
            new ChannelRegionMappingDto(ChannelNames.Fam, "FAM", 0, 1, [new WellColumnMappingDto(1, "A1")]));
        Assert.Throws<ArgumentException>(() =>
            new ChannelRegionMappingDto(
                ChannelNames.Fam,
                "FAM",
                0,
                0,
                [new WellColumnMappingDto(1, "A1"), new WellColumnMappingDto(1, "A2")]));
        Assert.Throws<ArgumentException>(() =>
            new ChannelRegionMappingDto(
                ChannelNames.Fam,
                "FAM",
                0,
                0,
                [new WellColumnMappingDto(1, "A1"), new WellColumnMappingDto(2, "A1")]));
    }

    [Fact]
    public void RegionCandidateDto_AllowsAmbiguityButRejectsInvalidSuggestionsAndOverlap()
    {
        RegionCandidateDto ambiguous = new("Sheet1", null, null);
        Assert.Null(ambiguous.HeaderRowIndex);
        Assert.Null(ambiguous.TimeColumnIndex);
        Assert.Null(ambiguous.SuggestedChannel);

        Assert.Throws<ArgumentException>(() => new RegionCandidateDto("Sheet1", 0, 0, suggestedChannel: "cy5"));
        Assert.Throws<ArgumentException>(() =>
            new RegionCandidateDto("Sheet1", 0, 1, [new WellColumnMappingDto(1, "A1")]));
    }

    [Fact]
    public void ConfirmMappingCommand_RequiresFixedChannelsAndMatchingWellSets()
    {
        ChannelRegionMappingDto fam = Mapping(ChannelNames.Fam, "FAM", "A1", "A2");
        ChannelRegionMappingDto rox = Mapping(ChannelNames.Rox, "ROX", "A1");

        Assert.Throws<ArgumentException>(() => new ConfirmMappingCommand(
            TestDtos.RequestId, TestDtos.RunId, 0, "actor", TestDtos.IssuedUtc, fam, rox,
            "run-001.xlsx", "data/raw/run-001.xlsx", TestDtos.ValidSha256, 2048, SourceFileDto.MediaTypeNames.Xlsx));
        Assert.Throws<ArgumentException>(() => new ConfirmMappingCommand(
            TestDtos.RequestId, TestDtos.RunId, 0, "actor", TestDtos.IssuedUtc,
            Mapping(ChannelNames.Rox, "ROX", "A1"), Mapping(ChannelNames.Fam, "FAM", "A1"),
            "run-001.xlsx", "data/raw/run-001.xlsx", TestDtos.ValidSha256, 2048, SourceFileDto.MediaTypeNames.Xlsx));
    }

    [Theory]
    [InlineData("Signals")]
    [InlineData("signals")]
    public void ConfirmMappingCommand_RejectsReusingTheSameSourceSignalCells(string roxSheet)
    {
        ChannelRegionMappingDto fam = new(ChannelNames.Fam, "Signals", 0, 0, [new(1, "A1"), new(2, "A2")]);
        ChannelRegionMappingDto rox = new(ChannelNames.Rox, roxSheet, 0, 0, [new(2, "A2"), new(1, "A1")]);

        Assert.Throws<ArgumentException>(() => new ConfirmMappingCommand(
            TestDtos.RequestId, TestDtos.RunId, 0, "actor", TestDtos.IssuedUtc, fam, rox,
            "run-001.xlsx", "data/raw/run-001.xlsx", TestDtos.ValidSha256, 2048, SourceFileDto.MediaTypeNames.Xlsx));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(10, 1)]
    public void ConfirmMappingCommand_AllowsDistinctColumnsOrRegionsOnOneSheet(int roxHeader, int roxColumn)
    {
        ChannelRegionMappingDto fam = new(ChannelNames.Fam, "Signals", 0, 0, [new(1, "A1")], endRowIndex: roxHeader > 0 ? roxHeader : null);
        ChannelRegionMappingDto rox = new(ChannelNames.Rox, "Signals", roxHeader, 0, [new(roxColumn, "A1")]);

        ConfirmMappingCommand command = new(
            TestDtos.RequestId, TestDtos.RunId, 0, "actor", TestDtos.IssuedUtc, fam, rox,
            "run-001.xlsx", "data/raw/run-001.xlsx", TestDtos.ValidSha256, 2048, SourceFileDto.MediaTypeNames.Xlsx);

        Assert.Same(rox, command.Rox);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ConfirmMappingCommandRejectsPartiallySharedSignalCells(int roxHeader)
    {
        var fam = new ChannelRegionMappingDto(ChannelNames.Fam, "Signals", 0, 0, [new(1, "A1"), new(2, "A2")]);
        var rox = new ChannelRegionMappingDto(ChannelNames.Rox, "Signals", roxHeader, 3, [new(2, "A1"), new(4, "A2")]);
        Assert.Throws<ArgumentException>(() => new ConfirmMappingCommand(
            TestDtos.RequestId, TestDtos.RunId, 0, "actor", TestDtos.IssuedUtc, fam, rox,
            "run-001.xlsx", "data/raw/run-001.xlsx", TestDtos.ValidSha256, 2048, SourceFileDto.MediaTypeNames.Xlsx));
    }

    [Fact]
    public void ConfirmMappingCommand_RejectsUnsafeStagedSourceIdentity()
    {
        ChannelRegionMappingDto fam = Mapping(ChannelNames.Fam, "FAM", "A1");
        ChannelRegionMappingDto rox = Mapping(ChannelNames.Rox, "ROX", "A1");

        // The forbidden form is assembled at run time so this test file never
        // carries a literal rooted path (FX-20260903-004).
        string rootedResource = "C:" + "/data/raw/run-001.xlsx";

        // A rooted resource path and a bare-name violation are both rejected
        // at construction, so no machine location can enter the contract.
        Assert.Throws<FormatException>(() => new ConfirmMappingCommand(
            TestDtos.RequestId, TestDtos.RunId, 0, "actor", TestDtos.IssuedUtc, fam, rox,
            "run-001.xlsx", rootedResource, TestDtos.ValidSha256, 2048, SourceFileDto.MediaTypeNames.Xlsx));
        Assert.Throws<ArgumentException>(() => new ConfirmMappingCommand(
            TestDtos.RequestId, TestDtos.RunId, 0, "actor", TestDtos.IssuedUtc, fam, rox,
            "data/raw/run-001.xlsx", "data/raw/run-001.xlsx", TestDtos.ValidSha256, 2048, SourceFileDto.MediaTypeNames.Xlsx));
    }

    [Fact]
    public void MappingDtos_SnapshotCallerOwnedCollections()
    {
        WellColumnMappingDto[] wells = [new(1, "A1")];
        ChannelRegionMappingDto mapping = new(ChannelNames.Fam, "FAM", 0, 0, wells);
        wells[0] = new WellColumnMappingDto(2, "A2");

        Assert.Equal("A1", Assert.Single(mapping.WellColumns).Well);
        Assert.False(mapping.WellColumns is WellColumnMappingDto[]);
    }

    [Fact]
    public void RunSnapshotDto_RejectsUnknownAction()
    {
        Assert.Throws<ArgumentException>(() => new RunSnapshotDto(
            TestDtos.RunId,
            revision: 0,
            domainState: RunStateNames.New,
            allowedActions: ["deleteEverything"],
            blockingIssues: [],
            warningIssues: [],
            methodId: "m",
            methodVersion: "1.0",
            methodSnapshotHash: TestDtos.ValidSha256,
            sourceFiles: []));
    }

    [Fact]
    public void RunSnapshotDto_RejectsUnknownRunState()
    {
        Assert.Throws<ArgumentException>(() => new RunSnapshotDto(
            TestDtos.RunId,
            revision: 0,
            domainState: "half-done",
            allowedActions: [],
            blockingIssues: [],
            warningIssues: [],
            methodId: "m",
            methodVersion: "1.0",
            methodSnapshotHash: TestDtos.ValidSha256,
            sourceFiles: []));
    }

    [Theory]
    [InlineData("Z1")]
    [InlineData("A13")]
    public void WellAssignmentDto_RejectsInvalidWell(string well)
    {
        Assert.Throws<FormatException>(() => new WellAssignmentDto(well, "ntc"));
    }

    [Fact]
    public void WellAssignmentDto_NormalizesLowercaseRowLetter()
    {
        WellAssignmentDto dto = new("a1", "ntc");
        Assert.Equal("A1", dto.Well);
    }

    [Theory]
    [InlineData("unknownRole")]
    [InlineData("NTC")]
    public void WellAssignmentDto_RejectsNonVocabularyRole(string role)
    {
        Assert.Throws<ArgumentException>(() => new WellAssignmentDto("A1", role));
    }

    [Fact]
    public void WellAssignmentDto_ToDomain_RoundTripsRole()
    {
        WellAssignmentDto dto = new("A1", "lqc01Percent", sampleId: null);
        WellAssignment domain = dto.ToDomain();

        Assert.Equal(WellRole.Lqc01Percent, domain.Role);
        Assert.Null(domain.Sample);
    }

    [Fact]
    public void WellAssignmentDto_ToDomain_SampleCarriesMetadata()
    {
        WellAssignmentDto dto = new("B2", "sample", sampleId: "S1", group: "G1", replicateIndex: 3, target: "BRAF", notes: "note");
        WellAssignment domain = dto.ToDomain();

        Assert.Equal(WellRole.Sample, domain.Role);
        Assert.Equal("S1", domain.Sample!.SampleId);
        Assert.Equal(3, domain.Sample.ReplicateIndex);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void WellAssignmentDto_RejectsSampleWithoutSampleId(string? sampleId)
    {
        Assert.Throws<ArgumentException>(() => new WellAssignmentDto("B3", WellRoleNames.Sample, sampleId: sampleId));
    }

    [Fact]
    public void AnalysisSummaryDto_CountsMustAddUp()
    {
        Assert.Throws<ArgumentException>(() => new AnalysisSummaryDto(
            StatusNames.Valid, sampleCount: 5, positiveCount: 1, negativeCount: 1, invalidCount: 1));
    }

    [Fact]
    public void GatewayResult_Fail_RequiresError()
    {
        Assert.Throws<ArgumentException>(() => GatewayResult.Fail<RunSnapshotDto>());
    }

    [Fact]
    public void GatewayResult_Fail_NeverCarriesValue()
    {
        AppErrorDto error = new("SYSTEM_UNEXPECTED", "blocking", "message");
        GatewayResult<RunSnapshotDto> result = GatewayResult.Fail<RunSnapshotDto>(error);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void DomainToDto_Mapping_RoundTripsSeverity()
    {
        AppError domain = new(ErrorCodes.CalibrationMissingEndpoint, ErrorSeverity.Blocking, "No endpoint.", "detail");
        AppErrorDto dto = domain.ToDto();
        AppError back = dto.ToDomain();

        Assert.Equal(domain.Code, back.Code);
        Assert.Equal(domain.Severity, back.Severity);
        Assert.Equal(domain.UserMessage, back.UserMessage);
    }

    private static ChannelRegionMappingDto Mapping(string channel, string sheet, params string[] wells) =>
        new(
            channel,
            sheet,
            0,
            0,
            [.. wells.Select((well, index) => new WellColumnMappingDto(index + 1, well))]);
}
