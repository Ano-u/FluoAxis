namespace FluoAxis.Reporting.Tests;

using System.Xml.Linq;
using FluoAxis.Contracts;
using Xunit;

public sealed class ReportFigureTests
{
    [Fact]
    public void Figure_uses_saved_coordinates_and_verdict_without_reclassification()
    {
        var renderer = new ResearchReportRenderer();
        string html = renderer.RenderHtml(renderer.CreateSnapshot(
            ReportFixture.Evidence(sampleId: "<script>hostile</script>", distance: 0.01), "1.0", ReportFixture.Created));
        XDocument svg = ExtractSvg(html);
        Assert.Equal(2, svg.Descendants().Count(element => element.Name.LocalName == "circle"));
        Assert.Contains("Y FAM=0.40000000000000002", svg.ToString(), StringComparison.Ordinal);
        Assert.Contains("#b95115", svg.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(svg.Descendants(), element => element.Name.LocalName == "script");
        Assert.Contains("<script>hostile</script>", svg.Descendants().First(element => element.Name.LocalName == "title").Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_run_figure_suppresses_positive_color()
    {
        var renderer = new ResearchReportRenderer();
        XDocument svg = ExtractSvg(renderer.RenderHtml(renderer.CreateSnapshot(ReportFixture.Evidence(invalid: true), "1.0", ReportFixture.Created)));
        Assert.DoesNotContain("#b95115", svg.ToString(), StringComparison.Ordinal);
        Assert.Contains("#677582", svg.ToString(), StringComparison.Ordinal);
    }

    private static XDocument ExtractSvg(string html)
    {
        int start = html.IndexOf("<svg ", StringComparison.Ordinal);
        int end = html.IndexOf("</svg>", start, StringComparison.Ordinal) + "</svg>".Length;
        return XDocument.Parse(html[start..end]);
    }

    [Fact]
    public void Malformed_signal_figure_metadata_is_rejected_before_rendering()
    {
        ReportSignalPointDto[] points = [new(0, 100)];
        Assert.Throws<ArgumentException>(() => new ReportSignalFigureDto("A1", null!, 1, points));
        Assert.Throws<ArgumentException>(() => new ReportSignalFigureDto(null!, ChannelNames.Fam, 1, points));
        Assert.Throws<ArgumentException>(() => new ReportContentDto("1.0", ReportFixture.Created, ReportFixture.Evidence(), signalFigures: [null!]));
    }

    [Fact]
    public void Signal_curve_points_are_frozen_into_hash_and_display_partial_extent()
    {
        var renderer = new ResearchReportRenderer();
        ReportSignalPointDto[] points = [new(long.MinValue, -10), new(0, 60), new(long.MaxValue, 120)];
        var view = new ReportSignalFigureDto("A1", ChannelNames.Fam, 2000, points);
        var report = renderer.CreateSnapshot(ReportFixture.Evidence(), "1.0", ReportFixture.Created, [view]);
        string before = renderer.GetCanonicalJson(report);
        points[0] = new(0, 999);
        Assert.Equal(before, renderer.GetCanonicalJson(report));
        Assert.Equal(-10, report.Content.SignalFigures![0].Points[0].RawRfu);
        string html = renderer.RenderHtml(report);
        Assert.Contains("前 3 点，共 2000 点", html, StringComparison.Ordinal);
        int start = html.LastIndexOf("<svg ", StringComparison.Ordinal);
        int end = html.IndexOf("</svg>", start, StringComparison.Ordinal) + "</svg>".Length;
        XDocument curve = XDocument.Parse(html[start..end]);
        string coordinates = curve.Descendants().Single(element => element.Name.LocalName == "polyline").Attribute("points")!.Value;
        Assert.All(coordinates.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries),
            coordinate => Assert.InRange(double.Parse(coordinate, System.Globalization.CultureInfo.InvariantCulture), 0, 900));
    }

    [Fact]
    public void Research_plot_attachment_is_revision_bound_and_encoded_in_report_identity()
    {
        const string pixel = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aFfoAAAAASUVORK5CYII=";
        RunEvidenceDto evidence = ReportFixture.Evidence();
        var figure = new ReportPlotFigureDto(evidence.Snapshot.RunId, evidence.Snapshot.Revision, Guid.NewGuid(), "研究图 <非判读>", pixel);
        var renderer = new ResearchReportRenderer();
        var plain = renderer.CreateSnapshot(evidence, "1.0", ReportFixture.Created);
        var illustrated = renderer.CreateSnapshot(evidence, "1.0", ReportFixture.Created, plotFigure: figure);
        Assert.NotEqual(plain.SnapshotSha256, illustrated.SnapshotSha256);
        string html = renderer.RenderHtml(illustrated);
        Assert.Contains("data:image/png;base64," + pixel, html, StringComparison.Ordinal);
        Assert.Contains("&lt;非判读&gt;", html, StringComparison.Ordinal);
        Assert.Contains("不参与正式判读", html, StringComparison.Ordinal);
        var stale = new ReportPlotFigureDto(evidence.Snapshot.RunId, evidence.Snapshot.Revision - 1, Guid.NewGuid(), "old", pixel);
        Assert.Throws<ArgumentException>(() => renderer.CreateSnapshot(evidence, "1.0", ReportFixture.Created, plotFigure: stale));
        Assert.Throws<ArgumentException>(() => new ReportPlotFigureDto(evidence.Snapshot.RunId, evidence.Snapshot.Revision, Guid.NewGuid(), "bad", "not-png"));
    }
}
