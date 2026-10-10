namespace FluoAxis.App;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

public sealed partial class MainWindow
{
    private bool themeRefreshQueued;
    private long primaryPaletteCallback;
    private long surfacePaletteCallback;
    private bool explorerFocused;

    private void UpdateExplorerLayout()
    {
        ExplorerTitleBar.Visibility = explorerFocused ? Visibility.Collapsed : Visibility.Visible;
        ExplorerSourceBar.Visibility = explorerFocused ? Visibility.Collapsed : Visibility.Visible;
        ExplorerFocusButton.Content = explorerFocused ? "还原布局" : "放大图形";
        // Flyouts overlay the workspace, so opening research tools never changes
        // the chart's viewport or pushes its axes outside the window.
        double width = Math.Max(280, RootSurface.ActualWidth - 110);
        double height = Math.Max(240, RootSurface.ActualHeight - 160);
        ExplorerSettingsPanel.Width = Math.Min(980, width);
        ExplorerSettingsPanel.Height = Math.Min(620, height);
        ExplorerTablePanel.Width = Math.Min(900, width);
        ExplorerTablePanel.MaxHeight = Math.Min(420, height);
    }

    private void OnExplorerFocusClick(object sender, RoutedEventArgs args)
    {
        explorerFocused = !explorerFocused;
        ExplorerSettingsFlyout.Hide();
        ExplorerTableFlyout.Hide();
        UpdateExplorerLayout();
    }

    private void OnCloseExplorerSettingsClick(object sender, RoutedEventArgs args) => ExplorerSettingsFlyout.Hide();

    private void OnExplorerViewportSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (uiInitialized) UpdateExplorerLayout();
    }

    private Brush WorkbenchBrush(string name) => name switch
    {
        "Surface" => PaletteSurface.Background, "SubtleSurface" => PaletteSubtle.Background,
        "Border" => PaletteBorder.Background, "PrimaryText" => PalettePrimary.Background,
        "SecondaryText" => PaletteSecondary.Background, "Accent" => PaletteAccent.Background,
        "AccentSoft" => PaletteAccentSoft.Background, "Success" => PaletteSuccess.Background,
        "Caution" => PaletteCaution.Background, "Danger" => PaletteDanger.Background,
        "SelectionBorder" => PaletteSelection.Background, _ => throw new ArgumentException("Unknown appearance token.", nameof(name)),
    };

    private void OnWorkbenchThemeChanged(FrameworkElement sender, object args)
    {
        if (!uiInitialized || disposed || themeRefreshQueued) return;
        // XAML ThemeResource values refresh automatically; generated native
        // stage/table elements must be rebuilt from the active theme too.
        themeRefreshQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            themeRefreshQueued = false;
            if (disposed) return;
            renderedStage = string.Empty;
            UpdateWorkbenchFromViewModel();
            RenderExplorerTable();
        })) themeRefreshQueued = false;
    }

    private static string LqcDirectionLabel(string? direction) => direction switch
    {
        FluoAxis.Contracts.CallNames.Positive => "Positive · 一致方向",
        FluoAxis.Contracts.CallNames.Negative => "Negative · 一致方向",
        _ => "未提供一致方向",
    };
}
