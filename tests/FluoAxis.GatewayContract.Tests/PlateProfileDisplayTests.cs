namespace FluoAxis.GatewayContract.Tests;

using FluoAxis.Contracts;
using FluoAxis.Presentation;
using Xunit;

public sealed class PlateProfileDisplayTests
{
    [Fact]
    public void UserSampleRectangleIsCompactAndControlsRemainSingleWells()
    {
        var assignments = UserPlateDefaults.CreateAssignments();
        Assert.Equal("B2-G11", PlateProfileDisplay.FormatWells(assignments.Where(item => item.Role == WellRoleNames.Sample).Select(item => item.Well)));
        Assert.Equal("A12", PlateProfileDisplay.FormatWells(assignments.Where(item => item.Role == WellRoleNames.Ntc).Select(item => item.Well)));
        Assert.Equal("", PlateProfileDisplay.FormatWells([]));
    }

    [Fact]
    public void SparseGeometryDoesNotInventFilledRectangleAndSortsColumnsNumerically()
    {
        Assert.Equal("A2, A10, B2", PlateProfileDisplay.FormatWells(["B2", "A10", "A2"]));
        Assert.Throws<ArgumentException>(() => PlateProfileDisplay.FormatWells(["A1", "A1"]));
        Assert.Throws<ArgumentException>(() => PlateProfileDisplay.FormatWells(Enumerable.Repeat("A1", 97)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UndefinedWellsAreDescribedAccordingToProfileContract(bool remainingSamples)
    {
        var controls = UserPlateDefaults.CreateAssignments().Where(item => item.Role is not (WellRoleNames.Sample or WellRoleNames.Ignore))
            .Select(item => new PlateProfileWellDto(item.Well, item.Role)).ToArray();
        var text = PlateProfileDisplay.DescribeCoverage(new("Controls", controls, remainingSamples));
        Assert.Contains("忽略 0 孔", text, StringComparison.Ordinal);
        Assert.Contains(remainingSamples ? "未定义的已测孔作为样本" : "92 个未定义孔需人工分配", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteUserProfileShowsActualSampleAndIgnoreCounts()
    {
        var definition = new PlateProfileDefinitionDto("Corners", UserPlateDefaults.CreateAssignments()
            .Select(item => new PlateProfileWellDto(item.Well, item.Role)).ToArray(), false);
        Assert.Equal("样本区 60 孔；忽略 32 孔", PlateProfileDisplay.DescribeCoverage(definition));
    }
}
