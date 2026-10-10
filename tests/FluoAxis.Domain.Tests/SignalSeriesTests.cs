namespace FluoAxis.Domain.Tests;

using Xunit;

public class SignalSeriesTests
{
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static SignalPoint P(double minutes, double rfu) => new(TimeSpan.FromMinutes(minutes), rfu);

    [Fact]
    public void Create_accepts_strictly_ascending_finite_points()
    {
        var series = SignalSeries.Create(RunId, new WellId('A', 1), FluorescenceChannel.Fam, [P(0, 100), P(2, 110), P(120, 200)]);

        Assert.Equal(3, series.Points.Count);
        Assert.Equal(RunId, series.RunId);
        Assert.Equal(new WellId('A', 1), series.Well);
        Assert.Equal(FluorescenceChannel.Fam, series.Channel);
    }

    [Fact]
    public void Create_accepts_empty_point_set()
    {
        var series = SignalSeries.Create(RunId, new WellId('H', 12), FluorescenceChannel.Rox, []);

        Assert.Empty(series.Points);
    }

    [Fact]
    public void Create_rejects_duplicate_elapsed_times()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            SignalSeries.Create(RunId, new WellId('A', 1), FluorescenceChannel.Fam, [P(0, 90), P(2, 100), P(2, 101)]));

        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_rejects_out_of_order_points()
    {
        Assert.Throws<ArgumentException>(() =>
            SignalSeries.Create(RunId, new WellId('A', 1), FluorescenceChannel.Fam, [P(5, 100), P(0, 90)]));
    }

    [Fact]
    public void Create_rejects_non_finite_rfu()
    {
        Assert.Throws<ArgumentException>(() =>
            SignalSeries.Create(RunId, new WellId('A', 1), FluorescenceChannel.Fam, [P(0, double.NaN)]));
        Assert.Throws<ArgumentException>(() =>
            SignalSeries.Create(RunId, new WellId('A', 1), FluorescenceChannel.Fam, [P(0, double.PositiveInfinity)]));
        Assert.Throws<ArgumentException>(() =>
            SignalSeries.Create(RunId, new WellId('A', 1), FluorescenceChannel.Fam, [P(0, double.NegativeInfinity)]));
    }

    [Fact]
    public void Create_rejects_null_points()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SignalSeries.Create(RunId, new WellId('A', 1), FluorescenceChannel.Fam, null!));
    }

    [Fact]
    public void Create_stores_a_defensive_copy()
    {
        var points = new List<SignalPoint> { P(0, 100), P(2, 110) };

        var series = SignalSeries.Create(RunId, new WellId('A', 1), FluorescenceChannel.Fam, points);
        points.Add(P(4, 120));

        Assert.Equal(2, series.Points.Count);
    }

    [Fact]
    public void Validated_raw_points_cannot_be_mutated_through_a_collection_cast()
    {
        var series = SignalSeries.Create(RunId, new WellId('A', 1), FluorescenceChannel.Fam, [P(0, 100), P(120, 460)]);
        var exposed = Assert.IsAssignableFrom<IList<SignalPoint>>(series.Points);
        Assert.Throws<NotSupportedException>(() => exposed[1] = P(120, double.NaN));
        Assert.Equal(460, series.Points[1].Rfu);
    }
}
