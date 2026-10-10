#if DEBUG
namespace FluoAxis.App;

using FluoAxis.Infrastructure;
using Microsoft.UI.Xaml;
using System.Diagnostics;

public sealed partial class MainWindow
{
    internal async Task CaptureVisualReviewAsync(string relativeOutputDirectory)
    {
        var measurements = new Dictionary<string, double>(StringComparer.Ordinal);
        try
        {
            await initializationTask.WaitAsync(TimeSpan.FromSeconds(30));
            await windowActivated.Task.WaitAsync(TimeSpan.FromSeconds(30));
            double scale = RootSurface.XamlRoot.RasterizationScale;
            AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(1280 * scale), (int)(880 * scale)));
            RootSurface.RequestedTheme = ElementTheme.Light;
            ShowArea("workbench");
            activeStage = "results";
            UpdateStageSurface();
            await CaptureAsync("results-light.png");
            activeStage = "mapping";
            UpdateStageSurface();
            await CaptureAsync("mapping-light.png");
            activeStage = "plate";
            UpdateStageSurface();
            await CaptureAsync("plate-light.png");
            // Native capture deliberately excludes WebView pixels. Inspect the
            // actual Explorer control layout without initializing a renderer.
            WorkbenchSurface.Visibility = Visibility.Collapsed;
            ExplorerSurface.Visibility = Visibility.Visible;
            PlotFallback.Text = "原生控件排版检查 · WebView2 图形需单独验证";
            await CaptureAsync("explorer-controls-light.png");
            ExplorerSettingsFlyout.ShowAt(ExplorerSettingsButton);
            await CaptureAsync("explorer-tools-light.png");
            ExplorerSettingsFlyout.Hide();
            ShowArea("reports");
            await CaptureAsync("reports-light.png");
            RootSurface.RequestedTheme = ElementTheme.Dark;
            ShowArea("workbench");
            activeStage = "results";
            UpdateStageSurface();
            await CaptureAsync("results-dark.png");
            AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(820 * scale), (int)(880 * scale)));
            RootSurface.RequestedTheme = ElementTheme.Light;
            await CaptureAsync("results-narrow.png");
            ProjectToolsExpander.IsExpanded = true;
            await CaptureAsync("project-tools-narrow.png");
            ProjectToolsExpander.IsExpanded = false;
            ShowArea("reports");
            await CaptureAsync("reports-narrow.png");
            await NativeVisualEvidence.SaveMeasurementsAsync(relativeOutputDirectory, measurements, scale);
            await NativeVisualEvidence.CompleteAsync(relativeOutputDirectory, "PASS: native XAML views captured; WebView, High Contrast and interactive workflow not covered.");
        }
        catch (Exception)
        {
            await NativeVisualEvidence.CompleteAsync(relativeOutputDirectory, "UI_LAYOUT_RENDER_FAILED");
        }
        finally
        {
            await workspace.CloseAsync();
            closeAllowed = true;
            Close();
        }

        async Task CaptureAsync(string fileName)
        {
            RootSurface.UpdateLayout();
            await Task.Delay(180);
            RootSurface.UpdateLayout();
            var timer = Stopwatch.StartNew();
            await NativeVisualEvidence.CaptureAsync(RootSurface, relativeOutputDirectory, fileName);
            measurements[fileName] = timer.Elapsed.TotalMilliseconds;
        }
    }
}
#endif
