namespace FluoAxis.Visualization.Tests;

using System.Text;
using FluoAxis.PlotProtocol;
using FluoAxis.Visualization;
using Xunit;

public sealed class OfflinePlotDocumentTests
{
    [Fact]
    public void OfflineDocumentBindsPinnedRendererAndEscapesEmbeddedData()
    {
        var id = Guid.NewGuid();
        var spec = new PlotSpec(id, PlotKind.Scatter2D, new("Time"), new("RFU"), null,
            [new SeriesSpec("raw", "<script>bad</script>")], null, true, "research plot");
        var data = new PlotDataSnapshot(id, PlotDimensions.Two, 25_000, [new("point-1", 1, 2, null, "raw", "</script><script>bad</script>")]);
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "FluoAxis.slnx"))) repository = repository.Parent;
        byte[] renderer = File.ReadAllBytes(Path.Combine(repository!.FullName, "web", "FluoAxis.PlotHost", "vendor", "plotly.min.js"));
        string html = Encoding.UTF8.GetString(OfflinePlotDocument.Create(spec, data, renderer));
        Assert.Contains("connect-src 'none'", html, StringComparison.Ordinal);
        Assert.Contains("data:application/javascript;base64,", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>bad</script>", html, StringComparison.Ordinal);
        Assert.Contains("point-1", html, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", html, StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(html) < OfflinePlotDocument.MaximumDocumentBytes);
    }

    [Fact]
    public void OfflineDocumentRejectsUntrustedScriptOrCrossPlotData()
    {
        var id = Guid.NewGuid();
        var spec = new PlotSpec(id, PlotKind.Scatter2D, new("x"), new("y"), null, [new("raw", "raw")], null, false, "test");
        var data = new PlotDataSnapshot(id, PlotDimensions.Two, 1, [new("one", 1, 2, null, "raw")]);
        Assert.Throws<ArgumentException>(() => OfflinePlotDocument.Create(spec, data, Encoding.UTF8.GetBytes("alert('untrusted')")));
        var wrongId = new PlotDataSnapshot(Guid.NewGuid(), PlotDimensions.Two, 1, data.Points);
        Assert.Throws<ArgumentException>(() => OfflinePlotDocument.Create(spec, wrongId, []));
    }
}
