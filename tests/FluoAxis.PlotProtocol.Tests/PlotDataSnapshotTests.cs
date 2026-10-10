namespace FluoAxis.PlotProtocol.Tests;

using System.Text.Json;
using FluoAxis.PlotProtocol;
using Xunit;

public sealed class PlotDataSnapshotTests
{
    [Fact]
    public void OptionalErrorBarsRemainFiniteNonnegativeResearchDisplayData()
    {
        Assert.Equal(0.5, new PlotDataPoint("p-1", 1, 2, null, "s-1", errorY: 0.5).ErrorY);
        Assert.Null(new PlotDataPoint("p-1", 1, 2, null, "s-1").ErrorY);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlotDataPoint("p-1", 1, 2, null, "s-1", errorY: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlotDataPoint("p-1", 1, 2, null, "s-1", errorY: double.NaN));
    }
    private static readonly Guid PlotId = Guid.Parse("30000000-0000-0000-0000-000000000003");

    [Fact]
    public void Snapshot_defensively_copies_and_performs_exact_ordinal_lookup()
    {
        var source = new List<PlotDataPoint> { new("point-A", 1.25, -2.5, null, "series-1", "A") };
        var snapshot = new PlotDataSnapshot(PlotId, PlotDimensions.Two, 10, source);
        source.Clear();

        Assert.Single(snapshot.Points);
        Assert.True(snapshot.TryGetPoint("point-A", out PlotDataPoint? point));
        Assert.Equal(1.25, point!.X);
        Assert.False(snapshot.TryGetPoint("point-a", out _));
    }

    [Fact]
    public void Snapshot_rejects_duplicates_dimension_mismatch_nonfinite_and_excess_points()
    {
        Assert.Throws<ArgumentException>(() => new PlotDataSnapshot(PlotId, PlotDimensions.Two, 2,
            [new("same", 0, 0, null, "s"), new("same", 1, 1, null, "s")]));
        Assert.Throws<ArgumentException>(() => new PlotDataSnapshot(PlotId, PlotDimensions.Three, 1,
            [new("p", 0, 0, null, "s")]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlotDataPoint("p", double.NaN, 0, null, "s"));
        Assert.Throws<ArgumentException>(() => new PlotDataSnapshot(PlotId, PlotDimensions.Two, 1,
            [new("p1", 0, 0, null, "s"), new("p2", 1, 1, null, "s")]));
    }

    [Fact]
    public void Measure_returns_exact_two_dimensional_components()
    {
        var snapshot = new PlotDataSnapshot(PlotId, PlotDimensions.Two, 2,
            [new("a", 1, 2, null, "s"), new("b", 4, 6, null, "s")]);

        PlotDistanceMeasurement result = snapshot.Measure("a", "b");

        Assert.Equal(3, result.DeltaX);
        Assert.Equal(4, result.DeltaY);
        Assert.Null(result.DeltaZ);
        Assert.Equal(5, result.Distance);
    }

    [Fact]
    public void Measure_returns_stable_three_dimensional_components()
    {
        var snapshot = new PlotDataSnapshot(PlotId, PlotDimensions.Three, 2,
            [new("a", 1e200, 1e200, 1e200, "s"), new("b", 2e200, 3e200, 3e200, "s")]);

        PlotDistanceMeasurement result = snapshot.Measure("a", "b");

        Assert.Equal(1e200, result.DeltaX);
        Assert.Equal(2e200, result.DeltaY);
        Assert.Equal(2e200, result.DeltaZ);
        Assert.Equal(3e200, result.Distance, 12);
    }

    [Fact]
    public void Snapshot_roundtrips_with_schema_version_and_rebuilds_lookup_index()
    {
        var snapshot = new PlotDataSnapshot(PlotId, PlotDimensions.Three, 25,
            [new("point-1", 1, 2, 3, "series-1", "Exact point")]);

        string json = JsonSerializer.Serialize(snapshot, PlotProtocolJsonContext.Default.PlotDataSnapshot);
        PlotDataSnapshot? restored = JsonSerializer.Deserialize(json, PlotProtocolJsonContext.Default.PlotDataSnapshot);

        Assert.Contains("\"schemaVersion\":1", json, StringComparison.Ordinal);
        Assert.NotNull(restored);
        Assert.True(restored.TryGetPoint("point-1", out PlotDataPoint? point));
        Assert.Equal(3, point!.Z);
    }

    [Fact]
    public void Measure_rejects_unknown_point_identity_without_echoing_it()
    {
        var snapshot = new PlotDataSnapshot(PlotId, PlotDimensions.Two, 1, [new("known", 0, 0, null, "s")]);
        KeyNotFoundException error = Assert.Throws<KeyNotFoundException>(() => snapshot.Measure("known", "missing"));
        Assert.DoesNotContain("missing", error.Message, StringComparison.Ordinal);
    }
}
