namespace FluoAxis.GatewayContract.Tests;

using FluoAxis.Contracts;
using FluoAxis.Presentation;
using Xunit;

public sealed class ExplorerResearchTests
{
    private static readonly Guid Run = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly string Hash = new('a', 64);

    [Theory]
    [InlineData("2^3^2", 512)]
    [InlineData("-2^2", -4)]
    [InlineData("mean(fam,rox,8)", 6)]
    [InlineData("median(8,2,4,6)", 5)]
    [InlineData("sd(2,4,6)", 2)]
    [InlineData("sqrt(9)+abs(-2)+ln(exp(1))", 6)]
    public void WhitelistedExpressionHasKnownIndependentValue(string formula, double expected)
    {
        ExplorerExpression expression = ExplorerExpression.Parse(formula, new HashSet<string>(["fam", "rox"]));
        int budget = 1000;
        var data = new Dictionary<string, double?[]> { ["fam"] = [4], ["rox"] = [6] };
        Assert.Equal(expected, expression.Evaluate(data, 0, ref budget)!.Value, 10);
    }

    [Theory]
    [InlineData("System.IO.File.ReadAllText(x)")]
    [InlineData("fam.ToString()")]
    [InlineData("eval(1)")]
    [InlineData("new x")]
    [InlineData("fam[0]")]
    [InlineData("1;2")]
    [InlineData("'secret'")]
    [InlineData("unknown+1")]
    public void UnsafeOrUnboundExpressionsAreRejected(string formula) =>
        Assert.Throws<ArgumentException>(() => ExplorerExpression.Parse(formula, new HashSet<string>(["fam"])));

    [Fact]
    public void ExpressionDepthSizeAndRuntimeBudgetAreBounded()
    {
        var names = new HashSet<string>(["fam"]);
        Assert.Throws<ArgumentException>(() => ExplorerExpression.Parse(new string('1', 1025), names));
        Assert.Throws<ArgumentException>(() => ExplorerExpression.Parse(new string('(', 17) + "1" + new string(')', 17), names));
        ExplorerExpression expression = ExplorerExpression.Parse("fam + 1", names);
        int budget = 1;
        Assert.Throws<InvalidOperationException>(() => expression.Evaluate(new Dictionary<string, double?[]> { ["fam"] = [1] }, 0, ref budget));
    }

    [Fact]
    public void NumericDomainFailuresBecomeExplicitMissingDerivedValues()
    {
        ExplorerExpression expression = ExplorerExpression.Parse("sqrt(-1)+1/0", new HashSet<string>());
        int budget = 100;
        Assert.Null(expression.Evaluate(new Dictionary<string, double?[]>(), 0, ref budget));
    }

    [Fact]
    public void DerivedFormulaEditFilterAndSortNeverAlterRawPage()
    {
        var page = new ExplorerPairPageDto(Run, 2, "A1", Hash, 0, 3,
            [new(0, 0, 1, 10), new(1, TimeSpan.TicksPerMinute, 2, 20), new(2, 2 * TimeSpan.TicksPerMinute, 3, 30)]);
        ExplorerDataModel model = ExplorerDataModel.FromPaired(page);
        string pointId = model.PointId(1);
        ExplorerViewDefinitionDto definition = Definition([new("sum", "Sum", "RFU", "fam+rox")], [new(pointId, "sum", 99)]);
        model.Apply(definition, TestContext.Current.CancellationToken);
        Assert.Equal(99, model.Value("sum", 1));
        Assert.Equal(2, model.Value("fam", 1));
        Assert.Equal(2, page.Points[1].Fam);
        Assert.False(model.CanEdit("fam"));
        Assert.True(model.CanEdit("sum"));
        Assert.Equal(new[] { 1, 2 }, model.SelectRows("sum", 20, null, "sum", descending: true));
        Assert.Throws<ArgumentException>(() => Definition([], [new(pointId, "fam", 99)]));
        Assert.Equal(20, page.Points[1].Rox);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void LowOrderSavitzkyGolayPreservesPolynomialIncludingShiftedEdges(int order)
    {
        double?[] polynomial = Enumerable.Range(0, 15).Select(index => (double?)(2 + index + index * index + (order == 3 ? index * index * index : 0))).ToArray();
        double?[] output = ExplorerDataModel.Smooth(polynomial, "savitzkyGolay", 7, order, TestContext.Current.CancellationToken);
        for (int i = 0; i < polynomial.Length; i++) Assert.Equal(polynomial[i]!.Value, output[i]!.Value, 6);
    }

    [Fact]
    public void MovingAverageHasExplicitFullWindowEdgeAndCancellation()
    {
        double?[] result = ExplorerDataModel.Smooth([1, 2, 9, 4, 5], "movingAverage", 3, 2, TestContext.Current.CancellationToken);
        Assert.Equal(4, result[0]);
        Assert.Equal(4, result[1]);
        Assert.Equal(6, result[4]);
        Assert.All(ExplorerDataModel.Smooth([1, 2], "movingAverage", 3, 2, CancellationToken.None), Assert.Null);
        using var token = new CancellationTokenSource(); token.Cancel();
        Assert.Throws<OperationCanceledException>(() => ExplorerDataModel.Smooth([1, 2, 3], "movingAverage", 3, 2, token.Token));
    }

    [Fact]
    public void ManualTableHasSeparateIdsAndCanOverrideOnlyResearchCells()
    {
        ExplorerManualPointDto[] points = [new(0, 1, 2, 3)];
        ExplorerDataModel model = ExplorerDataModel.FromManual(Run, points);
        var definition = new ExplorerViewDefinitionDto(Run, null, "A1", "manual", "Scatter3D", "x", "y", "z", "Linear", "Linear", "Linear", [], [], [new(model.PointId(0), "y", 12)], points);
        model.Apply(definition, CancellationToken.None);
        Assert.Equal(12, model.Value("y", 0));
        Assert.Equal(2, points[0].Y);
        Assert.StartsWith("manual-", model.PointId(0), StringComparison.Ordinal);
        Assert.False(model.CanEdit("index"));
    }

    [Fact]
    public void FullPageDerivedWorkRecordsLocalMeasurementWithoutPerformanceAssertion()
    {
        ExplorerPairedPointDto[] points = Enumerable.Range(0, 25000).Select(index => new ExplorerPairedPointDto(index,
            index * TimeSpan.TicksPerSecond, 100 + index * 0.1, 50 + index * 0.05)).ToArray();
        string fixtureHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(points)));
        var page = new ExplorerPairPageDto(Run, 1, "A1", fixtureHash, 0, points.Length, points);
        ExplorerDataModel model = ExplorerDataModel.FromPaired(page);
        var definition = new ExplorerViewDefinitionDto(Run, fixtureHash, "A1", "raw", "Scatter3D", "time", "fam", "smooth", "Linear", "Linear", "Linear",
            [new("sum", "Sum", "RFU", "fam+rox"), new("smooth", "Visual fit", "RFU", "sum", "savitzkyGolay", 11, 2)], [], [], []);
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        model.Apply(definition, TestContext.Current.CancellationToken);
        int[] rows = model.SelectRows("sum", 100, null, "smooth", true);
        timer.Stop();
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        Assert.Equal(25000, rows.Length);
        Assert.Equal(0, model.InvalidDerivedValues);
        Assert.Equal(100, page.Points[0].Fam);
        TestContext.Current.TestOutputHelper?.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            fixtureHash, rows = points.Length, derivedColumns = 2, smoothing = "SG order2 window11 shifted page edges",
            elapsedMilliseconds = timer.Elapsed.TotalMilliseconds, allocatedBytes = allocated,
            allocationScope = "process-wide; may include concurrent tests", runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            logicalProcessors = Environment.ProcessorCount, slaAsserted = false,
        }));
    }

    [Fact]
    public void ResearchColumnStatisticsUseOnlySelectedRowsAndDoNotMutateManualInputs()
    {
        ExplorerManualPointDto[] points = Enumerable.Range(1, 5).Select(value => new ExplorerManualPointDto(value - 1, value, 0, 0)).ToArray();
        ExplorerDataModel model = ExplorerDataModel.FromManual(Run, points);
        ExplorerColumnSummary summary = model.Summarize("x", model.SelectRows());
        Assert.Equal(5, summary.Count);
        Assert.Equal(3.0, summary.Mean!.Value, 12);
        Assert.Equal(3.0, summary.Median);
        Assert.Equal(Math.Sqrt(2.5), summary.SampleStandardDeviation!.Value, 12);
        Assert.Equal(4.0, model.Summarize("x", [2, 3, 4]).Mean!.Value, 12);
        Assert.Equal(1.0, points[0].X);
    }

    private static ExplorerViewDefinitionDto Definition(IReadOnlyList<ExplorerDerivedColumnDto> derived, IReadOnlyList<ExplorerCellOverrideDto> edits) =>
        new(Run, Hash, "A1", "raw", "Line2D", "time", "fam", "rox", "Linear", "Linear", "Linear", derived, [], edits, []);
}
