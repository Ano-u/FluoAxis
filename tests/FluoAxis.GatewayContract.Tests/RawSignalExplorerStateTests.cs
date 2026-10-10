namespace FluoAxis.GatewayContract.Tests;

using FluoAxis.Contracts;
using FluoAxis.Presentation;
using Xunit;

public sealed class RawSignalExplorerStateTests
{
    private static readonly Guid Run = Guid.Parse("44444444-4444-4444-8444-444444444444");

    [Fact]
    public void PagingReplacesOnlyTheCurrentPageAndUsesStoredPointIdentity()
    {
        var model = new RawSignalExplorerState();
        var first = new RawSignalPageDto(Run, 3, "A1", ChannelNames.Fam, 3, [new(0, 0, 100), new(1, TimeSpan.TicksPerMinute, 120)]);
        Assert.True(model.TryApply(first, Run, 3, "A1", ChannelNames.Fam, 0));
        Assert.False(model.CanPrevious);
        Assert.True(model.CanNext);
        var next = new RawSignalPageDto(Run, 3, "A1", ChannelNames.Fam, 3, [new(2, 2 * TimeSpan.TicksPerMinute, 130)]);
        Assert.True(model.TryApply(next, Run, 3, "A1", ChannelNames.Fam, 2));
        Assert.Same(next, model.Page);
        Assert.True(model.CanPrevious);
        Assert.False(model.CanNext);
        Assert.EndsWith("-A1-fam-2", RawSignalExplorerState.PointId(next, next.Points[0]), StringComparison.Ordinal);
        Assert.Equal(2.0, RawSignalExplorerState.DisplayMinutes(next.Points[0]));
        Assert.Equal(130.0, next.Points[0].Rfu);
    }

    [Fact]
    public void WrongRevisionRunOrSeriesCannotReplaceTheVisiblePage()
    {
        var model = new RawSignalExplorerState();
        var first = new RawSignalPageDto(Run, 3, "A1", ChannelNames.Fam, 1, [new(0, 0, 100)]);
        Assert.True(model.TryApply(first, Run, 3, "A1", ChannelNames.Fam, 0));
        Assert.False(model.TryApply(first, Run, 4, "A1", ChannelNames.Fam, 0));
        Assert.False(model.TryApply(first, Guid.NewGuid(), 3, "A1", ChannelNames.Fam, 0));
        Assert.False(model.TryApply(first, Run, 3, "A2", ChannelNames.Fam, 0));
        Assert.False(model.TryApply(first, Run, 3, "A1", ChannelNames.Rox, 0));
        Assert.Same(first, model.Page);
        model.Clear();
        Assert.Null(model.Page);
        Assert.False(model.CanNext);
    }

    [Fact]
    public void PointIdsDoNotDependOnPagePositionOrDisplayRounding()
    {
        var point = new RawSignalPointDto(12345, 1, 0.12345678901234567);
        var page = new RawSignalPageDto(Run, 5, "H12", ChannelNames.Rox, 20000, [point]);
        var samePointDifferentPage = new RawSignalPageDto(Run, 5, "H12", ChannelNames.Rox, 20000, [new(12344, 0, 5), point]);
        Assert.Equal(RawSignalExplorerState.PointId(page, point), RawSignalExplorerState.PointId(samePointDifferentPage, point));
        Assert.Equal(1.0 / TimeSpan.TicksPerMinute, RawSignalExplorerState.DisplayMinutes(point));
        Assert.Equal(0.12345678901234567, point.Rfu);
    }
}
