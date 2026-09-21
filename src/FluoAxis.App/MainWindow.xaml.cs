namespace FluoAxis.App;

using System.Text.Json;
using System.Diagnostics;
using FluoAxis.PlotProtocol;
using FluoAxis.Presentation;
using FluoAxis.Visualization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

public sealed partial class MainWindow : Window, IDisposable
{
    private PlotHostWebViewAdapter? plotHost;
    private readonly WorkbenchViewModel viewModel;
    private readonly PlotResponseGate responseGate = new();
    private Guid activePlotId;
    private long generation;
    private bool plotReady;
    private PlotKind activeKind = PlotKind.Scatter3D;
    private readonly PlotKind initialKind;
    private readonly int fixturePointCount;
    private readonly Stopwatch renderTimer = new();
    private readonly TaskCompletionSource windowActivated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? renderPreparation;
    private readonly CancellationTokenSource workbenchLifetime = new();

    public MainWindow(WorkbenchViewModel viewModel, string? launchArguments = null)
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        string? benchmarkMode = Environment.GetEnvironmentVariable("FLUOAXIS_SPIKE_BENCHMARK");
        bool benchmark2D = string.Equals(benchmarkMode, "2d", StringComparison.OrdinalIgnoreCase)
            || launchArguments?.Contains("--benchmark-25k-2d", StringComparison.Ordinal) == true;
        bool benchmark3D = string.Equals(benchmarkMode, "3d", StringComparison.OrdinalIgnoreCase)
            || launchArguments?.Contains("--benchmark-25k-3d", StringComparison.Ordinal) == true;
        fixturePointCount = benchmark2D || benchmark3D ? 25_000 : 6;
        initialKind = benchmark2D ? PlotKind.Scatter2D : PlotKind.Scatter3D;
        InitializeComponent();
        Activated += OnActivated;
        Closed += OnClosed;
        if (benchmark2D || benchmark3D)
        {
            viewModel.SelectArea("explorer");
            PrimaryNavigation.SelectedItem = ExplorerNavigationItem;
            ShowArea("explorer");
        }

        _ = InitializeWorkbenchAsync();
    }

    private async Task InitializeWorkbenchAsync()
    {
        try
        {
            await viewModel.InitializeAsync("Research workspace", workbenchLifetime.Token);
            if (!DispatcherQueue.TryEnqueue(UpdateWorkbenchFromViewModel))
            {
                ShowWorkbenchError("SYSTEM_UI_DISPATCH_UNAVAILABLE", "The workbench could not publish its current state.");
            }
        }
        catch (OperationCanceledException) when (workbenchLifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            ShowWorkbenchError("SYSTEM_WORKBENCH_INITIALIZATION_FAILED", "The workbench could not be initialized.");
        }
    }

    private void UpdateWorkbenchFromViewModel()
    {
        ProjectNameText.Text = viewModel.ProjectName;
        ProjectTab.Header = viewModel.ProjectName;
        RunStateText.Text = viewModel.RunState;
        MethodText.Text = viewModel.MethodIdentity;
        WorkbenchProgress.IsActive = viewModel.IsBusy;
        WorkbenchProgress.Visibility = viewModel.IsBusy ? Visibility.Visible : Visibility.Collapsed;

        if (viewModel.HasError)
        {
            GlobalInfoBar.Title = viewModel.ErrorCode ?? "SYSTEM_ERROR";
            GlobalInfoBar.Message = BuildErrorMessage();
            GlobalInfoBar.IsOpen = true;
        }

        ImportStageButton.IsEnabled = true;
        MappingStageButton.IsEnabled = true;
        PlateStageButton.IsEnabled = true;
        CalibrationStageButton.IsEnabled = true;
        ControlStageButton.IsEnabled = true;
        AnalysisStageButton.IsEnabled = true;
        ResultsStageButton.IsEnabled = true;
        UpdateStageSurface("import");
    }

    private string BuildErrorMessage()
    {
        string message = viewModel.ErrorMessage ?? "The operation failed.";
        if (!string.IsNullOrWhiteSpace(viewModel.RecommendedAction))
        {
            message = $"{message} {viewModel.RecommendedAction}";
        }

        return viewModel.CorrelationId is { } correlation
            ? $"{message} Correlation: {correlation}"
            : message;
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        string area = args.IsSettingsSelected
            ? "diagnostics"
            : (args.SelectedItemContainer?.Tag as string ?? "workbench");
        viewModel.SelectArea(area);
        ShowArea(area);
    }

    private void ShowArea(string area)
    {
        WorkbenchSurface.Visibility = area == "workbench" ? Visibility.Visible : Visibility.Collapsed;
        ExplorerSurface.Visibility = area == "explorer" ? Visibility.Visible : Visibility.Collapsed;
        PlaceholderSurface.Visibility = area is "reports" or "diagnostics" ? Visibility.Visible : Visibility.Collapsed;

        if (area == "reports")
        {
            PlaceholderTitle.Text = "Reports / Export";
            PlaceholderText.Text = "Immutable report snapshots and PDF/Excel export are not implemented in the current verified slice.";
        }
        else if (area == "diagnostics")
        {
            PlaceholderTitle.Text = "Diagnostics / Settings";
            PlaceholderText.Text = "Safe diagnostic events exist; the diagnostics page, persistent sink, retention, and support bundle remain pending.";
        }
        else if (area == "explorer" && plotHost is null)
        {
            _ = InitializePlotHostAsync();
        }
    }

    private void OnStageClick(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: string stage }) UpdateStageSurface(stage);
    }

    private void UpdateStageSurface(string stage)
    {
        (StageTitle.Text, StageSubtitle.Text) = stage switch
        {
            "import" => ("Import Preview", "Inspect a managed fluorescence data source without changing the Run."),
            "mapping" => ("Data Mapping", "Review candidate FAM and ROX regions. Confirmation is not yet available."),
            "plate" => ("Plate Map", "Assign logical well roles after a mapping has been confirmed."),
            "calibration" => ("Calibration", "Calibration requires an immutable prepared signal snapshot."),
            "control" => ("Control QC", "Control QC requires persisted calibration inputs."),
            "analysis" => ("Sample Analysis", "Sample analysis requires a Run ready for analysis."),
            "results" => ("Results / Export", "Formal results and immutable export snapshots are not available yet."),
            _ => ("Workbench", "Select a guided analysis stage."),
        };

        GuidedStageItem? definition = viewModel.GuidedStages.FirstOrDefault(item => item.Key == stage);
        bool backendAllowsStage = definition is not null && viewModel.IsStageAvailable(definition);
        PrimaryStageAction.Content = stage == "import" ? "Inspect source" : "Continue";
        // File-picker and mutation orchestration are intentionally absent from this slice.
        PrimaryStageAction.IsEnabled = false;
        StageEmptyState.Opacity = backendAllowsStage ? 1 : 0.72;
    }

    private void ShowWorkbenchError(string code, string message)
    {
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            GlobalInfoBar.Title = code;
            GlobalInfoBar.Message = message;
            GlobalInfoBar.IsOpen = true;
            WorkbenchProgress.IsActive = false;
            WorkbenchProgress.Visibility = Visibility.Collapsed;
        }))
        {
            Title = $"FluoAxis - {code}";
        }
    }

    private void OnTrustedMessageReceived(object? sender, TrustedPlotMessageEventArgs args)
    {
        if (args.Message.Payload is PlotReadyPayload ready)
        {
            if (ready.ProtocolVersion != PlotProtocolVersions.Current
                || !string.Equals(ready.PlotlyVersion, "4.0.0", StringComparison.Ordinal)
                || !ready.SupportsToImage)
            {
                ShowPlotFallback("PLOT_CAPABILITY_MISMATCH");
                return;
            }

            plotReady = true;
            if (!DispatcherQueue.TryEnqueue(async () => await RenderAfterActivationAsync(initialKind)))
            {
                ShowPlotFallback("PLOT_DISPATCH_UNAVAILABLE");
            }
            return;
        }

        if (!responseGate.TryAccept(args.Message.Envelope))
        {
            return;
        }

        if (args.Message.Envelope.Type == PlotMessageTypes.Rendered)
        {
            renderTimer.Stop();
            Title = fixturePointCount == 25_000
                ? $"FluoAxis - {fixturePointCount} points rendered in {renderTimer.ElapsedMilliseconds} ms"
                : "FluoAxis - Plot rendered";
            StatusText.Text = $"{activeKind} rendered ({fixturePointCount} points, {renderTimer.ElapsedMilliseconds} ms)";
            return;
        }

        if (args.Message.Payload is PlotExportCompletedPayload completed)
        {
            _ = SaveSpikeExportAsync(completed);
            return;
        }

        if (args.Message.Payload is PlotPointPayload point)
        {
            StatusText.Text = $"Selected PointId: {point.PointId}";
            return;
        }

        if (args.Message.Payload is PlotCameraPayload camera)
        {
            StatusText.Text = $"Camera: {camera.Camera.EyeX:F2}, {camera.Camera.EyeY:F2}, {camera.Camera.EyeZ:F2}";
            return;
        }

        if (args.Message.Payload is PlotErrorPayload error)
        {
            Title = $"FluoAxis - {error.Code}";
            return;
        }

    }

    private async Task RenderAfterActivationAsync(PlotKind kind)
    {
        await windowActivated.Task;
        await RenderAsync(kind);
    }

    private async Task RenderAsync(PlotKind kind)
    {
        if (!plotReady) return;
        renderPreparation?.Cancel();
        renderPreparation?.Dispose();
        renderPreparation = new CancellationTokenSource();
        CancellationToken cancellationToken = renderPreparation.Token;

        activeKind = kind;
        activePlotId = Guid.NewGuid();
        generation++;
        responseGate.Activate(activePlotId, generation);
        bool is3D = kind is PlotKind.Scatter3D or PlotKind.Line3D;
        Guid plotId = activePlotId;
        long renderGeneration = generation;
        StatusText.Text = $"Preparing {kind} ({fixturePointCount} points)...";
        await Task.Yield();

        PreparedRender prepared;
        try
        {
            prepared = await Task.Run(
                () => PrepareRender(plotId, renderGeneration, kind, is3D, fixturePointCount, cancellationToken),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            ShowPlotFallback("PLOT_RENDER_PREPARATION_FAILED");
            return;
        }

        if (cancellationToken.IsCancellationRequested || plotId != activePlotId || renderGeneration != generation) return;
        responseGate.RegisterCommand(plotId, renderGeneration, prepared.RequestId, PlotMessageTypes.Render);
        renderTimer.Restart();
        plotHost?.PostJson(prepared.Json);
        ExportSvgButton.IsEnabled = !is3D;
        StatusText.Text = $"Rendering {kind}...";
    }

    private static PreparedRender PrepareRender(
        Guid plotId,
        long renderGeneration,
        PlotKind kind,
        bool is3D,
        int pointCount,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var spec = new PlotSpec(
            plotId,
            kind,
            new AxisSpec("FAM"),
            new AxisSpec("ROX"),
            is3D ? new AxisSpec("Elapsed time") : null,
            [new SeriesSpec("research-series", "Research series", markerSize: 8)],
            is3D ? new CameraSpec(1.35, 1.35, 1.15) : null,
            showLegend: true,
            title: $"FluoAxis {kind} visualization spike");
        IReadOnlyList<PlotDataPoint> points = pointCount == 25_000
            ? BuildBenchmarkPoints(is3D, cancellationToken)
            :
            [
                new PlotDataPoint("p-1", 0.03, 0.02, is3D ? 0 : null, "research-series"),
                new PlotDataPoint("p-2", 0.12, 0.08, is3D ? 20 : null, "research-series"),
                new PlotDataPoint("p-3", 0.27, 0.18, is3D ? 40 : null, "research-series"),
                new PlotDataPoint("p-4", 0.46, 0.31, is3D ? 60 : null, "research-series"),
                new PlotDataPoint("p-5", 0.68, 0.49, is3D ? 90 : null, "research-series"),
                new PlotDataPoint("p-6", 0.91, 0.76, is3D ? 120 : null, "research-series"),
            ];
        var data = new PlotDataSnapshot(
            plotId,
            is3D ? PlotDimensions.Three : PlotDimensions.Two,
            pointLimit: pointCount,
            points);
        Guid requestId = Guid.NewGuid();
        PlotMessageEnvelope message = new()
        {
            SchemaVersion = PlotMessageCodec.CurrentSchemaVersion,
            Type = PlotMessageTypes.Render,
            RequestId = requestId,
            PlotId = plotId,
            Generation = renderGeneration,
            Payload = JsonSerializer.SerializeToElement(
                new PlotRenderPayload { Spec = spec, Data = data, DataRevision = 1 },
                PlotProtocolJsonContext.Default.PlotRenderPayload),
        };
        cancellationToken.ThrowIfCancellationRequested();
        return new PreparedRender(
            requestId,
            JsonSerializer.Serialize(message, PlotProtocolJsonContext.Default.PlotMessageEnvelope));
    }

    private static PlotDataPoint[] BuildBenchmarkPoints(bool is3D, CancellationToken cancellationToken)
    {
        var points = new PlotDataPoint[25_000];
        for (int index = 0; index < points.Length; index++)
        {
            if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            double phase = index * 0.0075;
            points[index] = new PlotDataPoint(
                $"p-{index + 1}",
                index / 250.0,
                Math.Sin(phase) + (index % 97) / 97.0,
                is3D ? Math.Cos(phase * 0.5) + index / 5000.0 : null,
                "research-series");
        }

        return points;
    }

    private async void OnScatter2DClick(object sender, RoutedEventArgs args) => await RenderAsync(PlotKind.Scatter2D);
    private async void OnScatter3DClick(object sender, RoutedEventArgs args) => await RenderAsync(PlotKind.Scatter3D);
    private async void OnLine3DClick(object sender, RoutedEventArgs args) => await RenderAsync(PlotKind.Line3D);
    private void OnExportPngClick(object sender, RoutedEventArgs args) => Export(PlotExportFormat.Png);
    private void OnExportSvgClick(object sender, RoutedEventArgs args) => Export(PlotExportFormat.Svg);

    private void Export(PlotExportFormat format)
    {
        if (!plotReady || (format == PlotExportFormat.Svg && activeKind is PlotKind.Scatter3D or PlotKind.Line3D)) return;
        Post(PlotMessageTypes.Export, JsonSerializer.SerializeToElement(
            new PlotExportPayload { Format = format, Width = 1600, Height = 900, Scale = 2 },
            PlotProtocolJsonContext.Default.PlotExportPayload));
        StatusText.Text = $"Exporting {format}...";
    }

    private void Post(string type, JsonElement payload)
    {
        if (!plotReady) return;
        PlotMessageEnvelope message = new()
        {
            SchemaVersion = PlotMessageCodec.CurrentSchemaVersion, Type = type, RequestId = Guid.NewGuid(),
            PlotId = activePlotId, Generation = generation, Payload = payload,
        };
        responseGate.RegisterCommand(activePlotId, generation, message.RequestId, message.Type);
        plotHost?.PostJson(JsonSerializer.Serialize(message, PlotProtocolJsonContext.Default.PlotMessageEnvelope));
    }

    private async Task SaveSpikeExportAsync(PlotExportCompletedPayload completed)
    {
        try
        {
            string prefix = $"data:{completed.MediaType};base64,";
            byte[] bytes = Convert.FromBase64String(completed.DataUrl[prefix.Length..]);
            string folder = Path.Combine(AppContext.BaseDirectory, "SpikeExports");
            Directory.CreateDirectory(folder);
            string extension = completed.Format == PlotExportFormat.Png ? "png" : "svg";
            await File.WriteAllBytesAsync(Path.Combine(folder, $"plot-export.{extension}"), bytes);
            StatusText.Text = $"{completed.Format} export completed";
        }
        catch (Exception)
        {
            StatusText.Text = "Export failed (PLOT_EXPORT_WRITE_FAILED)";
        }
    }

    private async Task InitializePlotHostAsync()
    {
        if (plotHost is not null) return;
        PlotHostWebViewAdapter adapter = new(PlotWebView);
        plotHost = adapter;
        adapter.TrustedMessageReceived += OnTrustedMessageReceived;
        StatusText.Text = "Starting renderer...";
        try
        {
            await adapter.InitializeAsync();
        }
        catch (PlotHostInitializationException error)
        {
            ShowPlotFallback(error.ErrorCode);
        }
        catch (Exception)
        {
            ShowPlotFallback("PLOT_INITIALIZATION_FAILED");
        }
    }

    private void ShowPlotFallback(string errorCode)
    {
        PlotWebView.Visibility = Visibility.Collapsed;
        PlotFallback.Visibility = Visibility.Visible;
        PlotFallback.Text = $"Visualization is unavailable ({errorCode}).";
        Title = $"FluoAxis - {errorCode}";
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Dispose();
    }

    public void Dispose()
    {
        workbenchLifetime.Cancel();
        workbenchLifetime.Dispose();
        renderPreparation?.Cancel();
        renderPreparation?.Dispose();
        if (activePlotId != Guid.Empty) responseGate.Dispose(activePlotId);
        if (plotHost is not null)
        {
            plotHost.TrustedMessageReceived -= OnTrustedMessageReceived;
            plotHost.Dispose();
        }
        Activated -= OnActivated;
        Closed -= OnClosed;
        GC.SuppressFinalize(this);
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        windowActivated.TrySetResult();
    }

    private sealed record PreparedRender(Guid RequestId, string Json);
}
