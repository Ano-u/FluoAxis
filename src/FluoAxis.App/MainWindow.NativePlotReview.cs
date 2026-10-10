#if DEBUG
namespace FluoAxis.App;

using System.Text.Json;
using FluoAxis.Infrastructure;
using Microsoft.UI.Xaml;
using System.Globalization;
using FluoAxis.PlotProtocol;

public sealed partial class MainWindow
{
    internal async Task VerifyNativePlotAsync(string sourceEntry, string outputEntry)
    {
        string phase = "startup";
        try
        {
            await initializationTask.WaitAsync(TimeSpan.FromSeconds(30));
            await windowActivated.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var source = await workspace.StageValidationSourceAsync(sourceEntry);
            if (!source.IsSuccess) throw new InvalidOperationException("Source unavailable.");
            await StartSmartImportAsync(true, reusedSource: source.Value!);
            viewModel.SelectArea("explorer");
            PrimaryNavigation.SelectedItem = ExplorerNavigationItem;
            ShowArea("explorer");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            while (!plotReady || !renderedPlotAvailable) await Task.Delay(100, deadline.Token);
            var measurements = new List<string>();
            foreach ((string name, int width, int height, PlotKind kind, bool focus) in new[] {
                ("2d-wide", 1280, 980, PlotKind.ScatterLine2D, false), ("2d-short", 960, 720, PlotKind.ScatterLine2D, false),
                ("3d-short", 960, 720, PlotKind.Scatter3D, false), ("3d-focus", 960, 720, PlotKind.Scatter3D, true),
                ("3d-restored", 960, 720, PlotKind.Scatter3D, false), ("3d-wide", 1280, 980, PlotKind.Scatter3D, false) })
            {
                phase = name;
                explorerFocused = focus;
                UpdateExplorerLayout();
                double scale = RootSurface.XamlRoot.RasterizationScale;
                AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(width * scale), (int)(height * scale)));
                SetExplorerPlotKind(kind);
                while (!renderedPlotAvailable) await Task.Delay(50, deadline.Token);
                RootSurface.UpdateLayout();
                await Task.Delay(700, deadline.Token);
                var top = PlotWebView.TransformToVisual(RootSurface).TransformPoint(new Windows.Foundation.Point(0, 0));
                if (PlotWebView.ActualHeight < 440 || top.Y < 0 || top.Y + PlotWebView.ActualHeight > RootSurface.ActualHeight - 8)
                    throw new InvalidOperationException("Plot is clipped or does not use the visible window.");
                measurements.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1:F0}x{2:F0} DIP, top={3:F0}, rootHeight={4:F0}", name, PlotWebView.ActualWidth, PlotWebView.ActualHeight, top.Y, RootSurface.ActualHeight));
                string json = await PlotWebView.CoreWebView2.ExecuteScriptAsync("(()=>{const p=document.getElementById('plot'),b=p.getBoundingClientRect();return {height:p.clientHeight,canvas:p._fullLayout._size.h,ytitle:document.querySelector('.ytitle')?.textContent,ticks:document.querySelectorAll('.ytick').length,modebar:getComputedStyle(document.querySelector('.modebar')).position,axes:[...document.querySelectorAll('.xtitle,.ytitle,.ztitle')].map(e=>{const r=e.getBoundingClientRect();return {text:e.textContent,left:r.left-b.left,top:r.top-b.top,right:r.right-b.left,bottom:r.bottom-b.top}})};})()");
                using JsonDocument geometry = JsonDocument.Parse(json);
                if (geometry.RootElement.GetProperty("canvas").GetDouble() < 200
                    || kind != PlotKind.Scatter3D && geometry.RootElement.GetProperty("ticks").GetInt32() < 3
                    || geometry.RootElement.GetProperty("modebar").GetString() != "absolute")
                    throw new InvalidOperationException("Browser plot geometry failed.");
                await NativeVisualEvidence.CaptureBrowserAsync(PlotWebView.CoreWebView2, outputEntry, name + ".png");
                await NativeVisualEvidence.CaptureAsync(RootSurface, outputEntry, name + "-layout.png");
            }
            phase = "settings-overlay";
            double plotHeight = PlotWebView.ActualHeight;
            ExplorerSettingsFlyout.ShowAt(ExplorerSettingsButton);
            await Task.Delay(350, deadline.Token);
            if (Math.Abs(PlotWebView.ActualHeight - plotHeight) > 1) throw new InvalidOperationException("Settings compressed the canvas.");
            await NativeVisualEvidence.CaptureAsync(ExplorerSettingsPanel, outputEntry, "settings-panel.png");
            ExplorerSettingsFlyout.Hide();
            phase = "selection-setup";
            await ValidateAndApplyDefinitionAsync(CopyDefinition(explorerDefinition!, kind: "ScatterLine2D", secondary: "rox", replaceSeries: true,
                filter: "time", low: 90, sort: "time", descending: true, replaceFilter: true));
            while (!renderedPlotAvailable) await Task.Delay(50, deadline.Token);
            if (!explorerRows.SequenceEqual(new[] { 2, 1 })) throw new InvalidOperationException("Filtered order mismatch.");
            phase = "plot-to-table";
            string coordinates = await PlotWebView.CoreWebView2.ExecuteScriptAsync("(()=>{const b=document.querySelectorAll('.scatterlayer .trace')[1].querySelector('.points path').getBoundingClientRect();return {x:b.x+b.width/2,y:b.y+b.height/2};})()");
            using (JsonDocument point = JsonDocument.Parse(coordinates))
            {
                string position = string.Format(CultureInfo.InvariantCulture, "\"x\":{0},\"y\":{1},\"button\":\"left\",\"clickCount\":1", point.RootElement.GetProperty("x").GetDouble(), point.RootElement.GetProperty("y").GetDouble());
                await PlotWebView.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", "{\"type\":\"mousePressed\"," + position + "}");
                await PlotWebView.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", "{\"type\":\"mouseReleased\"," + position + "}");
            }
            while (ExplorerTable.SelectedItem is not ExplorerGridRow { SourceIndex: 2 }) await Task.Delay(50, deadline.Token);
            phase = "table-to-plot";
            ExplorerTableFlyout.ShowAt(ExplorerTableButton);
            await Task.Delay(250, deadline.Token);
            if (Math.Abs(PlotWebView.ActualHeight - plotHeight) > 1) throw new InvalidOperationException("Table compressed the canvas.");
            ExplorerTable.SelectedIndex = 1;
            while (await PlotWebView.CoreWebView2.ExecuteScriptAsync("JSON.stringify(document.getElementById('plot').data.map(t=>t.selectedpoints))==='[[1],[1]]'") != "true")
                await Task.Delay(50, deadline.Token);
            while (selectionRequests.Count > 0) await Task.Delay(50, deadline.Token);
            if (ExplorerTable.SelectedItem is not ExplorerGridRow { SourceIndex: 1 }) throw new InvalidOperationException("Selection acknowledgement overwrote the latest row.");
            phase = "clear-selection";
            ExplorerTable.SelectedItems.Clear();
            while (selectionRequests.Count > 0) await Task.Delay(50, deadline.Token);
            if (await PlotWebView.CoreWebView2.ExecuteScriptAsync("document.getElementById('plot').data.every(t=>t.selectedpoints==null)") != "true") throw new InvalidOperationException("Cleared selection still dims the plot.");
            ExplorerTableFlyout.Hide();
            explorerViewDirty = false;
            await NativeVisualEvidence.CompleteAsync(outputEntry, "PASS: 2D/3D inside actual window bounds; focus/restore; settings/table overlays preserve canvas; browser mouse selects the filtered/sorted secondary row; table highlights both series; clear restores points.\n" + string.Join('\n', measurements));
        }
        catch (Exception)
        {
            Environment.ExitCode = 1;
            await NativeVisualEvidence.CompleteAsync(outputEntry, "NATIVE_PLOT_REVIEW_FAILED: " + phase);
        }
        finally { await workspace.CloseAsync(); closeAllowed = true; Close(); }
    }
}
#endif
