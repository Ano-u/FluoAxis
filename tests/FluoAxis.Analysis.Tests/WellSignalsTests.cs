namespace FluoAxis.Analysis.Tests;

using FluoAxis.Analysis;
using FluoAxis.Domain;
using Xunit;

public sealed class WellSignalsTests
{
    private static readonly WellId Well = new('A', 1);
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-000000000111");

    [Fact]
    public void Constructor_rejects_channels_from_different_runs()
    {
        SignalSeries fam = Series(RunId, FluorescenceChannel.Fam, [new(TimeSpan.Zero, 1)]);
        SignalSeries rox = Series(Guid.NewGuid(), FluorescenceChannel.Rox, [new(TimeSpan.Zero, 1)]);

        Assert.Throws<ArgumentException>(() => new WellSignals(Well, Channels(fam, rox)));
    }

    [Fact]
    public void Constructor_rejects_channels_with_different_time_axes()
    {
        SignalSeries fam = Series(RunId, FluorescenceChannel.Fam, [new(TimeSpan.Zero, 1), new(TimeSpan.FromMinutes(2), 2)]);
        SignalSeries rox = Series(RunId, FluorescenceChannel.Rox, [new(TimeSpan.Zero, 1), new(TimeSpan.FromMinutes(3), 2)]);

        Assert.Throws<ArgumentException>(() => new WellSignals(Well, Channels(fam, rox)));
    }

    [Fact]
    public void Constructor_rejects_a_channel_missing_acquisition_points()
    {
        SignalSeries fam = Series(RunId, FluorescenceChannel.Fam, [new(TimeSpan.Zero, 1), new(TimeSpan.FromMinutes(2), 2)]);
        SignalSeries rox = Series(RunId, FluorescenceChannel.Rox, [new(TimeSpan.Zero, 1)]);

        Assert.Throws<ArgumentException>(() => new WellSignals(Well, Channels(fam, rox)));
    }

    private static SignalSeries Series(Guid runId, FluorescenceChannel channel, IReadOnlyList<SignalPoint> points) =>
        SignalSeries.Create(runId, Well, channel, points);

    [Fact]
    public void Validated_channel_pair_is_isolated_from_mutation()
    {
        SignalSeries fam = Series(RunId, FluorescenceChannel.Fam, [new(TimeSpan.Zero, 100)]);
        SignalSeries rox = Series(RunId, FluorescenceChannel.Rox, [new(TimeSpan.Zero, 50)]);
        var source = Channels(fam, rox);
        var paired = new WellSignals(Well, source);
        source.Clear();
        Assert.Equal(2, paired.Channels.Count);
        var exposed = Assert.IsAssignableFrom<IDictionary<FluorescenceChannel, SignalSeries>>(paired.Channels);
        Assert.Throws<NotSupportedException>(() => exposed.Remove(FluorescenceChannel.Rox));
    }

    private static Dictionary<FluorescenceChannel, SignalSeries> Channels(SignalSeries fam, SignalSeries rox) => new()
    {
        [FluorescenceChannel.Fam] = fam,
        [FluorescenceChannel.Rox] = rox,
    };
}
