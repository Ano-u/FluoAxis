namespace FluoAxis.App;

using FluoAxis.Contracts;
using FluoAxis.Infrastructure;
using FluoAxis.PlotProtocol;
using Microsoft.UI.Xaml;

public sealed partial class MainWindow
{
    private Guid reportFigurePlotId;
    private Guid reportFigureRunId;
    private int reportFigureRevision;
    private string reportFigureCaption = string.Empty;

    private void OnAttachReportFigureClick(object sender, RoutedEventArgs args)
    {
        if (activePlotIsDemo || !renderedPlotAvailable || IsWorkbenchBusy || viewModel.Run is not { } run || activeFrozenSpec is null) return;
        reportFigurePlotId = activePlotId; reportFigureRunId = run.RunId; reportFigureRevision = run.Revision;
        reportFigureCaption = activeFrozenSpec.Title;
        Export(PlotExportFormat.Png);
        StatusText.Text = "正在冻结当前图形为报告附图…";
    }

    private async Task<bool> TryAttachReportFigureAsync(byte[] bytes)
    {
        if (reportFigurePlotId == Guid.Empty || reportFigurePlotId != activePlotId) return false;
        reportFigurePlotId = Guid.Empty;
        try
        {
            var figure = new ReportPlotFigureDto(reportFigureRunId, reportFigureRevision, activePlotId, reportFigureCaption, Convert.ToBase64String(bytes));
            var result = await workspace.AttachReportPlotFigureAsync(figure, workbenchLifetime.Token);
            if (result.IsSuccess && viewModel.Run?.RunId == figure.RunId)
            {
                ReportPreviewSummary.Text = "报告附图已更新，请重新读取快照确认新的报告身份。";
                ReportDetailsText.Text = "之前的快照未包含当前附图，已停止显示旧的 Report ID 与 hash。";
                DeliveryStatusText.Text = string.Empty;
            }
            StatusText.Text = result.IsSuccess ? "当前科研图形已冻结。到报告页读取快照后可在 HTML / PDF 中输出附图。" : "运行已改变，附图未加入；请重新绘图。";
        }
        catch (ArgumentException) { StatusText.Text = "附图超过 1 MiB 或 PNG 内容不符合报告限制；仍可独立导出当前图形。"; }
        return true;
    }

    private async void OnExportHtmlPlotClick(object sender, RoutedEventArgs args)
    {
        if (!renderedPlotAvailable || activeFrozenSpec is not { } spec || activeFrozenData is not { } data || IsWorkbenchBusy) return;
        activeOperationTask = SaveOfflinePlotAsync();
        await activeOperationTask;

        async Task SaveOfflinePlotAsync()
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(workbenchLifetime.Token);
            activeWorkbenchOperation = cancellation;
            UpdateWorkbenchFromViewModel();
            try
            {
                GatewayResult<bool>? result = await DesktopWorkspace.ExportOfflinePlotAsync(WinRT.Interop.WindowNative.GetWindowHandle(this), spec, data, cancellation.Token);
                if (result is null) StatusText.Text = "已取消离线图形导出。";
                else StatusText.Text = result.IsSuccess ? "已保存离线交互 HTML，包含本页冻结数据与本地绘图资源。" : "离线图形保存失败，请检查目标权限与磁盘空间。";
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { StatusText.Text = "离线图形导出已取消。"; }
            finally { activeWorkbenchOperation = null; UpdateWorkbenchFromViewModel(); }
        }
    }
}
