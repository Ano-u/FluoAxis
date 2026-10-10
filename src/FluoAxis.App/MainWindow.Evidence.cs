namespace FluoAxis.App;

using System.Globalization;
using System.Text;
using FluoAxis.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

public sealed partial class MainWindow
{
    private int scientificPointOffset;
    private int scientificTotalPoints;

    private void BindScientificEvidence(RunEvidenceDto? evidence)
    {
        ScientificWellChooser.Items.Clear();
        ScientificSummaryText.Text = "此 Run 尚未保存完整阶段来源。旧记录保留缺项，不补算。";
        ScientificPointsText.Text = "选择已保存的阶段孔位后，可逐页读取实际计算点。";
        scientificPointOffset = 0;
        scientificTotalPoints = 0;
        foreach (AnalysisStageEvidenceDto stage in evidence?.ScientificEvidence?.Stages ?? [])
            foreach (WellScientificEvidenceDto well in stage.Wells)
                ScientificWellChooser.Items.Add(new ComboBoxItem { Content = $"{ScientificStageLabel(stage.Stage)} · {well.Well} · {well.Channel.ToUpperInvariant()} · {StatusLabel(well.Status)}", Tag = new ScientificSelection(stage, well) });
        if (ScientificWellChooser.Items.Count > 0) ScientificWellChooser.SelectedIndex = 0;
    }

    private void OnScientificWellSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        scientificPointOffset = 0;
        scientificTotalPoints = 0;
        ScientificPointsText.Text = "每页最多 250 个已保存计算点；界面不重新计算。";
        if (ScientificWellChooser.SelectedItem is not ComboBoxItem { Tag: ScientificSelection choice }) return;
        WellScientificEvidenceDto well = choice.Well;
        AnalysisInputIdentityDto identity = choice.Stage.Identity;
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"{ScientificStageLabel(choice.Stage.Stage)} · {well.Well} / {well.Channel.ToUpperInvariant()} · {well.Role} · {StatusLabel(well.Status)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"来源完整性：{choice.Stage.ProvenanceCompleteness}（工程标记，不代表科学批准）");
        text.AppendLine(CultureInfo.InvariantCulture, $"输入 revision：{identity.InputRevision} · 算法：{identity.Algorithm}");
        text.AppendLine(CultureInfo.InvariantCulture, $"输入 SHA-256：{identity.InputSnapshotSha256}\n映射 SHA-256：{identity.MappingSha256}\n源文件 SHA-256：{identity.SourceSha256}");
        if (choice.Stage.EvaluatedPointsSha256 is { } pointHash) text.AppendLine(CultureInfo.InvariantCulture, $"实际计算点 SHA-256：{pointHash}");
        SourcePointEvidenceDto point = well.Endpoint;
        text.AppendLine(CultureInfo.InvariantCulture, $"端点：{point.SheetName} · 源行列（0起） {point.SourceRowIndex}/{point.SourceColumnIndex} · 点 {point.PointIndex} · ticks {point.ElapsedTicks} · Raw RFU {FullPrecision(point.RawRfu)}");
        if (well.Window is { } window) text.AppendLine(CultureInfo.InvariantCulture, $"计算窗口：{FullPrecision(window.StartMinutes)}–{FullPrecision(window.EndMinutes)} min · 实际点数 {window.PointCount} · 序号 {window.FirstPointIndex}–{window.LastPointIndex}");
        foreach (TraceValueDto value in well.Values) text.AppendLine(CultureInfo.InvariantCulture, $"{value.Name} = {FullPrecision(value.Value)}");
        if (well.ErrorCodes.Count > 0) text.AppendLine(CultureInfo.InvariantCulture, $"问题：{string.Join("、", well.ErrorCodes)}");
        ScientificSummaryText.Text = text.ToString();
        if (well.Window is null) ScientificPointsText.Text = "此阶段只使用端点，实际双通道源值已在上方摘要和 trace 中显示。";
        UpdateScientificPointActions();
    }

    private async void OnReadScientificPointsClick(object sender, RoutedEventArgs args) => await StartScientificReadAsync(0);
    private async void OnPreviousScientificPointsClick(object sender, RoutedEventArgs args) => await StartScientificReadAsync(Math.Max(0, scientificPointOffset - 250));
    private async void OnNextScientificPointsClick(object sender, RoutedEventArgs args)
    {
        if (scientificPointOffset + 250 < scientificTotalPoints) await StartScientificReadAsync(scientificPointOffset + 250);
    }

    private async Task StartScientificReadAsync(int offset)
    {
        if (IsWorkbenchBusy || viewModel.Run is not { } run || ScientificWellChooser.SelectedItem is not ComboBoxItem { Tag: ScientificSelection choice } || choice.Well.Window is null) return;
        activeOperationTask = ReadAsync();
        await activeOperationTask;
        async Task ReadAsync()
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(workbenchLifetime.Token);
            activeWorkbenchOperation = cancellation;
            UpdateWorkbenchFromViewModel();
            try
            {
                var response = await workspace.GetScientificPointPageAsync(run.RunId, choice.Stage.Stage, choice.Well.Well, choice.Well.Channel, offset, 250, cancellation.Token);
                if (viewModel.Run?.RunId != run.RunId || viewModel.Run?.Revision != run.Revision
                    || (ScientificWellChooser.SelectedItem as ComboBoxItem)?.Tag is not ScientificSelection current || current != choice) return;
                if (!ShowDeliveryFailure(response)) return;
                ScientificPointPageDto page = response.Value!;
                if (page.Revision != run.Revision) { ScientificPointsText.Text = "Run 已更新，请刷新阶段证据。"; return; }
                scientificPointOffset = page.Offset;
                scientificTotalPoints = page.TotalPoints;
                var text = new StringBuilder();
                text.AppendLine(CultureInfo.InvariantCulture, $"偏移 {page.Offset} · 本页 {page.Points.Count} · 共 {page.TotalPoints} 点\n点序号 | elapsed ticks | Raw RFU | 已保存 Y | 源行 | 源列（0起） | 已保存 NTC QC");
                foreach (ScientificPointDto point in page.Points)
                    text.AppendLine(CultureInfo.InvariantCulture, $"{point.PointIndex} | {point.ElapsedTicks} | {FullPrecision(point.RawRfu)} | {(point.Normalized is { } y ? FullPrecision(y) : "—")} | {point.SourceRowIndex} | {point.SourceColumnIndex} | {(point.WithinBand is { } passed ? passed ? "Pass" : "Fail" : "—")}");
                ScientificPointsText.Text = text.ToString();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { ScientificPointsText.Text = "读取已取消。"; }
            catch (Exception) { ShowWorkbenchError("SYSTEM_EVIDENCE_READ_FAILED", "无法读取已保存的计算点，请重新打开当前项目。" ); }
            finally { activeWorkbenchOperation = null; UpdateWorkbenchFromViewModel(); }
        }
    }

    private static string ScientificStageLabel(string stage) => stage switch { "calibration" => "校准", "control" => "对照质控", "sample" => "样本分析", _ => stage };
    private void UpdateScientificPointActions()
    {
        bool available = !IsWorkbenchBusy && ScientificWellChooser.SelectedItem is ComboBoxItem { Tag: ScientificSelection { Well.Window: not null } };
        ReadScientificPointsButton.IsEnabled = available;
        PreviousScientificPointsButton.IsEnabled = available && scientificPointOffset > 0;
        NextScientificPointsButton.IsEnabled = available && scientificPointOffset + 250 < scientificTotalPoints;
    }
    private sealed record ScientificSelection(AnalysisStageEvidenceDto Stage, WellScientificEvidenceDto Well);
}
