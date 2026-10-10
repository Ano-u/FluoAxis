namespace FluoAxis.App;

using System.Globalization;
using System.Text;
using FluoAxis.Contracts;
using Microsoft.UI.Xaml;

public sealed partial class MainWindow
{
    private async void OnRefreshDiagnosticsClick(object sender, RoutedEventArgs args) => await TrackDeliveryAsync("diagnostics");
    private async void OnSupportBundleClick(object sender, RoutedEventArgs args) => await TrackDeliveryAsync("support");

    private async Task RenderDiagnosticsAsync(CancellationToken cancellationToken)
    {
        GatewayResult<WorkspaceDiagnosticsDto> result = await workspace.GetDiagnosticsAsync(cancellationToken);
        if (!ShowDeliveryFailure(result)) return;
        WorkspaceDiagnosticsDto data = result.Value!;
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"FluoAxis {data.SoftwareVersion} · .NET {data.FrameworkVersion}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Windows {data.OperatingSystemVersion} · {data.Architecture} · {data.LogicalProcessors} 个逻辑处理器");
        text.AppendLine(CultureInfo.InvariantCulture, $"当前托管内存：{data.ManagedMemoryBytes / 1048576.0:F1} MiB（不含 WebView2 子进程）");
        text.AppendLine(CultureInfo.InvariantCulture, $"首批目录记录：{data.CatalogPageCount}{(data.MoreProjectsAvailable ? "，还有更多项目" : string.Empty)}");
        text.AppendLine(data.RecoveredUncleanSession ? "恢复：检测到上次会话未正常关闭；SQLite 完整性检查通过，已恢复已提交事务。" : "恢复：本次启动未检测到未关闭会话。");
        text.AppendLine("\n近期技术事件（UTC）");
        foreach (DiagnosticEventDto item in data.RecentEvents)
            text.AppendLine(CultureInfo.InvariantCulture, $"{item.OccurredUtc:O}  {item.Code}  {item.Category}  {item.CorrelationId:D}");
        PlaceholderText.Text = text.ToString();
        OperationText.Text = "已读取受限的运行诊断。";
    }
}
