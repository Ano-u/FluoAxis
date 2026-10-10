namespace FluoAxis.Import.Tests;

using System.Text;
using FluoAxis.Domain;
using FluoAxis.Import;
using Xunit;

public sealed class AutomaticImportRecognizerTests
{
    [Theory]
    [InlineData(',')]
    [InlineData('\t')]
    [InlineData(';')]
    public async Task WideSemanticHeadersIdentifyChannelsAndUnitsButDoNotGuessRoles(char separator)
    {
        string text = $"Time(min){separator}A1_FAM{separator}A1_ROX\n0{separator}1000{separator}500\n120{separator}1000{separator}500\n";
        AutomaticRecognitionResult result = await RecognizeText(text);
        Assert.True(result.MappingReady);
        Assert.True(result.RequiresReview);
        Assert.Empty(result.Assignments);
        Assert.Equal("minutes", result.Options!.TimeUnit);
        Assert.Equal(new WellId('A', 1), Assert.Single(result.Fam!.WellColumns).Value);
        Assert.Equal(1, Assert.Single(result.Fam.WellColumns).Key);
        Assert.Equal(2, Assert.Single(result.Rox!.WellColumns).Key);
        Assert.Contains(result.Issues, issue => issue.Code == "IMPORT_AUTO_ROLES_REQUIRED");
    }

    [Fact]
    public async Task UnknownUnitRequiresReviewUntilAnExplicitUserOptionSuppliesIt()
    {
        const string text = "Time,A1_FAM,A1_ROX\n0,1000,500\n120,1000,500\n";
        AutomaticRecognitionResult unknown = await RecognizeText(text);
        Assert.False(unknown.MappingReady);
        Assert.Contains(unknown.Issues, issue => issue.Code == "IMPORT_AUTO_TIME_UNIT_REQUIRED");
        AutomaticRecognitionResult confirmed = await RecognizeText(text, new("utf-8", "comma", "invariant", "minutes"));
        Assert.True(confirmed.MappingReady);
        Assert.Equal("minutes", confirmed.Options!.TimeUnit);
    }

    [Fact]
    public async Task ExplicitFileUnitCannotBeOverriddenByTheProfile()
    {
        AutomaticRecognitionResult result = await RecognizeText("Time(seconds),A1_FAM,A1_ROX\n0,1,2\n120,3,4\n", new("utf-8", "comma", "invariant", "minutes"));
        Assert.False(result.MappingReady);
        Assert.Contains(result.Issues, issue => issue.Code == "IMPORT_AUTO_TIME_UNIT_CONFLICT");
    }

    [Theory]
    [InlineData("RFU,A1_FAM,A1_ROX\n0,10,20\n1,30,40\n")]
    [InlineData("Time(min),A1,A2\n0,10,20\n1,30,40\n")]
    [InlineData("Time(min),A1_FAM,A2_ROX\n0,10,20\n1,30,40\n")]
    [InlineData("Time(min),A1_FAM,A1_ROX\n0,10,20\n0,30,40\n")]
    [InlineData("Time(min),A1_FAM,A1_ROX\n0,10,20\n1,NaN,40\n")]
    public async Task AmbiguousOrInvalidLayoutsDoNotBecomeAutomaticMappings(string text)
    {
        AutomaticRecognitionResult result = await RecognizeText(text);
        Assert.False(result.MappingReady);
        Assert.True(result.RequiresReview);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public async Task WorkbookChannelsAndExplicitPlateMapProduceCompleteMetadata()
    {
        IReadOnlyList<IReadOnlyList<object?>> signals = [["Time(min)", "A1", "A12", "H1", "H12", "B2"], [0, 1000, 100, 100, 640, 640], [120, 1000, 100, 100, 640, 640]];
        IReadOnlyList<IReadOnlyList<object?>> roles = [["Well", "Role", "SampleId", "Replicate"],
            ["A1", "Positive100", null, null], ["A12", "NTC", null, null], ["H1", "WT", null, null], ["H12", "LQC", null, null], ["B2", "Sample", "S-42", 1]];
        byte[] bytes = WorkbookBuilder.Build(new("FAM", signals), new("ROX", signals), new("PlateMap", roles));
        using var stream = new MemoryStream(bytes);
        var read = await AutomaticImportRecognizer.RecognizeAsync(stream, ImportFileFormat.Xlsx, ImportLimits.Default, TestContext.Current.CancellationToken);
        Assert.True(read.IsSuccess);
        var result = read.Value!;
        Assert.True(result.MappingReady, string.Join(';', result.Issues.Select(issue => issue.Code)));
        Assert.False(result.RequiresReview);
        Assert.Equal(5, result.Assignments.Count);
        Assert.Equal("S-42", Assert.Single(result.Assignments, assignment => assignment.Role == WellRole.Sample).SampleId);
        Assert.Equal(WellRole.Positive100, Assert.Single(result.Assignments, assignment => assignment.Well == new WellId('A', 1)).Role);
    }

    [Fact]
    public async Task ConflictingExplicitRoleLabelsNeedReview()
    {
        var result = await RecognizeText("Time(min),A1_FAM|NTC,A1_ROX|WT\n0,100,50\n120,100,50\n");
        Assert.True(result.MappingReady);
        Assert.True(result.RequiresReview);
        Assert.Contains(result.Issues, issue => issue.Code == "IMPORT_AUTO_ROLE_CONFLICT");
        Assert.Empty(result.Assignments);
    }

    [Fact]
    public async Task PartialExplicitSampleIdentityRequiresReviewInsteadOfInventingAReplicate()
    {
        var result = await RecognizeText("Time(min),B2_FAM|Sample=S-42,B2_ROX|Sample=S-42\n0,100,50\n120,100,50\n");
        Assert.True(result.MappingReady);
        Assert.True(result.RequiresReview);
        Assert.Contains(result.Issues, issue => issue.Code == "IMPORT_AUTO_SAMPLE_METADATA_REQUIRED");
        Assert.Empty(result.Assignments);
    }

    [Fact]
    public async Task PrefixBudgetIsExplicitAndCancellationPublishesNoSuggestion()
    {
        string text = "Time(min),A1_FAM,A1_ROX\n" + string.Join('\n', Enumerable.Range(0, 300).Select(index => $"{index},100,50"));
        var result = await RecognizeText(text);
        Assert.False(result.MappingReady);
        Assert.Contains(result.Issues, issue => issue.Code == "IMPORT_AUTO_PREFIX_LIMIT");
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(text));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AutomaticImportRecognizer.RecognizeAsync(source, ImportFileFormat.DelimitedText, ImportLimits.Default, cancellation.Token));
    }

    [Fact]
    public async Task BomCannotBeOverriddenByConfiguredEncoding()
    {
        byte[] bytes = [0xff, 0xfe, .. Encoding.Unicode.GetBytes("Time(min),A1_FAM,A1_ROX\n0,1,2\n1,2,3\n")];
        using var source = new MemoryStream(bytes);
        var read = await AutomaticImportRecognizer.RecognizeAsync(source, ImportFileFormat.DelimitedText, ImportLimits.Default,
            TestContext.Current.CancellationToken, new("utf-8", "comma", "invariant", "minutes"));
        Assert.True(read.IsSuccess);
        Assert.False(read.Value!.MappingReady);
        Assert.Contains(read.Value.Issues, issue => issue.Code == "IMPORT_AUTO_ENCODING_CONFLICT");
    }

    private static async Task<AutomaticRecognitionResult> RecognizeText(string text, RecognitionParsingOptions? options = null)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var result = await AutomaticImportRecognizer.RecognizeAsync(stream, ImportFileFormat.DelimitedText,
            ImportLimits.Default, TestContext.Current.CancellationToken, options);
        Assert.True(result.IsSuccess, string.Join(';', result.Errors.Select(error => error.Code)));
        return result.Value!;
    }
}
