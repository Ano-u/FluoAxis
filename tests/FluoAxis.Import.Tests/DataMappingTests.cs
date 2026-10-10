namespace FluoAxis.Import.Tests;

using FluoAxis.Domain;
using FluoAxis.Import;
using Xunit;

public sealed class DataMappingTests
{
    [Fact]
    public void Rejects_reusing_the_same_source_signal_cells_for_both_channels()
    {
        var fam = new ChannelRegionMapping(FluorescenceChannel.Fam, "Signals", 0, 0,
            new Dictionary<int, WellId> { [1] = WellId.Parse("A1"), [2] = WellId.Parse("A2") });
        var rox = new ChannelRegionMapping(FluorescenceChannel.Rox, "signals", 0, 0,
            new Dictionary<int, WellId> { [2] = WellId.Parse("A2"), [1] = WellId.Parse("A1") });

        Assert.Throws<ArgumentException>(() => new DataMapping(Guid.NewGuid(), fam, rox));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(10, 1)]
    public void Allows_distinct_columns_or_regions_on_one_sheet(int roxHeader, int roxColumn)
    {
        var fam = new ChannelRegionMapping(FluorescenceChannel.Fam, "Signals", 0, 0,
            new Dictionary<int, WellId> { [1] = WellId.Parse("A1") }, roxHeader > 0 ? roxHeader : null);
        var rox = new ChannelRegionMapping(FluorescenceChannel.Rox, "Signals", roxHeader, 0,
            new Dictionary<int, WellId> { [roxColumn] = WellId.Parse("A1") });

        DataMapping mapping = new(Guid.NewGuid(), fam, rox);

        Assert.Same(rox, mapping.Rox);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Rejects_partial_signal_column_overlap_even_with_distinct_time_columns(int roxHeader)
    {
        var fam = new ChannelRegionMapping(FluorescenceChannel.Fam, "Signals", 0, 0,
            new Dictionary<int, WellId> { [1] = WellId.Parse("A1"), [2] = WellId.Parse("A2") }, 5);
        var rox = new ChannelRegionMapping(FluorescenceChannel.Rox, "signals", roxHeader, 4,
            new Dictionary<int, WellId> { [2] = WellId.Parse("A1"), [3] = WellId.Parse("A2") }, 10);
        Assert.Throws<ArgumentException>(() => new DataMapping(Guid.NewGuid(), fam, rox));
    }
}
