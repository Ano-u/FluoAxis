namespace FluoAxis.Reporting;

using System.Net;
using System.Text;
using FluoAxis.Contracts;

public sealed partial class ResearchReportRenderer
{
    public string RenderHtml(ReportSnapshotDto snapshot)
    {
        _ = ValidateSnapshot(snapshot);
        var html = new StringBuilder("""
            <!doctype html><html lang="zh-CN"><head><meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; img-src data:; base-uri 'none'; form-action 'none'">
            <title>FluoAxis 科研技术报告 · DRAFT</title><style>
            @page{size:A4 landscape;margin:14mm}*{box-sizing:border-box}body{font-family:'Segoe UI','Microsoft YaHei',sans-serif;color:#182c3f;line-height:1.45;margin:24px;background:white}
            h1{font-size:24px;margin-bottom:6px}h2{font-size:17px;margin-top:26px;border-bottom:2px solid #087f8c;padding-bottom:5px}
            .notice{border:2px solid #a46516;background:#fff7e6;padding:12px;margin:12px 0}.meta{font-size:11px;overflow-wrap:anywhere;color:#465568}
            .watermark{position:fixed;inset:40% 0 auto;text-align:center;font-size:58px;color:rgba(120,90,50,.09);transform:rotate(-22deg);pointer-events:none;z-index:-1}
            table{border-collapse:collapse;width:100%;font-size:11px;table-layout:auto}th,td{border:1px solid #cad4de;padding:6px;text-align:left;vertical-align:top;overflow-wrap:anywhere;white-space:pre-wrap}th{background:#eaf3f5}thead{display:table-header-group}tr{break-inside:avoid}section{break-inside:auto}.empty{color:#687785;font-style:italic}.footer{margin-top:24px;border-top:1px solid #cad4de;padding-top:10px;font-size:10px}
            .figure{break-inside:avoid}.figure svg{display:block;width:100%;max-height:112mm}.figure p{margin:4px 0}
            </style></head><body><div class="watermark">RESEARCH DRAFT · 未批准</div><h1>FluoAxis 科研技术报告</h1><div class="notice"><strong>DRAFT · Method / Golden 未批准</strong><br>
            """);
        html.Append(WebUtility.HtmlEncode(snapshot.Content.ResearchUseNotice));
        html.Append("<br>").Append(WebUtility.HtmlEncode(ReportTables.IncompleteNotice)).Append("</div>");
        html.Append("<p class=\"meta\">Report ID: ").Append(WebUtility.HtmlEncode(snapshot.ReportId))
            .Append("<br>Snapshot SHA-256: ").Append(snapshot.SnapshotSha256).Append("</p>");
        AppendEndpointFigure(html, snapshot.Content.Evidence);
        AppendSignalFigures(html, snapshot.Content.SignalFigures);
        if (snapshot.Content.PlotFigure is { } figure)
        {
            html.Append("<section class=\"figure\"><h2>科研图形附录</h2><p>").Append(WebUtility.HtmlEncode(figure.Caption))
                .Append("</p><p class=\"meta\">此图是 Explorer 的冻结显示快照，可包含手工或研究派生列，不参与正式判读。图形身份：")
                .Append(figure.PlotId.ToString("D")).Append("</p><img style=\"max-width:100%;max-height:135mm\" alt=\"科研图形显示快照\" src=\"data:image/png;base64,")
                .Append(figure.PngBase64).Append("\"></section>");
        }
        foreach (ReportTable table in ReportTables.Create(snapshot))
        {
            // Figure point values remain in the frozen JSON and XLSX appendix;
            // avoid duplicating thousands of curve rows in the printed report.
            if (table.Name == "FigureRawPoints") continue;
            html.Append("<section><h2>").Append(WebUtility.HtmlEncode(TableTitle(table.Name))).Append("</h2>");
            if (table.Rows.Count == 0) html.Append(table.Name is "Issues" or "CalibrationIssues" or "StageErrors"
                ? "<p class=\"empty\">没有已记录的问题。</p>"
                : "<p class=\"empty\">未保存此阶段的证据；未重新计算。</p>");
            else
            {
                html.Append("<table><thead><tr>");
                foreach (string column in table.Columns) html.Append("<th>").Append(WebUtility.HtmlEncode(column)).Append("</th>");
                html.Append("</tr></thead><tbody>");
                foreach (ReportCell[] row in table.Rows)
                {
                    html.Append("<tr>");
                    foreach (ReportCell cell in row) html.Append("<td>").Append(WebUtility.HtmlEncode(cell.Display)).Append("</td>");
                    html.Append("</tr>");
                }
                html.Append("</tbody></table>");
            }
            html.Append("</section>");
            if (html.Length > ReportSafety.MaxOutputBytes) throw new InvalidOperationException("REPORT_OUTPUT_LIMIT_EXCEEDED");
        }
        html.Append("<p class=\"footer\">本报告不包含完整原始 RFU 时程序列；所列端点值来自已保存的计算证据。全部结果与数值均来自冻结的已存储证据；显示格式不参与判读。此技术报告不表示科学批准或临床诊断资格。</p></body></html>");
        string output = html.ToString();
        if (Encoding.UTF8.GetByteCount(output) > ReportSafety.MaxOutputBytes) throw new InvalidOperationException("REPORT_OUTPUT_LIMIT_EXCEEDED");
        return output;
    }
}
