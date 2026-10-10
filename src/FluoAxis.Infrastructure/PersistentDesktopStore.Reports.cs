namespace FluoAxis.Infrastructure;

using System.Security.Cryptography;
using System.Text;
using FluoAxis.Contracts;
using FluoAxis.Domain;
using FluoAxis.Reporting;

public sealed partial class PersistentDesktopStore
{
    // Bounded to the last viewed report; consecutive preview/format exports use
    // the same frozen identity. A changed Run or changed evidence creates a new one.
    private ReportSnapshotDto? latestReport;
    private ReportPlotFigureDto? latestPlotFigure;

    public Task<GatewayResult<bool>> AttachReportPlotFigureAsync(ReportPlotFigureDto figure, CancellationToken cancellationToken) => ExecuteAsync(async () =>
    {
        ArgumentNullException.ThrowIfNull(figure);
        var run = await gateway.GetRunAsync(new GetRunQuery(Guid.NewGuid(), figure.RunId), cancellationToken).ConfigureAwait(false);
        if (!run.IsSuccess) return GatewayResult.Fail<bool>(run.Errors.ToArray());
        if (run.Value!.Revision != figure.RunRevision) return Failure<bool>("SYSTEM_STALE_RUN_REVISION", "运行已更新，请重新绘图后添加报告附图。");
        if (figure.Caption != SafeDiagnostics.RedactMachinePaths(figure.Caption)) return Failure<bool>("SYSTEM_REPORT_FIGURE_INVALID", "附图标题包含不可输出的路径。");
        latestPlotFigure = figure;
        return GatewayResult.Ok(true);
    }, cancellationToken);

    public Task<GatewayResult<ReportSnapshotDto>> CreateReportSnapshotAsync(Guid runId, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            GatewayResult<RunEvidenceDto> evidence = await gateway.GetRunEvidenceAsync(
                new GetRunQuery(Guid.NewGuid(), runId), cancellationToken).ConfigureAwait(false);
            if (!evidence.IsSuccess) return GatewayResult.Fail<ReportSnapshotDto>(evidence.Errors.ToArray());
            if (evidence.Value!.Snapshot.DomainState is not (RunStateNames.Analyzed or RunStateNames.RunInvalid or RunStateNames.Exported))
                return Failure<ReportSnapshotDto>("SYSTEM_REPORT_NOT_READY", "请先完成分析或复核无效运行，再生成技术报告。");
            var renderer = new ResearchReportRenderer();
            string software = typeof(PersistentDesktopStore).Assembly.GetName().Version?.ToString() ?? "unknown";
            var figures = new List<ReportSignalFigureDto>();
            // A role-representative display view is bounded separately from the
            // complete analysis evidence. Every curve states its page extent.
            foreach (WellAssignmentDto assignment in evidence.Value.Assignments.GroupBy(item => item.Role, StringComparer.Ordinal)
                .Where(group => group.Key != WellRoleNames.Ignore).Select(group => group.OrderBy(item => item.Well, StringComparer.Ordinal).First()).Take(6))
            {
                foreach (FluorescenceChannel channel in new[] { FluorescenceChannel.Fam, FluorescenceChannel.Rox })
                {
                    var page = await repository.GetRawSignalPageAsync(runId, WellId.Parse(assignment.Well), channel, 0, 500, cancellationToken).ConfigureAwait(false);
                    if (page.Points.Count > 0) figures.Add(new ReportSignalFigureDto(assignment.Well, channel == FluorescenceChannel.Fam ? ChannelNames.Fam : ChannelNames.Rox,
                        page.TotalPoints, page.Points.Select(point => new ReportSignalPointDto(point.ElapsedTicks, point.Rfu)).ToArray()));
                }
            }
            ReportSnapshotDto snapshot;
            ReportPlotFigureDto? plotFigure = latestPlotFigure is { } storedFigure && storedFigure.RunId == runId
                && storedFigure.RunRevision == evidence.Value.Snapshot.Revision ? storedFigure : null;
            if (latestReport is { } prior && prior.Content.Evidence.Snapshot.RunId == runId
                && prior.Content.Evidence.Snapshot.Revision == evidence.Value.Snapshot.Revision)
            {
                var candidate = renderer.CreateSnapshot(evidence.Value, software, prior.Content.CreatedUtc, figures, plotFigure);
                snapshot = candidate.SnapshotSha256 == prior.SnapshotSha256 ? prior
                    : renderer.CreateSnapshot(evidence.Value, software, DateTimeOffset.UtcNow, figures, plotFigure);
            }
            else snapshot = renderer.CreateSnapshot(evidence.Value, software, DateTimeOffset.UtcNow, figures, plotFigure);
            string reportFolder = Resolve(RelativePath.Parse("reports"));
            Directory.CreateDirectory(reportFolder);
            string identity = snapshot.SnapshotSha256;
            string canonical = renderer.GetCanonicalJson(snapshot);
            byte[] snapshotBytes = Encoding.UTF8.GetBytes(canonical);
            if (snapshotBytes.Length > 16 * 1024 * 1024)
                return Failure<ReportSnapshotDto>("SYSTEM_REPORT_LIMIT_EXCEEDED", "报告快照超过大小上限。");
            string entry = Resolve(RelativePath.Parse($"reports/{identity}.json"));
            if (!File.Exists(entry))
            {
                string temporary = Resolve(RelativePath.Parse($"reports/snapshot-{Guid.NewGuid():N}.tmp"));
                try
                {
                    await using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
                    {
                        await output.WriteAsync(snapshotBytes, cancellationToken).ConfigureAwait(false);
                        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Move(temporary, entry, false);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            else
            {
                await using FileStream existing = new(entry, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
                string hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(existing, cancellationToken).ConfigureAwait(false));
                if (existing.Length != snapshotBytes.Length || hash != identity)
                    return Failure<ReportSnapshotDto>("SYSTEM_REPORT_HASH_MISMATCH", "已保存报告快照校验失败，原文件保持不变。");
            }
            latestReport = snapshot;
            return GatewayResult.Ok(snapshot);
        }, cancellationToken);

    public Task<GatewayResult<ReportExportDto>> CreateReportExportAsync(
        ReportSnapshotDto snapshot, string format, CancellationToken cancellationToken) => ExecuteAsync(async () =>
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (format is not ("html" or "xlsx")) return Failure<ReportExportDto>("SYSTEM_REPORT_FORMAT_INVALID", "报告格式无效。");
        var renderer = new ResearchReportRenderer();
        Directory.CreateDirectory(Resolve(RelativePath.Parse("reports")));
        string path = Resolve(RelativePath.Parse($"reports/{snapshot.SnapshotSha256}.{format}"));
        string temporary = Resolve(RelativePath.Parse($"reports/report-{Guid.NewGuid():N}.tmp"));
        try
        {
            await using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                64 * 1024, FileOptions.Asynchronous))
            {
                if (format == "html")
                {
                    byte[] html = Encoding.UTF8.GetBytes(renderer.RenderHtml(snapshot));
                    if (html.Length > 16 * 1024 * 1024) return Failure<ReportExportDto>("SYSTEM_REPORT_LIMIT_EXCEEDED", "报告超出大小上限。");
                    await output.WriteAsync(html, cancellationToken).ConfigureAwait(false);
                }
                else await renderer.RenderXlsxAsync(snapshot, output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (output.Length > 32 * 1024 * 1024) return Failure<ReportExportDto>("SYSTEM_REPORT_LIMIT_EXCEEDED", "报告超出大小上限。");
                output.Position = 0;
                string hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(output, cancellationToken).ConfigureAwait(false));
                long length = output.Length;
                output.Close();
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, path, true);
                return GatewayResult.Ok(new ReportExportDto($"FluoAxis-{snapshot.ReportId}.{format}", format, hash, length, snapshot.SnapshotSha256));
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }, cancellationToken);

    /// <summary>Copies only a validated managed export; external handles remain in Infrastructure.</summary>
    public Task<GatewayResult<bool>> CopyManagedExportAsync(string relativeEntry, string expectedSha256,
        Stream destination, CancellationToken cancellationToken) => ExecuteAsync(async () =>
    {
        RelativePath entry = RelativePath.Parse(relativeEntry);
        if (!(entry.Value.StartsWith("reports/", StringComparison.Ordinal) || entry.Value.StartsWith("packages/", StringComparison.Ordinal)
            || entry.Value.StartsWith("support/", StringComparison.Ordinal)))
            return Failure<bool>("SYSTEM_EXPORT_RESOURCE_INVALID", "导出文件身份无效。");
        await using FileStream input = new(Resolve(entry), FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        string actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
        if (!string.Equals(actual, expectedSha256, StringComparison.Ordinal))
            return Failure<bool>("SYSTEM_EXPORT_HASH_MISMATCH", "导出文件校验失败，请重新生成。");
        input.Position = 0;
        await input.CopyToAsync(destination, 64 * 1024, cancellationToken).ConfigureAwait(false);
        return GatewayResult.Ok(true);
    }, cancellationToken);
}
