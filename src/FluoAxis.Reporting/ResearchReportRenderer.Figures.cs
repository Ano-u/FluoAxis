namespace FluoAxis.Reporting;

using System.Globalization;
using System.Net;
using System.Text;
using FluoAxis.Contracts;

public sealed partial class ResearchReportRenderer
{
    // Plot only saved coordinates. Axis placement is a display transform;
    // thresholds, normalization and verdicts are never calculated here.
    private static void AppendEndpointFigure(StringBuilder html, RunEvidenceDto evidence)
    {
        var points = evidence.Samples.SelectMany(sample => sample.PerWell.Select(well =>
            new EndpointMarker(well.Well, sample.SampleId, well.YFam, well.YRox,
                sample.Status == StatusNames.Pass && evidence.Snapshot.DomainState is RunStateNames.Analyzed or RunStateNames.Exported
                    ? well.Call : null))).Where(point => point.X.HasValue && point.Y.HasValue).ToList();
        if (evidence.Control is { WtYFamMean: { } wtX, WtYRoxMean: { } wtY })
            points.Add(new EndpointMarker("WT", "已保存 WT 参考点", wtX, wtY, "reference"));
        if (points.Count == 0) return;
        double minX = points.Min(point => point.X!.Value), maxX = points.Max(point => point.X!.Value);
        double minY = points.Min(point => point.Y!.Value), maxY = points.Max(point => point.Y!.Value);
        html.Append("<section class=\"figure\"><h2>已保存的双通道端点分布</h2>")
            .Append("<p class=\"meta\">仅展示冻结结果中的 Y FAM / Y ROX；点色沿用已保存的单孔技术分类。此图不重新判读，不添加新的阈值。</p>")
            .Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 900 440\" role=\"img\" aria-label=\"已保存双通道端点散点图\">")
            .Append("<rect width=\"900\" height=\"440\" fill=\"white\"/><rect x=\"94\" y=\"30\" width=\"730\" height=\"330\" fill=\"#f5f9fc\" stroke=\"#b8c8d5\"/>");
        for (int grid = 1; grid < 5; grid++)
        {
            html.Append(CultureInfo.InvariantCulture, $"<path d=\"M {94 + grid * 146} 30 V 360 M 94 {30 + grid * 66} H 824\" stroke=\"#dce5ec\" fill=\"none\"/>");
        }
        foreach (EndpointMarker point in points)
        {
            double x = 112 + Position(point.X!.Value, minX, maxX) * 694;
            double y = 342 - Position(point.Y!.Value, minY, maxY) * 294;
            string color = point.Call switch { CallNames.Positive => "#b95115", CallNames.Negative => "#147d9a", "reference" => "#3b3569", _ => "#677582" };
            string label = $"{point.Well} · {point.SampleId} · Y FAM={point.X.Value.ToString("G17", CultureInfo.InvariantCulture)} · Y ROX={point.Y.Value.ToString("G17", CultureInfo.InvariantCulture)}";
            html.Append(CultureInfo.InvariantCulture, $"<g><circle cx=\"{x:F3}\" cy=\"{y:F3}\" r=\"5\" fill=\"{color}\" stroke=\"white\"><title>")
                .Append(WebUtility.HtmlEncode(label)).Append("</title></circle>");
            if (points.Count <= 25)
                html.Append(CultureInfo.InvariantCulture, $"<text x=\"{x + 7:F3}\" y=\"{y - 7:F3}\" font-size=\"11\" fill=\"#23384a\">")
                    .Append(WebUtility.HtmlEncode(point.Well)).Append("</text>");
            html.Append("</g>");
        }
        html.Append("<g font-size=\"12\" fill=\"#23384a\"><text x=\"94\" y=\"381\">").Append(Number(minX))
            .Append("</text><text x=\"824\" y=\"381\" text-anchor=\"end\">").Append(Number(maxX))
            .Append("</text><text x=\"82\" y=\"345\" text-anchor=\"end\">").Append(Number(minY))
            .Append("</text><text x=\"82\" y=\"49\" text-anchor=\"end\">").Append(Number(maxY))
            .Append("</text><text x=\"440\" y=\"411\" text-anchor=\"middle\">Y FAM（已保存）</text>")
            .Append("<text x=\"18\" y=\"215\" transform=\"rotate(-90 18 215)\" text-anchor=\"middle\">Y ROX（已保存）</text></g></svg>")
            .Append("<p class=\"meta\">橙：已存 Positive；蓝：已存 Negative；紫：WT 参考点；灰：无有效分类。高密度图请结合逐孔表和 Trace ID 复核。</p></section>");
    }

    private static double Position(double value, double minimum, double maximum)
    {
        if (minimum == maximum) return 0.5;
        double scale = Math.Max(Math.Abs(minimum), Math.Abs(maximum));
        return (value / scale - minimum / scale) / (maximum / scale - minimum / scale);
    }

    private static void AppendSignalFigures(StringBuilder html, IReadOnlyList<ReportSignalFigureDto>? figures)
    {
        foreach (ReportSignalFigureDto figure in figures ?? [])
        {
            double minimum = figure.Points.Min(point => point.RawRfu), maximum = figure.Points.Max(point => point.RawRfu);
            long first = figure.Points[0].ElapsedTicks, last = figure.Points[^1].ElapsedTicks;
            html.Append("<section class=\"figure\"><h2>").Append(figure.Well).Append(" / ").Append(figure.Channel.ToUpperInvariant())
                .Append(" · Raw RFU 曲线</h2><p class=\"meta\">角色代表孔显示；未平滑、未重算。当前冻结前 ").Append(figure.Points.Count)
                .Append(" 点，共 ").Append(figure.TotalPoints).Append(" 点；全部孔判读与问题请见逐孔证据。</p>")
                .Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 900 290\" role=\"img\" aria-label=\"冻结原始信号曲线\"><rect x=\"95\" y=\"25\" width=\"730\" height=\"205\" fill=\"#f5f9fc\" stroke=\"#b8c8d5\"/><polyline fill=\"none\" stroke=\"#087f8c\" stroke-width=\"2\" points=\"");
            foreach (ReportSignalPointDto point in figure.Points)
            {
                double x = 108 + Position(point.ElapsedTicks, first, last) * 704;
                double y = 217 - Position(point.RawRfu, minimum, maximum) * 180;
                html.Append(CultureInfo.InvariantCulture, $"{x:F3},{y:F3} ");
            }
            html.Append("\"/><g font-size=\"12\" fill=\"#23384a\"><text x=\"82\" y=\"39\" text-anchor=\"end\">").Append(Number(maximum))
                .Append("</text><text x=\"82\" y=\"220\" text-anchor=\"end\">").Append(Number(minimum))
                .Append("</text><text x=\"95\" y=\"251\">").Append(Number(first / (double)TimeSpan.TicksPerMinute))
                .Append("</text><text x=\"825\" y=\"251\" text-anchor=\"end\">").Append(Number(last / (double)TimeSpan.TicksPerMinute))
                .Append("</text><text x=\"460\" y=\"279\" text-anchor=\"middle\">经过时间 / min</text><text x=\"20\" y=\"140\" transform=\"rotate(-90 20 140)\">Raw RFU</text></g></svg></section>");
        }
    }

    private static string Number(double value) => value.ToString("G6", CultureInfo.InvariantCulture);
    private sealed record EndpointMarker(string Well, string SampleId, double? X, double? Y, string? Call);

    private static string TableTitle(string name) => name switch
    {
        "Identity" or "Overview" => "报告与运行身份", "Sources" => "原始文件身份", "Mapping" => "通道与来源映射", "ImportOptions" => "显式导入选项",
        "Plate" => "孔位与样本元数据", "Calibration" => "校准摘要", "CalibrationIssues" => "校准问题",
        "Controls" => "对照质控", "Samples" => "样本结果", "PerWell" => "逐孔结果", "Issues" => "问题与建议",
        "Traces" => "计算追溯", "TraceValues" => "计算中间值", "StageIdentity" => "逐阶段输入身份",
        "StageWells" => "逐阶段端点来源", "StageWindows" => "实际计算窗口", "StageValues" => "逐阶段计算中间值",
        "StageErrors" => "逐阶段错误", "TraceSources" => "Trace 双通道端点来源", _ => name,
    };
}
