namespace FluoAxis.App;

using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FluoAxis.Contracts;
using FluoAxis.PlotProtocol;
using FluoAxis.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

public sealed partial class MainWindow
{
    private CancellationTokenSource? rawReadCancellation;
    private long rawReadGeneration;
    private long explorerEditGeneration;
    private CancellationTokenSource? explorerComputation;
    private bool rawReadBusy;
    private bool updatingRawControls;
    private bool explorerDemoMode;
    private bool activePlotIsDemo;
    private int activePlotPointCount;
    private bool renderedPlotAvailable;
    private Guid? rawCatalogRun;
    private int rawCatalogRevision = -1;
    private ExplorerPairPageDto? pairedPage;
    private ExplorerDataModel? explorerData;
    private ExplorerViewDefinitionDto? explorerDefinition;
    private int[] explorerRows = [];
    private PlotSpec? activeFrozenSpec;
    private PlotDataSnapshot? activeFrozenData;
    private string? measureFirstId;
    private bool measureNextPoint;
    private bool explorerViewDirty;
    private bool explorerComputing;
    private bool explorerOperationBusy;
    private bool IsManualExplorer => ExplorerModeBox.SelectedItem is ComboBoxItem { Tag: "manual" };

    private void InitializeExplorerControls(bool benchmark)
    {
        updatingRawControls = true;
        explorerDemoMode = benchmark;
        ExplorerModeBox.SelectedIndex = benchmark ? 1 : 0;
        RawChannelBox.SelectedIndex = 0;
        SmoothingBox.SelectedIndex = 0;
        SmoothingOrderBox.SelectedIndex = 0;
        updatingRawControls = false;
        UpdateExplorerMode();
    }

    private void BindRawExplorerSource()
    {
        Guid? runId = viewModel.Run?.RunId;
        int revision = viewModel.Run?.Revision ?? -1;
        if (rawCatalogRun == runId && rawCatalogRevision == revision) { UpdateRawControls(); return; }
        bool changedRun = rawCatalogRun != runId;
        rawCatalogRun = runId; rawCatalogRevision = revision;
        string? selected = RawWellBox.SelectedItem as string;
        updatingRawControls = true;
        RawWellBox.Items.Clear();
        string[] wells = viewModel.CurrentEvidence?.Mapping?.Fam.WellColumns.Select(item => item.Well).ToArray()
            ?? viewModel.CurrentEvidence?.Assignments.Select(item => item.Well).Distinct(StringComparer.Ordinal).ToArray() ?? [];
        foreach (string well in wells) RawWellBox.Items.Add(well);
        RawWellBox.SelectedItem = selected is not null && wells.Contains(selected, StringComparer.Ordinal) ? selected : wells.FirstOrDefault();
        updatingRawControls = false;
        if (changedRun) { explorerDefinition = null; explorerData = null; ExplorerTable.ItemsSource = null; pairedPage = null; explorerViewDirty = false; }
        if (!explorerDemoMode)
        {
            CancelRawRead();
            ClearVisiblePlot(wells.Length == 0 ? "当前 Run 尚无原始信号。可新建独立手工科研表，或先完成数据映射。" : "选择孔位读取配对 FAM/ROX 原始记录。");
            if (viewModel.ActiveArea == "explorer" && plotReady && wells.Length > 0 && !IsManualExplorer) TrackPlotOperation(() => LoadRawPageAsync(0));
        }
        UpdateRawControls();
    }

    private async void OnExplorerModeChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!uiInitialized || updatingRawControls || closeRequested || disposed) return;
        bool nextDemo = ExplorerModeBox.SelectedItem is ComboBoxItem { Tag: "demo" };
        string nextMode = IsManualExplorer ? "manual" : "raw";
        if (!nextDemo && explorerDefinition is { } previous && previous.Mode != nextMode && explorerViewDirty
            && !await ConfirmExplorerReplacementAsync())
        {
            updatingRawControls = true; ExplorerModeBox.SelectedIndex = explorerDemoMode ? 1 : previous.Mode == "manual" ? 2 : 0; updatingRawControls = false;
            return;
        }
        explorerDemoMode = nextDemo;
        CancelRawRead();
        if (!nextDemo && explorerDefinition?.Mode != nextMode) { pairedPage = null; explorerData = null; explorerDefinition = null; explorerViewDirty = false; }
        ExplorerTable.ItemsSource = null;
        ClearVisiblePlot(explorerDemoMode ? "正在准备显式选择的演示数据。" : "准备科研视图。");
        UpdateExplorerMode();
        if (IsManualExplorer && explorerDefinition is { Mode: "manual" }) TrackExplorerEditAsync(() => RebuildExplorerAsync(workbenchLifetime.Token));
        else if (IsManualExplorer) CreateManualTable([]);
        else if (plotReady) TrackPlotOperation(RenderExplorerAfterActivationAsync);
    }

    private void UpdateExplorerMode()
    {
        ExplorerNotice.Title = explorerDemoMode ? "演示数据 · 非实验结果" : IsManualExplorer ? "手工科研表 · 与正式分析隔离" : "Raw / Derived 科研视图";
        ExplorerNotice.Message = explorerDemoMode ? "固定演示点不是实验结果；三维相机固定。"
            : "Raw 只读；公式、平滑和单元格编辑只生成科研派生值，不进入正式判读。配对页最多 25,000 点，手工表最多 10,000 行。三维相机保持固定。";
        UpdateRawControls();
    }

    private void UpdateRawControls()
    {
        bool enabled = !explorerDemoMode && !rawReadBusy && !explorerComputing && !explorerOperationBusy && !IsWorkbenchBusy && workspaceAvailable;
        ExplorerModeBox.IsEnabled = !closeRequested && !disposed && !IsWorkbenchBusy && !rawReadBusy && !explorerComputing && !explorerOperationBusy;
        ExplorerSettingsButton.IsEnabled = enabled && viewModel.Run is not null;
        RawWellBox.IsEnabled = enabled && !IsManualExplorer && RawWellBox.Items.Count > 0;
        RawChannelBox.IsEnabled = enabled && !IsManualExplorer;
        LoadRawButton.IsEnabled = enabled && !IsManualExplorer && RawWellBox.SelectedItem is string && viewModel.Run is not null && plotReady;
        PreviousRawButton.IsEnabled = enabled && pairedPage is { Offset: > 0 };
        NextRawButton.IsEnabled = enabled && pairedPage is { } page && page.Points.Count > 0 && (long)page.Offset + page.Points.Count < page.TotalPoints;
        DemoScatter3DButton.IsEnabled = plotReady && !rawReadBusy && !explorerComputing && (explorerDemoMode || explorerData is { Count: > 0 });
        DemoLine3DButton.IsEnabled = DemoScatter3DButton.IsEnabled;
        SaveExplorerButton.IsEnabled = enabled && explorerDefinition is not null;
        RestoreExplorerButton.IsEnabled = enabled && viewModel.Run is not null;
        ExportPngButton.IsEnabled = plotReady && renderedPlotAvailable && !rawReadBusy && !closeRequested;
        ExportSvgButton.IsEnabled = ExportPngButton.IsEnabled && activeKind is not (PlotKind.Scatter3D or PlotKind.Line3D) && activePlotPointCount < 10_000;
        ToolTipService.SetToolTip(ExportSvgButton, "二维少于 10,000 点支持矢量 SVG；大数据和三维图请用 PNG。");
        ExportHtmlPlotButton.IsEnabled = ExportPngButton.IsEnabled && activeFrozenSpec is not null && activeFrozenData is not null;
        AttachReportFigureButton.IsEnabled = ExportHtmlPlotButton.IsEnabled && !activePlotIsDemo && viewModel.Run is not null && !IsWorkbenchBusy;
        UpdateDraftStatus();
    }

    private async Task<bool> ConfirmExplorerReplacementAsync()
    {
        if (!explorerViewDirty) return true;
        if (draftDialogOpen) return false;
        draftDialogOpen = true;
        try
        {
            var dialog = new ContentDialog { XamlRoot = RootSurface.XamlRoot, Title = "替换未保存的科研视图？",
                Content = "当前公式、列映射或手工数据尚未保存。替换只影响科研视图，正式 Run 不会改变。", PrimaryButtonText = "丢弃视图并继续", CloseButtonText = "返回保存", DefaultButton = ContentDialogButton.Close };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch (Exception) { return false; }
        finally { draftDialogOpen = false; }
    }

    private void OnRawSeriesChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!uiInitialized || updatingRawControls || explorerDemoMode || IsManualExplorer || closeRequested || disposed) return;
        if (ReferenceEquals(sender, RawChannelBox) && explorerDefinition is not null)
        {
            string y = RawChannelBox.SelectedIndex == 1 ? "rox" : "fam";
            explorerDefinition = CopyDefinition(explorerDefinition, y: y); explorerViewDirty = true; UpdateColumnChoices();
            TrackPlotOperation(RenderCurrentRawPageAsync); return;
        }
        CancelRawRead(); pairedPage = null; explorerData = null; ExplorerTable.ItemsSource = null;
        if (plotReady) TrackPlotOperation(() => LoadRawPageAsync(0));
    }

    private void OnLoadRawClick(object sender, RoutedEventArgs args) => TrackPlotOperation(() => LoadRawPageAsync(0));
    private void OnPreviousRawClick(object sender, RoutedEventArgs args) => TrackPlotOperation(() => LoadRawPageAsync(Math.Max(0, (pairedPage?.Offset ?? 0) - RawSignalPageDto.MaximumPageSize)));
    private void OnNextRawClick(object sender, RoutedEventArgs args) => TrackPlotOperation(() => LoadRawPageAsync((pairedPage?.Offset ?? 0) + (pairedPage?.Points.Count ?? 0)));
    private void OnRawLineClick(object sender, RoutedEventArgs args) => SetExplorerPlotKind(PlotKind.Line2D);
    private void OnScatterLineClick(object sender, RoutedEventArgs args) => SetExplorerPlotKind(PlotKind.ScatterLine2D);
    private void SetExplorerPlotKind(PlotKind kind)
    {
        if (explorerDemoMode) TrackPlotOperation(() => RenderAsync(kind));
        else if (explorerDefinition is not null)
        {
            explorerDefinition = CopyDefinition(explorerDefinition, kind: kind.ToString()); explorerViewDirty = true;
            TrackPlotOperation(RenderCurrentRawPageAsync);
        }
    }

    private async Task RenderExplorerAfterActivationAsync()
    {
        await windowActivated.Task.WaitAsync(workbenchLifetime.Token);
        if (explorerDemoMode) await RenderAsync(initialKind);
        else if (IsManualExplorer) await RebuildExplorerAsync();
        else await LoadRawPageAsync(0);
    }

    private async Task LoadRawPageAsync(int offset)
    {
        if (explorerDemoMode || IsManualExplorer || closeRequested || disposed || !workspaceAvailable || !plotReady) return;
        if (viewModel.Run is not { } run || RawWellBox.SelectedItem is not string well)
        { ClearVisiblePlot("当前 Run 没有可配对的原始信号；先完成数据映射，或选择手工科研表。"); return; }
        CancelRawRead(); long request = ++rawReadGeneration;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(workbenchLifetime.Token);
        rawReadCancellation = cancellation; rawReadBusy = true; pairedPage = null; explorerData = null;
        ClearVisiblePlot("正在读取配对原始信号页…"); UpdateRawControls();
        ExplorerTable.ItemsSource = null;
        try
        {
            GatewayResult<ExplorerPairPageDto> result = await workspace.GetExplorerPairPageAsync(run.RunId, well, offset, 25000, cancellation.Token);
            if (request != rawReadGeneration || explorerDemoMode || closeRequested || disposed || viewModel.Run?.RunId != run.RunId) return;
            if (!result.IsSuccess) { ClearVisiblePlot($"{result.Errors[0].UserMessage}\n{result.Errors[0].Code}"); return; }
            ExplorerPairPageDto page = result.Value!;
            if (page.Revision != viewModel.Run?.Revision || page.Well != well) { ClearVisiblePlot("Run 已更新，请重新读取。"); return; }
            pairedPage = page;
            if (explorerDefinition is null || explorerDefinition.SourceSha256 != page.SourceSha256)
                explorerDefinition = DefaultDefinition(page.RunId, page.SourceSha256, page.Well, false);
            else if (explorerDefinition.Well != page.Well) { explorerDefinition = CopyDefinition(explorerDefinition, well: page.Well); explorerViewDirty = true; }
            explorerDefinition = CopyDefinition(explorerDefinition, offset: offset);
            await RebuildExplorerAsync(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally { if (request == rawReadGeneration) { rawReadCancellation = null; rawReadBusy = false; UpdateRawControls(); } }
    }

    private void CancelRawRead() { rawReadGeneration++; explorerEditGeneration++; rawReadCancellation?.Cancel(); rawReadCancellation = null; explorerComputation?.Cancel(); rawReadBusy = false; }
    private void ClearVisiblePlot(string message)
    {
        renderPreparation?.Cancel();
        if (activePlotId != Guid.Empty)
        {
            if (plotReady) { try { Post(PlotMessageTypes.Dispose, JsonSerializer.SerializeToElement(new EmptyPlotPayload(), PlotProtocolJsonContext.Default.EmptyPlotPayload)); } catch (Exception) { } }
            responseGate.Dispose(activePlotId); activePlotId = Guid.Empty;
        }
        renderedPlotAvailable = false; activeFrozenSpec = null; activeFrozenData = null; measureFirstId = null;
        selectionRequests.Clear();
        PlotWebView.Visibility = Visibility.Collapsed; PlotFallback.Visibility = Visibility.Visible; PlotFallback.Text = message; StatusText.Text = message;
        RawPageText.Text = message; UpdateRawControls();
    }

    private async Task RebuildExplorerAsync(CancellationToken cancellationToken = default)
    {
        if (explorerDefinition is not { } definition) return;
        explorerComputation?.Cancel();
        using var computation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, workbenchLifetime.Token);
        explorerComputation = computation;
        explorerComputing = true; UpdateRawControls();
        Guid runId = definition.RunId;
        ExplorerPairPageDto? page = pairedPage;
        ExplorerDataModel data = definition.Mode == "manual" ? ExplorerDataModel.FromManual(runId, definition.ManualPoints)
            : page is not null ? ExplorerDataModel.FromPaired(page) : throw new ArgumentException("尚无原始页。");
        try { await Task.Run(() => data.Apply(definition, computation.Token), computation.Token); }
        finally { if (ReferenceEquals(explorerComputation, computation)) { explorerComputation = null; explorerComputing = false; UpdateRawControls(); } }
        if (viewModel.Run?.RunId != runId || !ReferenceEquals(explorerDefinition, definition) || closeRequested || disposed) return;
        explorerData = data; explorerRows = SelectDefinitionRows(data, definition);
        UpdateColumnChoices(); RenderExplorerTable();
        await RenderCurrentRawPageAsync();
    }

    private void UpdateColumnChoices()
    {
        if (explorerData is not { } data || explorerDefinition is not { } definition) return;
        updatingRawControls = true;
        foreach (ComboBox chooser in new[] { ExplorerXBox, ExplorerYBox, ExplorerZBox, ExplorerEditColumnBox, ExplorerFilterColumnBox })
        {
            string? prior = (chooser.SelectedItem as ComboBoxItem)?.Tag as string;
            chooser.Items.Clear();
            foreach (ExplorerColumn column in data.Columns) chooser.Items.Add(new ComboBoxItem { Content = $"{column.Label} [{column.Id}]", Tag = column.Id });
            string preferred = ReferenceEquals(chooser, ExplorerXBox) ? definition.XColumn : ReferenceEquals(chooser, ExplorerYBox) ? definition.YColumn : ReferenceEquals(chooser, ExplorerZBox) ? definition.ZColumn : prior ?? definition.YColumn;
            chooser.SelectedItem = chooser.Items.Cast<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, preferred)) ?? chooser.Items[0];
        }
        ExplorerLogX.IsChecked = definition.XScale == "Log"; ExplorerLogY.IsChecked = definition.YScale == "Log"; ExplorerLogZ.IsChecked = definition.ZScale == "Log";
        ExplorerFilterMinBox.Value = definition.FilterMinimum ?? double.NaN; ExplorerFilterMaxBox.Value = definition.FilterMaximum ?? double.NaN;
        ExplorerSortDescending.IsChecked = definition.SortDescending;
        if (definition.FilterColumn is { } filter) ExplorerFilterColumnBox.SelectedItem = ExplorerFilterColumnBox.Items.Cast<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, filter));
        foreach (ComboBox chooser in new[] { ExplorerSecondaryYBox, ExplorerErrorYBox })
        {
            chooser.Items.Clear(); chooser.Items.Add(new ComboBoxItem { Content = "无" });
            foreach (ExplorerColumn column in data.Columns) chooser.Items.Add(new ComboBoxItem { Content = $"{column.Label} [{column.Id}]", Tag = column.Id });
            string? selected = ReferenceEquals(chooser, ExplorerSecondaryYBox) ? definition.SecondaryYColumn : definition.ErrorYColumn;
            chooser.SelectedItem = chooser.Items.Cast<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, selected)) ?? chooser.Items[0];
        }
        updatingRawControls = false;
    }

    private void RenderExplorerTable()
    {
        if (explorerData is not { } data) return;
        ExplorerTableHeader.Children.Clear();
        foreach (ExplorerColumn column in data.Columns) ExplorerTableHeader.Children.Add(new TextBlock { Text = $"{column.Label}\n{column.Unit}{(column.IsRaw ? " · Raw" : " · Derived")}", Width = 110, Padding = new Thickness(8), FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = WorkbenchBrush("PrimaryText"), TextWrapping = TextWrapping.Wrap });
        ExplorerTable.ItemsSource = new ExplorerRows(data, explorerRows);
        RawPageText.Text = $"{(data.IsManual ? "手工研究数据" : $"{data.Well} · Raw 配对页")} · 表格 {explorerRows.Length:N0}/{data.Count:N0} 行 · {data.InvalidDerivedValues:N0} 个派生缺值。来源/排序不改变正式分析。";
    }

    private async Task RenderCurrentRawPageAsync()
    {
        if (explorerDemoMode || !plotReady) return;
        if (explorerData is not { Count: > 0 } data || explorerDefinition is not { } definition)
        {
            ClearVisiblePlot("当前科研表为空。添加手工行、粘贴数值表，或读取已保存的原始信号。");
            return;
        }
        renderPreparation?.Cancel(); renderPreparation?.Dispose(); renderPreparation = CancellationTokenSource.CreateLinkedTokenSource(workbenchLifetime.Token);
        CancellationToken token = renderPreparation.Token;
        Guid plotId = activePlotId = Guid.NewGuid(); long renderGeneration = ++generation;
        selectionRequests.Clear();
        responseGate.Activate(plotId, renderGeneration);
        PlotKind kind = Enum.Parse<PlotKind>(definition.PlotKind); activeKind = kind;
        activePlotIsDemo = false; renderedPlotAvailable = false; UpdateRawControls();
        int[] rows = explorerRows;
        var prepared = await Task.Run(() => PrepareExplorerRender(data, definition, rows, plotId, renderGeneration, token), token);
        if (token.IsCancellationRequested || plotId != activePlotId || renderGeneration != generation || explorerDemoMode || !ReferenceEquals(data, explorerData)) return;
        activeFrozenSpec = prepared.Spec; activeFrozenData = prepared.Data; activePlotPointCount = prepared.Data.Points.Count;
        measureFirstId = null; measureNextPoint = false;
        if (activePlotPointCount == 0) { ClearVisiblePlot("当前列/筛选没有可绘制的有限值；log 轴要求正值。原始数据保持不变。"); return; }
        responseGate.RegisterCommand(plotId, renderGeneration, prepared.RequestId, PlotMessageTypes.Render);
        PlotFallback.Visibility = Visibility.Collapsed; PlotWebView.Visibility = Visibility.Visible; renderTimer.Restart();
        plotHost?.PostJson(prepared.Json); StatusText.Text = $"正在绘制 {activePlotPointCount:N0} 点 · {(data.IsManual ? "手工" : "Raw / Derived")}…";
    }

    private static ExplorerPreparedRender PrepareExplorerRender(ExplorerDataModel data, ExplorerViewDefinitionDto definition, int[] rows, Guid plotId, long renderGeneration, CancellationToken token)
    {
        PlotKind kind = Enum.Parse<PlotKind>(definition.PlotKind);
        bool three = kind is PlotKind.Scatter3D or PlotKind.Line3D;
        var points = new List<PlotDataPoint>(rows.Length * (definition.SecondaryYColumn is null || three ? 1 : 2));
        foreach (int row in rows)
        {
            if ((row & 127) == 0) token.ThrowIfCancellationRequested();
            double? x = data.Value(definition.XColumn, row), y = data.Value(definition.YColumn, row), z = three ? data.Value(definition.ZColumn, row) : null;
            if (x is null || y is null || (three && z is null) || definition.XScale == "Log" && x <= 0 || definition.YScale == "Log" && y <= 0 || three && definition.ZScale == "Log" && z <= 0) continue;
            double? uncertainty = !three && definition.ErrorYColumn is { } errorColumn ? data.Value(errorColumn, row) : null;
            if (!three && definition.ErrorYColumn is not null && (uncertainty is null || uncertainty < 0)) continue;
            points.Add(new PlotDataPoint(data.PointId(row), x.Value, y.Value, z, "view", errorY: uncertainty));
            if (!three && definition.SecondaryYColumn is { } second && data.Value(second, row) is { } otherY && (definition.YScale != "Log" || otherY > 0))
                points.Add(new PlotDataPoint(data.PointId(row) + "-s2", x.Value, otherY, null, "second", errorY: uncertainty));
        }
        AxisSpec Axis(string id, string scale)
        {
            ExplorerColumn? column = data.Columns.FirstOrDefault(item => item.Id == id);
            if (column is null) throw new ArgumentException("绘图列不存在。");
            return new AxisSpec($"{column.Label}{(column.Unit.Length == 0 ? "" : $" / {column.Unit}")}", Enum.Parse<AxisScale>(scale));
        }
        var series = new List<SeriesSpec> { new("view", data.Columns.First(column => column.Id == definition.YColumn).Label, markerSize: 5) };
        if (!three && definition.SecondaryYColumn is { } secondary) series.Add(new SeriesSpec("second", data.Columns.First(column => column.Id == secondary).Label, markerSize: 5));
        var spec = new PlotSpec(plotId, kind, Axis(definition.XColumn, definition.XScale), Axis(definition.YColumn, definition.YScale), three ? Axis(definition.ZColumn, definition.ZScale) : null,
            series, three ? new CameraSpec(1.8, 1.8, 1.5333333333333334) : null, true,
            $"{(data.IsManual ? "手工表" : data.Well)} · 研究显示（不参与正式判读）");
        var snapshot = new PlotDataSnapshot(plotId, three ? PlotDimensions.Three : PlotDimensions.Two, 50000, points);
        Guid request = Guid.NewGuid();
        var envelope = new PlotMessageEnvelope { SchemaVersion = 1, Type = PlotMessageTypes.Render, RequestId = request, PlotId = plotId, Generation = renderGeneration,
            Payload = JsonSerializer.SerializeToElement(new PlotRenderPayload { Spec = spec, Data = snapshot, DataRevision = 1 }, PlotProtocolJsonContext.Default.PlotRenderPayload) };
        return new ExplorerPreparedRender(request, JsonSerializer.Serialize(envelope, PlotProtocolJsonContext.Default.PlotMessageEnvelope), spec, snapshot);
    }

    private void OnExplorerAxesChanged(object sender, SelectionChangedEventArgs args)
    {
        if (updatingRawControls || explorerDefinition is null) return;
        explorerDefinition = CopyDefinition(explorerDefinition, x: SelectedId(ExplorerXBox), y: SelectedId(ExplorerYBox), z: SelectedId(ExplorerZBox));
        explorerViewDirty = true;
        TrackPlotOperation(RenderCurrentRawPageAsync);
    }
    private void OnExplorerScaleChanged(object sender, RoutedEventArgs args)
    {
        if (updatingRawControls || explorerDefinition is null) return;
        explorerDefinition = CopyDefinition(explorerDefinition, xs: ExplorerLogX.IsChecked == true ? "Log" : "Linear", ys: ExplorerLogY.IsChecked == true ? "Log" : "Linear", zs: ExplorerLogZ.IsChecked == true ? "Log" : "Linear");
        explorerViewDirty = true;
        TrackPlotOperation(RenderCurrentRawPageAsync);
    }
    private void OnExplorerSeriesChanged(object sender, SelectionChangedEventArgs args)
    {
        if (updatingRawControls || explorerDefinition is null) return;
        explorerDefinition = CopyDefinition(explorerDefinition, secondary: SelectedId(ExplorerSecondaryYBox), error: SelectedId(ExplorerErrorYBox), replaceSeries: true);
        explorerViewDirty = true;
        TrackPlotOperation(RenderCurrentRawPageAsync);
    }
    private void OnAddDerivedClick(object sender, RoutedEventArgs args) => TrackExplorerEditAsync(async () =>
    {
        if (explorerDefinition is not { } definition) return;
        string id = DerivedNameBox.Text.Trim();
        var column = new ExplorerDerivedColumnDto(id, id, "", DerivedExpressionBox.Text.Trim(), (SmoothingBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "none",
            ReadPositiveInteger(SmoothingWindowBox.Value), int.Parse((SmoothingOrderBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "2", CultureInfo.InvariantCulture));
        ExplorerViewDefinitionDto candidate = CopyDefinition(definition, derived: [.. definition.DerivedColumns, column]);
        await ValidateAndApplyDefinitionAsync(candidate);
    });
    private void OnClearDerivedClick(object sender, RoutedEventArgs args) => TrackExplorerEditAsync(async () =>
    {
        if (explorerDefinition is not { } definition) return;
        string[] retained = IsManualExplorer ? ["index", "x", "y", "z"] : ["index", "time", "fam", "rox"];
        await ValidateAndApplyDefinitionAsync(CopyDefinition(definition, derived: [], edits: [], labels: definition.Labels.Where(label => retained.Contains(label.Id, StringComparer.Ordinal)).ToArray(),
            x: IsManualExplorer ? "x" : "time", y: IsManualExplorer ? "y" : "fam", z: IsManualExplorer ? "z" : "rox", replaceSeries: true, replaceFilter: true));
    });
    private void OnRenameExplorerColumnClick(object sender, RoutedEventArgs args) => TrackExplorerEditAsync(async () =>
    {
        if (explorerDefinition is not { } definition || SelectedId(ExplorerEditColumnBox) is not { } id) return;
        var labels = definition.Labels.Where(item => item.Id != id).Append(new ExplorerColumnLabelDto(id, ExplorerColumnLabelBox.Text.Trim(), ExplorerColumnUnitBox.Text.Trim())).ToArray();
        await ValidateAndApplyDefinitionAsync(CopyDefinition(definition, labels: labels));
    });
    private void OnEditExplorerCellClick(object sender, RoutedEventArgs args) => TrackExplorerEditAsync(async () =>
    {
        if (explorerDefinition is not { } definition || explorerData is not { } data || ExplorerTable.SelectedItem is not ExplorerGridRow row || SelectedId(ExplorerEditColumnBox) is not { } id) return;
        if (!data.CanEdit(id)) throw new ArgumentException("Raw 列只读，请先创建派生列再编辑。");
        if (!double.TryParse(ExplorerEditValueBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value)) throw new ArgumentException("编辑值必须是有限数字（小数点为 .）。");
        string pointId = data.PointId(row.SourceIndex);
        var edits = definition.Overrides.Where(item => item.PointId != pointId || item.ColumnId != id).Append(new ExplorerCellOverrideDto(pointId, id, value)).ToArray();
        await ValidateAndApplyDefinitionAsync(CopyDefinition(definition, edits: edits));
    });
    private void TrackExplorerEditAsync(Func<Task> operation)
    {
        if (explorerOperationBusy || rawReadBusy || IsWorkbenchBusy || closeRequested || disposed) return;
        explorerOperationBusy = true; UpdateRawControls();
        TrackPlotOperation(async () =>
        {
            try { await operation(); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
            { StatusText.Text = "研究视图操作被拒绝。"; RawPageText.Text = error is ArgumentException ? error.Message : "计算超过资源预算或出现无效拟合；请简化公式/减小页面。"; }
            finally { explorerOperationBusy = false; UpdateRawControls(); }
        });
    }
    private async Task ValidateAndApplyDefinitionAsync(ExplorerViewDefinitionDto candidate)
    {
        long request = ++explorerEditGeneration;
        explorerComputation?.Cancel();
        using var computation = CancellationTokenSource.CreateLinkedTokenSource(workbenchLifetime.Token);
        explorerComputation = computation;
        explorerComputing = true; UpdateRawControls();
        ExplorerDataModel model = candidate.Mode == "manual" ? ExplorerDataModel.FromManual(candidate.RunId, candidate.ManualPoints)
            : pairedPage is { } page ? ExplorerDataModel.FromPaired(page) : throw new ArgumentException("没有原始页。");
        try { await Task.Run(() => model.Apply(candidate, computation.Token), computation.Token); }
        finally { if (ReferenceEquals(explorerComputation, computation)) { explorerComputation = null; explorerComputing = false; UpdateRawControls(); } }
        if (request != explorerEditGeneration || viewModel.Run?.RunId != candidate.RunId || closeRequested || disposed) return;
        explorerDefinition = candidate; explorerViewDirty = true; explorerData = model; explorerRows = SelectDefinitionRows(model, candidate); UpdateColumnChoices(); RenderExplorerTable(); await RenderCurrentRawPageAsync();
    }
    private void OnFilterExplorerClick(object sender, RoutedEventArgs args) => TrackExplorerEditAsync(async () =>
    {
        if (explorerData is not { } data || explorerDefinition is not { } definition) return;
        string? id = SelectedId(ExplorerFilterColumnBox);
        ExplorerViewDefinitionDto candidate = CopyDefinition(definition, filter: id, low: double.IsNaN(ExplorerFilterMinBox.Value) ? null : ExplorerFilterMinBox.Value,
                high: double.IsNaN(ExplorerFilterMaxBox.Value) ? null : ExplorerFilterMaxBox.Value, sort: id, descending: ExplorerSortDescending.IsChecked == true, replaceFilter: true);
        int[] rows = await Task.Run(() => SelectDefinitionRows(data, candidate), workbenchLifetime.Token);
        if (!ReferenceEquals(explorerData, data) || !ReferenceEquals(explorerDefinition, definition)) return;
        explorerDefinition = candidate; explorerRows = rows; explorerViewDirty = true;
        RenderExplorerTable(); await RenderCurrentRawPageAsync();
    });
    private void OnClearExplorerFilterClick(object sender, RoutedEventArgs args) { if (explorerData is { } data && explorerDefinition is { } definition) { explorerDefinition = CopyDefinition(definition, replaceFilter: true); explorerRows = data.SelectRows(); explorerViewDirty = true; UpdateColumnChoices(); RenderExplorerTable(); TrackPlotOperation(RenderCurrentRawPageAsync); } }
    private void OnExplorerStatisticsClick(object sender, RoutedEventArgs args) => TrackExplorerEditAsync(async () =>
    {
        if (explorerData is not { } data || SelectedId(ExplorerFilterColumnBox) is not { } column) return;
        int[] rows = explorerRows;
        ExplorerColumnSummary summary = await Task.Run(() => data.Summarize(column, rows), workbenchLifetime.Token);
        if (!ReferenceEquals(explorerData, data)) return;
        RawPageText.Text = $"当前页/筛选后的 {column} 描述统计（仅科研）\n有效 {summary.Count} · 缺值 {summary.Missing} · min {summary.Minimum:G17} · max {summary.Maximum:G17}\nmean {summary.Mean:G17} · median {summary.Median:G17} · sample sd {summary.SampleStandardDeviation?.ToString("G17", CultureInfo.InvariantCulture) ?? "不可计算"}\n不影响正式分析，也不表示全 Run 总体统计。";
    });
    private void OnCopyExplorerClick(object sender, RoutedEventArgs args)
    {
        if (explorerData is not { } data) return;
        ExplorerGridRow[] selected = ExplorerTable.SelectedItems.Cast<ExplorerGridRow>().ToArray();
        if ((long)selected.Length * data.Columns.Count > 10000) { RawPageText.Text = "复制最多 10,000 单元格；请缩小选择。"; return; }
        var text = new StringBuilder(string.Join('\t', data.Columns.Select(column => column.Id)));
        foreach (ExplorerGridRow row in selected) text.Append('\n').Append(string.Join('\t', data.Columns.Select(column => data.Value(column.Id, row.SourceIndex)?.ToString("G17", CultureInfo.InvariantCulture) ?? "")));
        var package = new DataPackage(); package.SetText(text.ToString()); Clipboard.SetContent(package);
        RawPageText.Text = $"已复制 {selected.Length} 行研究显示数据。";
    }
    private void OnExplorerTableSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (synchronizingExplorerSelection) return;
        SendSelectedRowsToPlot();
        if (ExplorerTable.SelectedItem is not ExplorerGridRow row || explorerData is not { } data) return;
        if (!data.IsManual) HighlightPlateWellFromExplorer(data.Well);
        ShowRawPoint(data.PointId(row.SourceIndex));
    }

    private void HighlightPlateWellFromExplorer(string well)
    {
        // Selection only: never edit a role or submit a workflow command.
        if (!wellButtons.ContainsKey(well)) return;
        plateDraft.Select(well);
        RefreshWellAppearance();
        UpdatePlateActions();
    }

    private void SelectExplorerWellFromPlate(string well)
    {
        if (explorerDemoMode || IsManualExplorer || closeRequested || disposed || !RawWellBox.Items.Contains(well)) return;
        RawWellBox.SelectedItem = well;
    }
    private void OnNewManualTableClick(object sender, RoutedEventArgs args) => CreateManualTable([]);
    private void OnAddManualRowClick(object sender, RoutedEventArgs args)
    {
        if (explorerDefinition is not { Mode: "manual" } definition) { CreateManualTable([new(0, 0, 0, 0)]); return; }
        int next = definition.ManualPoints.Count == 0 ? 0 : definition.ManualPoints.Max(point => point.PointIndex) + 1;
        TrackExplorerEditAsync(() => ValidateAndApplyDefinitionAsync(CopyDefinition(definition, manual: [.. definition.ManualPoints, new(next, 0, 0, 0)])));
    }
    private void OnPasteManualTableClick(object sender, RoutedEventArgs args)
    {
        try
        {
            string[] lines = ExplorerPasteBox.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 10000) throw new ArgumentException("手工表最多 10,000 行。");
            var points = new List<ExplorerManualPointDto>();
            foreach (string line in lines)
            {
                string[] cells = line.Split(['\t', ','], StringSplitOptions.TrimEntries);
                if (cells.Length != 3) throw new ArgumentException("每行需要恰好 X/Y/Z 三个数值。");
                double[] values = cells.Select(cell => double.Parse(cell, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
                if (values.Any(value => !double.IsFinite(value))) throw new ArgumentException("不接受非有限值。");
                points.Add(new(points.Count, values[0], values[1], values[2]));
            }
            CreateManualTable(points);
        }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException) { RawPageText.Text = "粘贴失败：只接受最多 10,000 行、每行三个有限数字（小数点 .），不会执行单元格公式。"; }
    }
    private void CreateManualTable(List<ExplorerManualPointDto> points)
    {
        if (viewModel.Run is not { } run) { RawPageText.Text = "先创建或打开项目，再建立独立手工科研表。"; return; }
        CancelRawRead(); updatingRawControls = true; ExplorerModeBox.SelectedIndex = 2; explorerDemoMode = false; updatingRawControls = false;
        pairedPage = null; explorerDefinition = DefaultDefinition(run.RunId, null, "A1", true, points); explorerViewDirty = true; UpdateExplorerMode();
        ClearVisiblePlot(points.Count == 0 ? "新的手工科研表为空；原始信号不受影响。" : "正在准备手工科研数据…");
        TrackExplorerEditAsync(() => RebuildExplorerAsync(workbenchLifetime.Token));
    }
    private void OnMeasureDistanceClick(object sender, RoutedEventArgs args)
    {
        measureFirstId = null; measureNextPoint = true; RawPageText.Text = "依次选择图中的两个点。距离使用当前 X/Y/Z 列的完整 C# 数值；不同单位不可解释为物理距离。";
    }
    private void ShowRawPoint(string pointId)
    {
        string sourceIdentity = string.Empty;
        if (explorerData is { } model)
        {
            if (!model.IsManual) HighlightPlateWellFromExplorer(model.Well);
            string sourcePointId = pointId.EndsWith("-s2", StringComparison.Ordinal) ? pointId[..^3] : pointId;
            int row = model.IndexOfPoint(sourcePointId);
            if (row >= 0)
                sourceIdentity = model.IsManual
                    ? $"手工科研行 ID：{sourcePointId}（独立于 Raw 和正式分析）"
                    : $"Raw 来源：Run {model.RunId} · 孔 {model.Well} · PointIndex {model.Value("index", row):G17}\n原始 FAM={model.Value("fam", row):G17} RFU · ROX={model.Value("rox", row):G17} RFU · 时间={model.Value("time", row):G17} min\n源 SHA-256：{model.SourceSha256}";
        }
        if (activeFrozenData is { } frozen && frozen.TryGetPoint(pointId, out PlotDataPoint? point))
        {
            if (measureNextPoint)
            {
                if (measureFirstId is null) { measureFirstId = pointId; RawPageText.Text = $"点 A：{pointId}；请选择点 B。"; return; }
                try { PlotDistanceMeasurement measured = frozen.Measure(measureFirstId, pointId); RawPageText.Text = $"C# {measured.Dimensions}D 距离 = {measured.Distance:G17}\nΔX={measured.DeltaX:G17} · ΔY={measured.DeltaY:G17} · ΔZ={measured.DeltaZ?.ToString("G17", CultureInfo.InvariantCulture) ?? "—"}\n仅当前科研坐标的几何工具，不是 BRAF 正式参数。"; }
                catch (Exception error) when (error is ArgumentException or KeyNotFoundException) { RawPageText.Text = "所选点已改变或两点差值超出有限数值范围，请重新选点。"; }
                measureNextPoint = false; return;
            }
            RawPageText.Text = $"{sourceIdentity}\n显示 PointId：{pointId}\nX={point!.X:G17} · Y={point.Y:G17} · Z={point.Z?.ToString("G17", CultureInfo.InvariantCulture) ?? "—"}\n当前研究显示值；Raw 原记录只读。";
        }
        else if (sourceIdentity.Length > 0) RawPageText.Text = $"{sourceIdentity}\n该行因当前筛选、缺值或 log 轴限制未绘图，原始记录仍可复核。";
    }
    private void OnSaveExplorerClick(object sender, RoutedEventArgs args) => TrackExplorerEditAsync(async () =>
    {
        if (explorerDefinition is not { } definition) return;
        GatewayResult<SavedExplorerViewDto> result = await workspace.SaveExplorerViewAsync(definition, workbenchLifetime.Token);
        if (result.IsSuccess && ReferenceEquals(explorerDefinition, definition)) { explorerViewDirty = false; UpdateDraftStatus(); }
        RawPageText.Text = result.IsSuccess ? $"视图定义已保存 · SHA-256 {result.Value!.Sha256}" : result.Errors[0].UserMessage;
        StatusText.Text = RawPageText.Text;
    });
    private void OnRestoreExplorerClick(object sender, RoutedEventArgs args) => TrackExplorerEditAsync(async () =>
    {
        if (viewModel.Run is not { } run) return;
        GatewayResult<SavedExplorerViewDto?> result = await workspace.LoadExplorerViewAsync(run.RunId, workbenchLifetime.Token);
        if (viewModel.Run?.RunId != run.RunId) return;
        if (!result.IsSuccess) { RawPageText.Text = result.Errors[0].UserMessage; return; }
        if (result.Value is null) { RawPageText.Text = "当前 Run 尚无保存的 Explorer 视图。"; return; }
        if (explorerViewDirty && !await ConfirmExplorerReplacementAsync()) return;
        if (viewModel.Run?.RunId != run.RunId) return;
        ExplorerViewDefinitionDto definition = result.Value.Definition;
        updatingRawControls = true; ExplorerModeBox.SelectedIndex = definition.Mode == "manual" ? 2 : 0; explorerDemoMode = false; RawWellBox.SelectedItem = definition.Well; updatingRawControls = false;
        explorerDefinition = definition; explorerViewDirty = false; UpdateExplorerMode();
        if (definition.Mode == "manual") await RebuildExplorerAsync(workbenchLifetime.Token); else await LoadRawPageAsync(definition.PageOffset);
    });

    private static string? SelectedId(ComboBox chooser) => (chooser.SelectedItem as ComboBoxItem)?.Tag as string;
    private static int[] SelectDefinitionRows(ExplorerDataModel data, ExplorerViewDefinitionDto definition) =>
        data.SelectRows(definition.FilterColumn, definition.FilterMinimum, definition.FilterMaximum, definition.SortColumn, definition.SortDescending);
    private static ExplorerViewDefinitionDto DefaultDefinition(Guid run, string? hash, string well, bool manual, IReadOnlyList<ExplorerManualPointDto>? points = null) =>
        new(run, hash, well, manual ? "manual" : "raw", "Line2D", manual ? "x" : "time", manual ? "y" : "fam", manual ? "z" : "rox", "Linear", "Linear", "Linear", [], [], [], points ?? []);
    private static ExplorerViewDefinitionDto CopyDefinition(ExplorerViewDefinitionDto value, string? x = null, string? y = null, string? z = null, string? kind = null, string? xs = null, string? ys = null, string? zs = null,
        IReadOnlyList<ExplorerDerivedColumnDto>? derived = null, IReadOnlyList<ExplorerColumnLabelDto>? labels = null, IReadOnlyList<ExplorerCellOverrideDto>? edits = null, IReadOnlyList<ExplorerManualPointDto>? manual = null,
        string? secondary = null, string? error = null, bool replaceSeries = false, string? well = null,
        string? filter = null, double? low = null, double? high = null, string? sort = null, bool descending = false, bool replaceFilter = false, int? offset = null) =>
        new(value.RunId, value.SourceSha256, well ?? value.Well, value.Mode, kind ?? value.PlotKind, x ?? value.XColumn, y ?? value.YColumn, z ?? value.ZColumn,
            xs ?? value.XScale, ys ?? value.YScale, zs ?? value.ZScale, derived ?? value.DerivedColumns, labels ?? value.Labels, edits ?? value.Overrides, manual ?? value.ManualPoints,
            secondaryYColumn: replaceSeries ? secondary : value.SecondaryYColumn, errorYColumn: replaceSeries ? error : value.ErrorYColumn,
            filterColumn: replaceFilter ? filter : value.FilterColumn, filterMinimum: replaceFilter ? low : value.FilterMinimum,
            filterMaximum: replaceFilter ? high : value.FilterMaximum, sortColumn: replaceFilter ? sort : value.SortColumn,
            sortDescending: replaceFilter ? descending : value.SortDescending, pageOffset: offset ?? value.PageOffset);
    private sealed record ExplorerPreparedRender(Guid RequestId, string Json, PlotSpec Spec, PlotDataSnapshot Data);
    [Microsoft.UI.Xaml.Data.Bindable]
    public sealed class ExplorerGridRow
    {
        public int SourceIndex { get; }
        public string[] Cells { get; }
        public ExplorerGridRow(ExplorerDataModel model, int row)
        {
            SourceIndex = row; Cells = new string[12]; Array.Fill(Cells, "");
            for (int column = 0; column < model.Columns.Count; column++) Cells[column] = model.Value(model.Columns[column].Id, row)?.ToString("G10", CultureInfo.InvariantCulture) ?? "缺值";
        }
    }
    // ListView requests only realized rows; never allocate a visual/cell string array for every raw point.
    private sealed class ExplorerRows(ExplorerDataModel model, int[] rows) : IList
    {
        public int Count => rows.Length; public bool IsReadOnly => true; public bool IsFixedSize => true; public bool IsSynchronized => false; public object SyncRoot => this;
        public object? this[int index] { get => new ExplorerGridRow(model, rows[index]); set => throw new NotSupportedException(); }
        public IEnumerator GetEnumerator() { for (int i = 0; i < rows.Length; i++) yield return this[i]; }
        public int IndexOf(object? value) => value is ExplorerGridRow row ? Array.IndexOf(rows, row.SourceIndex) : -1;
        public bool Contains(object? value) => IndexOf(value) >= 0;
        public void CopyTo(Array array, int index) { for (int i = 0; i < Count; i++) array.SetValue(this[i], index + i); }
        public int Add(object? value) => throw new NotSupportedException(); public void Clear() => throw new NotSupportedException(); public void Insert(int index, object? value) => throw new NotSupportedException(); public void Remove(object? value) => throw new NotSupportedException(); public void RemoveAt(int index) => throw new NotSupportedException();
    }
}
