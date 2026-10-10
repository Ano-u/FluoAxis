namespace FluoAxis.App;

using System.Text.Json;
using FluoAxis.PlotProtocol;
using Microsoft.UI.Xaml.Controls;

public sealed partial class MainWindow
{
    private bool synchronizingExplorerSelection;
    private readonly HashSet<Guid> selectionRequests = [];

    private void SelectExplorerRows(IReadOnlyList<string> pointIds)
    {
        if (explorerData is not { } model || ExplorerTable.ItemsSource is not ExplorerRows items) return;
        var requested = pointIds.Select(id => id.EndsWith("-s2", StringComparison.Ordinal) ? id[..^3] : id).ToHashSet(StringComparer.Ordinal);
        int[] visible = Enumerable.Range(0, explorerRows.Length).Where(index => requested.Contains(model.PointId(explorerRows[index]))).ToArray();
        if (ExplorerTable.SelectedItems.Cast<ExplorerGridRow>().Select(row => row.SourceIndex).ToHashSet()
            .SetEquals(visible.Select(index => explorerRows[index]))) return;
        synchronizingExplorerSelection = true;
        try
        {
            ExplorerTable.SelectedItems.Clear();
            foreach (int index in visible) ExplorerTable.SelectedItems.Add(items[index]);
            if (visible.Length == 1) ExplorerTable.ScrollIntoView(items[visible[0]], ScrollIntoViewAlignment.Leading);
        }
        finally { synchronizingExplorerSelection = false; }
    }

    private void SendExplorerSelection(string[] pointIds)
    {
        if (!plotReady || !renderedPlotAvailable || activePlotId == Guid.Empty) return;
        if (pointIds.Length > PlotMessageCodec.MaximumPointIds || selectionRequests.Count >= 64)
        {
            RawPageText.Text = "本次选择超出图形联动预算，请缩小选择或等待图形完成响应。表格选择保持不变。";
            return;
        }
        Guid requestId = Post(PlotMessageTypes.SetSelection, JsonSerializer.SerializeToElement(
            new PlotSelectionPayload { PointIds = pointIds }, PlotProtocolJsonContext.Default.PlotSelectionPayload));
        if (requestId != Guid.Empty) selectionRequests.Add(requestId);
    }

    private void SendSelectedRowsToPlot()
    {
        if (explorerData is not { } data || activeFrozenData is not { } frozen) return;
        var rows = ExplorerTable.SelectedItems.Cast<ExplorerGridRow>().Select(row => data.PointId(row.SourceIndex)).ToHashSet(StringComparer.Ordinal);
        // The snapshot contains at most 50,000 points. Preserve actual plot IDs,
        // including the second series, instead of using displayed row indices.
        string[] ids = frozen.Points.Where(point => rows.Contains(point.PointId.EndsWith("-s2", StringComparison.Ordinal) ? point.PointId[..^3] : point.PointId))
            .Select(point => point.PointId).ToArray();
        SendExplorerSelection(ids);
    }
}
