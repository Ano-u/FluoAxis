namespace FluoAxis.App;

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FluoAxis.Contracts;
using FluoAxis.Infrastructure;
using FluoAxis.PlotProtocol;
using FluoAxis.Presentation;
using FluoAxis.Visualization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Windows.System;
using CoreVirtualKeyStates = Windows.UI.Core.CoreVirtualKeyStates;

public sealed partial class MainWindow : Window, IDisposable
{
    private PlotHostWebViewAdapter? plotHost;
    private readonly WorkbenchViewModel viewModel;
    private readonly DesktopWorkspace workspace;
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
    private CancellationTokenSource? activeWorkbenchOperation;
    private string activeStage = "import";
    private ImportPreviewDto? displayedPreview;
    private readonly Dictionary<string, WellEditor> wellEditors = new(StringComparer.Ordinal);
    private bool disposed;
    private bool uiInitialized;
    private bool refreshQueued;
    private Task initializationTask = Task.CompletedTask;
    private Task activeOperationTask = Task.CompletedTask;
    private readonly List<Task> exportTasks = [];
    private readonly List<Task> plotTasks = [];
    private bool closeRequested;
    private bool closeAllowed;
    private bool workspaceAvailable;
    private bool projectOperationBusy;
    private int projectPageOffset;
    private int projectPageCount;
    private const int ProjectPageSize = 50;
    private RunEvidenceDto? displayedEvidence;
    private readonly PlateDraftEditor plateDraft = new();
    private readonly Dictionary<string, ToggleButton> wellButtons = new(StringComparer.Ordinal);
    private bool updatingDraftControls;
    private string mappingBaseline = string.Empty;
    private bool draftDialogOpen;
    private bool abandonDraftOnClose;
    private RunSnapshotDto? renderedSnapshot;
    private IReadOnlyList<SampleAnalysisDto>? renderedSamples;
    private string renderedStage = string.Empty;
    private string scrollStage = string.Empty;
    private Guid? scrollRunId;
    private Guid? reportRunId;
    private int? reportRevision;
    private Guid? projectHeaderRunId;

    public MainWindow(WorkbenchViewModel viewModel, DesktopWorkspace workspace, string? launchArguments = null)
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this.workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        string? benchmarkMode = Environment.GetEnvironmentVariable("FLUOAXIS_SPIKE_BENCHMARK");
        bool benchmark2D = string.Equals(benchmarkMode, "2d", StringComparison.OrdinalIgnoreCase)
            || launchArguments?.Contains("--benchmark-25k-2d", StringComparison.Ordinal) == true;
        bool benchmark3D = string.Equals(benchmarkMode, "3d", StringComparison.OrdinalIgnoreCase)
            || launchArguments?.Contains("--benchmark-25k-3d", StringComparison.Ordinal) == true;
        fixturePointCount = benchmark2D || benchmark3D ? 25_000 : 6;
        initialKind = benchmark2D ? PlotKind.Scatter2D : PlotKind.Scatter3D;
        InitializeComponent();
        InitializeImportControls();
        uiInitialized = true;
        RootSurface.ActualThemeChanged += OnWorkbenchThemeChanged;
        primaryPaletteCallback = PalettePrimary.RegisterPropertyChangedCallback(Border.BackgroundProperty,
            (_, _) => OnWorkbenchThemeChanged(RootSurface, EventArgs.Empty));
        surfacePaletteCallback = PaletteAccentSoft.RegisterPropertyChangedCallback(Border.BackgroundProperty,
            (_, _) => OnWorkbenchThemeChanged(RootSurface, EventArgs.Empty));
        foreach (GuidedStageItem stage in viewModel.GuidedStages)
            CompactStageChooser.Items.Add(new ComboBoxItem { Content = StageLabel(stage.Key), Tag = stage.Key });
        BatchRoleBox.Items.Add(new ComboBoxItem { Content = "保持原角色" });
        foreach (RoleChoice role in RoleChoices) BatchRoleBox.Items.Add(new ComboBoxItem { Content = role.Label, Tag = role.Value });
        BatchRoleBox.SelectedIndex = 0;
        FamColumnsBox.TextChanged += OnMappingDraftChanged;
        RoxColumnsBox.TextChanged += OnMappingDraftChanged;
        FamHeaderBox.ValueChanged += OnMappingNumberChanged;
        FamTimeBox.ValueChanged += OnMappingNumberChanged;
        RoxHeaderBox.ValueChanged += OnMappingNumberChanged;
        RoxTimeBox.ValueChanged += OnMappingNumberChanged;
        InitializeExplorerControls(benchmark2D || benchmark3D);
        Activated += OnActivated;
        Closed += OnClosed;
        AppWindow.Closing += OnAppWindowClosing;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        if (benchmark2D || benchmark3D)
        {
            viewModel.SelectArea("explorer");
            PrimaryNavigation.SelectedItem = ExplorerNavigationItem;
            ShowArea("explorer");
        }
        else
        {
            viewModel.SelectArea("workbench");
            PrimaryNavigation.SelectedItem = PrimaryNavigation.MenuItems[0];
        }
        projectOperationBusy = true;
        initializationTask = InitializeWorkbenchAsync();
    }

    private async Task InitializeWorkbenchAsync()
    {
        try
        {
            await workspace.Ready.ConfigureAwait(true);
            workbenchLifetime.Token.ThrowIfCancellationRequested();
            workspaceAvailable = true;
            await RefreshPlateProfilesAsync(workbenchLifetime.Token);
            await RefreshProjectListAsync(0, workbenchLifetime.Token);
            if (!workspaceAvailable) return;
            if (ProjectChooser.Items.Count > 0 && ProjectChooser.Items[0] is ComboBoxItem { Tag: ProjectSnapshotDto project })
            {
                ProjectChooser.SelectedIndex = 0;
                if (await viewModel.LoadRunAsync(project.RunId, workbenchLifetime.Token)) SelectRestoredStage();
            }
            else viewModel.ShowEmptyWorkspace();
            UpdateWorkbenchFromViewModel();
        }
        catch (OperationCanceledException) when (workbenchLifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            workspaceAvailable = false;
            ShowWorkbenchError("SYSTEM_WORKBENCH_INITIALIZATION_FAILED", "工作区初始化失败，请重新启动并重试。");
        }
        finally
        {
            projectOperationBusy = false;
            UpdateWorkbenchFromViewModel();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (disposed || refreshQueued) return;
        refreshQueued = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            refreshQueued = false;
            UpdateWorkbenchFromViewModel();
        })) refreshQueued = false;
    }

    private bool IsWorkbenchBusy => viewModel.IsBusy || activeWorkbenchOperation is not null || projectOperationBusy || closeRequested || draftDialogOpen;

    private void UpdateWorkbenchFromViewModel()
    {
        if (!uiInitialized || disposed) return;
        ProjectNameText.Text = viewModel.ProjectName;
        ProjectTab.Text = viewModel.ProjectName;
        if (projectHeaderRunId != viewModel.Run?.RunId)
        {
            projectHeaderRunId = viewModel.Run?.RunId;
            ProjectToolsExpander.IsExpanded = false;
        }
        RunStateText.Text = $"{StateLabel(viewModel.RunState)} · revision {viewModel.Run?.Revision ?? 0}";
        MethodText.Text = viewModel.MethodIdentity;
        MethodHashText.Text = viewModel.Run is { } run ? $"Method SHA-256\n{run.MethodSnapshotHash}" : string.Empty;
        CompactRunContext.Text = $"{viewModel.ProjectName} · {StateLabel(viewModel.RunState)} · revision {viewModel.Run?.Revision ?? 0}\n{viewModel.MethodIdentity} · 科学批准仍为草案";
        WorkbenchProgress.IsActive = IsWorkbenchBusy;
        WorkbenchProgress.Visibility = IsWorkbenchBusy ? Visibility.Visible : Visibility.Collapsed;
        CancelOperationButton.IsEnabled = activeWorkbenchOperation is { IsCancellationRequested: false };
        CancelDeliveryButton.IsEnabled = CancelOperationButton.IsEnabled;
        CancelOperationButton.Visibility = activeWorkbenchOperation is null ? Visibility.Collapsed : Visibility.Visible;
        CancelDeliveryButton.Visibility = CancelOperationButton.Visibility;
        NewProjectButton.IsEnabled = workspaceAvailable && !IsWorkbenchBusy;
        NewProjectNameBox.IsEnabled = NewProjectButton.IsEnabled;
        OpenProjectButton.IsEnabled = workspaceAvailable && !IsWorkbenchBusy && ProjectChooser.Items.Count > 0;
        ProjectChooser.IsEnabled = OpenProjectButton.IsEnabled;
        PreviousProjectsButton.IsEnabled = workspaceAvailable && !IsWorkbenchBusy && projectPageOffset > 0;
        NextProjectsButton.IsEnabled = workspaceAvailable && !IsWorkbenchBusy && projectPageCount == ProjectPageSize;
        OpenPackageButton.IsEnabled = workspaceAvailable && !IsWorkbenchBusy;
        bool canDeliver = workspaceAvailable && !IsWorkbenchBusy && viewModel.Run is not null;
        SavePackageButton.IsEnabled = canDeliver;
        PreviewReportButton.IsEnabled = canDeliver;
        ExportHtmlButton.IsEnabled = canDeliver;
        ExportXlsxButton.IsEnabled = canDeliver;
        ExportPdfButton.IsEnabled = canDeliver;
        ReportRunText.Text = viewModel.Run is { } reportRun
            ? $"{viewModel.ProjectName} · {StateLabel(reportRun.DomainState)} · revision {reportRun.Revision}\n{viewModel.MethodIdentity}"
            : "请先新建或打开一个项目。";
        if (reportRunId != viewModel.Run?.RunId || reportRevision != viewModel.Run?.Revision)
        {
            reportRunId = viewModel.Run?.RunId;
            reportRevision = viewModel.Run?.Revision;
            ReportDetailsText.Text = "尚未读取当前项目的报告快照。";
            ReportPreviewSummary.Text = "当前运行尚未读取快照。导出时将验证并冻结已提交内容。";
            DeliveryStatusText.Text = string.Empty;
        }
        if (viewModel.HasError)
        {
            GlobalInfoBar.Title = viewModel.ErrorCode ?? "SYSTEM_ERROR";
            GlobalInfoBar.Message = BuildErrorMessage();
            GlobalInfoBar.Severity = InfoBarSeverity.Error;
            GlobalInfoBar.IsOpen = true;
        }

        // Read-only navigation remains available for reviewing failed and
        // completed stages. Only the stage's mutation action consumes
        // AllowedActions, so an INVALID Run cannot hide its diagnostic history.
        foreach (Button button in StageButtons()) button.IsEnabled = viewModel.Run is not null;
        BindImportPreview();
        BindStoredEvidence();
        BindRawExplorerSource();
        UpdateStageSurface();
        UpdateDraftStatus();
        UpdateSmartControls();
    }

    private string BuildErrorMessage()
    {
        string message = viewModel.ErrorMessage ?? "操作失败。";
        if (!string.IsNullOrWhiteSpace(viewModel.RecommendedAction)) message = $"{message} {viewModel.RecommendedAction}";
        return viewModel.CorrelationId is { } correlation ? $"{message}\n关联编号：{correlation}" : message;
    }

    private IEnumerable<Button> StageButtons() =>
        [ImportStageButton, MappingStageButton, PlateStageButton, CalibrationStageButton, ControlStageButton, AnalysisStageButton, ResultsStageButton];

    private static string StageLabel(string key) => key switch
    {
        "import" => "导入预览", "mapping" => "数据映射", "plate" => "孔位与重复孔", "calibration" => "校准",
        "control" => "对照质控", "analysis" => "样本分析", "results" => "结果与追溯", _ => "工作台",
    };

    private void UpdateStageNavigation()
    {
        int number = 0;
        foreach (Button button in StageButtons())
        {
            string key = (string)button.Tag;
            GuidedStageProgress progress = viewModel.StageProgress(key);
            string symbol = progress.State switch { "complete" => "✓", "failed" => "!", "available" => "○", _ => "·" };
            string current = key == activeStage ? "当前 · " : string.Empty;
            ++number;
            button.Style = (Style)Application.Current.Resources["WorkbenchStageButtonStyle"];
            bool selected = key == activeStage;
            button.Background = WorkbenchBrush(selected ? "AccentSoft" : "SubtleSurface");
            button.BorderBrush = WorkbenchBrush(selected ? "SelectionBorder" : "SubtleSurface");
            button.BorderThickness = new Thickness(selected ? 2 : 1);
            foreach (string state in new[] { "PointerOver", "Pressed" })
            {
                button.Resources[$"ButtonBackground{state}"] = WorkbenchBrush(selected ? "AccentSoft" : "Surface");
                button.Resources[$"ButtonForeground{state}"] = WorkbenchBrush("PrimaryText");
                button.Resources[$"ButtonBorderBrush{state}"] = WorkbenchBrush("SelectionBorder");
            }
            var content = new Grid { ColumnSpacing = 10, VerticalAlignment = VerticalAlignment.Center };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.Children.Add(new TextBlock { Text = number.ToString("D2", CultureInfo.InvariantCulture), FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Top,
                Foreground = WorkbenchBrush(selected ? "PrimaryText" : "SecondaryText") });
            var label = new StackPanel { Spacing = 4 };
            label.Children.Add(new TextBlock { Text = StageLabel(key), FontSize = 14,
                FontWeight = selected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = WorkbenchBrush("PrimaryText"), TextWrapping = TextWrapping.Wrap });
            label.Children.Add(new TextBlock { Text = $"{symbol} {current}{progress.Label}", FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = WorkbenchBrush(progress.State == "failed" ? "Danger" : progress.State == "complete" ? "Success" : "SecondaryText") });
            Grid.SetColumn(label, 1);
            content.Children.Add(label);
            button.Content = content;
            AutomationProperties.SetName(button, $"步骤 {number} {StageLabel(key)}，{current}{progress.Label}");
        }
        foreach (object item in CompactStageChooser.Items)
            if (item is ComboBoxItem { Tag: string key } option && key == activeStage)
            {
                CompactStageChooser.SelectedItem = option;
                break;
            }
    }

    private void OnCompactStageChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!uiInitialized || CompactStageChooser.SelectedItem is not ComboBoxItem { Tag: string stage } || stage == activeStage) return;
        activeStage = stage;
        UpdateStageSurface();
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (!uiInitialized) return;
        double width = args.NewSize.Width;
        bool compact = width < 900;
        BrandHeader.Padding = new Thickness(compact ? 56 : 24, 16, 24, 16);
        bool contextVisible = width >= 1220;
        StageRail.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        WorkbenchSurface.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : 198);
        RunContextPanel.Visibility = contextVisible ? Visibility.Visible : Visibility.Collapsed;
        WorkbenchSurface.ColumnDefinitions[2].Width = new GridLength(contextVisible ? 238 : 0);
        CompactStageChooser.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        CompactRunContext.Visibility = contextVisible ? Visibility.Collapsed : Visibility.Visible;
        StageWorkspace.Padding = new Thickness(compact ? 16 : 24);
        PrimaryNavigation.PaneDisplayMode = compact ? NavigationViewPaneDisplayMode.LeftMinimal : NavigationViewPaneDisplayMode.LeftCompact;
        bool narrowProject = width < 760;
        Grid.SetRow(ProjectPaginationPanel, narrowProject ? 1 : 0);
        Grid.SetColumn(ProjectPaginationPanel, narrowProject ? 0 : 1);
        Grid.SetRow(NewProjectForm, narrowProject ? 2 : 1);
        Grid.SetRow(ProjectFilesPanel, narrowProject ? 3 : 1);
        Grid.SetColumn(ProjectFilesPanel, narrowProject ? 0 : 1);
        ProjectManagementGrid.ColumnDefinitions[1].Width = narrowProject ? new GridLength(0) : GridLength.Auto;
        bool stackedChannels = width < 1480;
        Grid.SetRow(RoxMappingCard, stackedChannels ? 1 : 0);
        Grid.SetColumn(RoxMappingCard, stackedChannels ? 0 : 1);
        ChannelMappingGrid.ColumnDefinitions[1].Width = new GridLength(stackedChannels ? 0 : 1, stackedChannels ? GridUnitType.Pixel : GridUnitType.Star);
    }

    private string MappingDraftFingerprint() => JsonSerializer.Serialize(new
    {
        fam = FamCandidateBox.SelectedIndex, rox = RoxCandidateBox.SelectedIndex,
        fh = FamHeaderBox.Value.ToString("R", CultureInfo.InvariantCulture), ft = FamTimeBox.Value.ToString("R", CultureInfo.InvariantCulture),
        rh = RoxHeaderBox.Value.ToString("R", CultureInfo.InvariantCulture), rt = RoxTimeBox.Value.ToString("R", CultureInfo.InvariantCulture),
        fc = FamColumnsBox.Text, rc = RoxColumnsBox.Text,
        fs = FamSheetBox.Text, rs = RoxSheetBox.Text,
        fe = FamEndRowBox.Value.ToString("R", CultureInfo.InvariantCulture), re = RoxEndRowBox.Value.ToString("R", CultureInfo.InvariantCulture),
        options = ReadImportOptions(),
    });

    private bool HasUnsavedDraft => plateDraft.IsDirty || explorerViewDirty
        || (viewModel.CurrentPreview is not null && viewModel.CurrentEvidence?.Mapping is null && MappingDraftFingerprint() != mappingBaseline);

    private void OnMappingDraftChanged(object sender, TextChangedEventArgs args) => UpdateDraftStatus();
    private void OnMappingNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => UpdateDraftStatus();

    private void UpdateDraftStatus()
    {
        if (!uiInitialized || updatingDraftControls) return;
        DraftStatusText.Text = HasUnsavedDraft ? "● 有未提交编辑" : "✓ 已提交内容自动保存";
        DraftStatusText.Foreground = WorkbenchBrush(HasUnsavedDraft ? "Caution" : "SecondaryText");
    }

    private async Task<bool> ConfirmDiscardDraftAsync()
    {
        if (!HasUnsavedDraft) return true;
        draftDialogOpen = true;
        UpdateWorkbenchFromViewModel();
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = RootSurface.XamlRoot, Title = "保留当前未提交编辑？",
                Content = "切换项目或关闭窗口会丢弃尚未提交的数据映射、孔角色、样本信息和未保存的科研视图。已提交结果保存在工作区中。",
                PrimaryButtonText = "丢弃编辑并继续", CloseButtonText = "返回编辑", DefaultButton = ContentDialogButton.Close,
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch (Exception)
        {
            ShowWorkbenchError("SYSTEM_DRAFT_CONFIRMATION_FAILED", "无法打开编辑确认。当前草稿保持不变，请稍后重试。");
            return false;
        }
        finally { draftDialogOpen = false; UpdateWorkbenchFromViewModel(); }
    }

    private async Task RefreshProjectListAsync(int offset, CancellationToken cancellationToken)
    {
        GatewayResult<IReadOnlyList<ProjectSnapshotDto>> result = await workspace.ListProjectsAsync(cancellationToken, offset, ProjectPageSize);
        if (!result.IsSuccess)
        {
            workspaceAvailable = false;
            AppErrorDto error = result.Errors[0];
            ShowWorkbenchError(error.Code, $"{error.UserMessage} {error.RecommendedAction}");
            return;
        }
        projectPageOffset = offset;
        projectPageCount = result.Value!.Count;
        ProjectChooser.Items.Clear();
        foreach (ProjectSnapshotDto project in result.Value)
            ProjectChooser.Items.Add(new ComboBoxItem { Content = $"{project.ProjectName} · {StateLabel(project.RunDomainState)}", Tag = project });
        if (ProjectChooser.Items.Count > 0) ProjectChooser.SelectedIndex = 0;
        ProjectPageText.Text = $"第 {offset / ProjectPageSize + 1} 页 · {projectPageCount} 项";
    }

    private async void OnNewProjectClick(object sender, RoutedEventArgs args) => await TrackProjectOperationAsync("new");
    private async void OnOpenProjectClick(object sender, RoutedEventArgs args) => await TrackProjectOperationAsync("open");
    private async void OnPreviousProjectsClick(object sender, RoutedEventArgs args) => await TrackProjectOperationAsync("previous");
    private async void OnNextProjectsClick(object sender, RoutedEventArgs args) => await TrackProjectOperationAsync("next");

    private async Task TrackProjectOperationAsync(string action)
    {
        if (IsWorkbenchBusy || !workspaceAvailable) return;
        activeOperationTask = ProjectOperationAsync(action);
        await activeOperationTask;
    }

    private async Task ProjectOperationAsync(string action)
    {
        projectOperationBusy = true;
        GlobalInfoBar.IsOpen = false;
        UpdateWorkbenchFromViewModel();
        try
        {
            await Task.Yield();
            CancellationToken token = workbenchLifetime.Token;
            if ((action is "new" or "open") && !await ConfirmDiscardDraftAsync()) return;
            if (action == "new")
            {
                string name = NewProjectNameBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    ShowWorkbenchError("SYSTEM_PROJECT_NAME_REQUIRED", "请输入新项目名称。新建操作不会覆盖已有项目。");
                    return;
                }
                ResetDraftEditors();
                await viewModel.InitializeAsync(name, token);
                if (!viewModel.HasError)
                {
                    activeStage = "import";
                    await RefreshProjectListAsync(0, token);
                    SelectProjectInList(viewModel.Run?.RunId);
                    OperationText.Text = "新项目已创建并保存。请选择原始数据。";
                }
            }
            else if (action == "open" && ProjectChooser.SelectedItem is ComboBoxItem { Tag: ProjectSnapshotDto project })
            {
                ResetDraftEditors();
                if (await viewModel.LoadRunAsync(project.RunId, token))
                {
                    SelectRestoredStage();
                    OperationText.Text = "已打开保存的 Run 与计算证据；未重新执行分析。";
                }
            }
            else if (action == "previous") await RefreshProjectListAsync(Math.Max(0, projectPageOffset - ProjectPageSize), token);
            else if (action == "next") await RefreshProjectListAsync(projectPageOffset + ProjectPageSize, token);
        }
        catch (OperationCanceledException) when (workbenchLifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            workspaceAvailable = false;
            ShowWorkbenchError("SYSTEM_PROJECT_OPEN_FAILED", "项目操作未完成。请重新启动工作区后读取已保存项目。");
        }
        finally
        {
            projectOperationBusy = false;
            UpdateWorkbenchFromViewModel();
        }
    }

    private void SelectProjectInList(Guid? runId)
    {
        foreach (object item in ProjectChooser.Items)
            if (item is ComboBoxItem { Tag: ProjectSnapshotDto project } option && project.RunId == runId)
            {
                ProjectChooser.SelectedItem = option;
                return;
            }
    }

    private void SelectRestoredStage()
    {
        activeStage = viewModel.RunState switch
        {
            RunStateNames.New => "import", RunStateNames.Imported => "plate", RunStateNames.Mapped => "calibration",
            RunStateNames.CalibrationPassed => "control", RunStateNames.ReadyForAnalysis => "analysis",
            RunStateNames.CalibrationFailed => "calibration", RunStateNames.ControlQcFailed => "control",
            _ => "results",
        };
    }

    private void ResetDraftEditors()
    {
        updatingDraftControls = true;
        displayedPreview = null;
        displayedEvidence = null;
        smartSource = null;
        smartProposal = null;
        SmartOutcomePanel.Visibility = Visibility.Collapsed;
        BindScientificEvidence(null);
        FamCandidateBox.Items.Clear();
        RoxCandidateBox.Items.Clear();
        FamColumnsBox.Text = string.Empty;
        RoxColumnsBox.Text = string.Empty;
        FamSheetBox.Text = string.Empty; RoxSheetBox.Text = string.Empty;
        FamEndRowBox.Value = 0; RoxEndRowBox.Value = 0;
        RestoreImportOptions(null);
        WellEditors.Children.Clear();
        PlateOverview.Children.Clear();
        wellEditors.Clear();
        wellButtons.Clear();
        plateDraft.Load([]);
        BatchRoleBox.SelectedIndex = 0;
        BatchSampleCheck.IsChecked = false;
        BatchGroupCheck.IsChecked = false;
        BatchTargetCheck.IsChecked = false;
        BatchNotesCheck.IsChecked = false;
        BatchReplicateCheck.IsChecked = false;
        BatchSampleBox.Text = string.Empty;
        BatchGroupBox.Text = string.Empty;
        BatchTargetBox.Text = string.Empty;
        BatchNotesBox.Text = string.Empty;
        BatchReplicateBox.Value = 1;
        mappingBaseline = string.Empty;
        TraceChooser.Items.Clear();
        TraceDetailsText.Text = "尚无已保存的计算证据。";
        SourceDetailsText.Text = "尚未选择原始数据。";
        PreviewDetailsText.Text = string.Empty;
        updatingDraftControls = false;
        UpdateDraftStatus();
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!uiInitialized) return;
        string area = args.IsSettingsSelected ? "diagnostics" : (args.SelectedItemContainer?.Tag as string ?? "workbench");
        viewModel.SelectArea(area);
        ShowArea(area);
    }

    private void ShowArea(string area)
    {
        BrandHeader.Visibility = area == "explorer" ? Visibility.Collapsed : Visibility.Visible;
        ProjectHeader.Visibility = area == "explorer" ? Visibility.Collapsed : Visibility.Visible;
        if (area != "explorer")
        {
            ExplorerSettingsFlyout.Hide();
            ExplorerTableFlyout.Hide();
            explorerFocused = false;
        }
        WorkbenchSurface.Visibility = area == "workbench" ? Visibility.Visible : Visibility.Collapsed;
        ExplorerSurface.Visibility = area == "explorer" ? Visibility.Visible : Visibility.Collapsed;
        ReportsSurface.Visibility = area == "reports" ? Visibility.Visible : Visibility.Collapsed;
        PlaceholderSurface.Visibility = area == "diagnostics" ? Visibility.Visible : Visibility.Collapsed;
        if (area == "reports")
        {
            UpdateWorkbenchFromViewModel();
        }
        else if (area == "diagnostics")
        {
            PlaceholderTitle.Text = "设置与诊断";
            _ = TrackDeliveryAsync("diagnostics");
        }
        else if (area == "explorer")
        {
            UpdateExplorerLayout();
            BindRawExplorerSource();
            if (plotHost is null) TrackPlotOperation(InitializePlotHostAsync);
            else if (plotReady && !renderedPlotAvailable) TrackPlotOperation(RenderExplorerAfterActivationAsync);
        }
    }

    private void OnStageClick(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: string stage }) return;
        activeStage = stage;
        UpdateStageSurface();
    }

    private async void OnOpenPackageClick(object sender, RoutedEventArgs args) => await TrackDeliveryAsync("openPackage");
    private async void OnSavePackageClick(object sender, RoutedEventArgs args) => await TrackDeliveryAsync("savePackage");
    private async void OnPreviewReportClick(object sender, RoutedEventArgs args) => await TrackDeliveryAsync("preview");
    private async void OnExportHtmlClick(object sender, RoutedEventArgs args) => await TrackDeliveryAsync("html");
    private async void OnExportXlsxClick(object sender, RoutedEventArgs args) => await TrackDeliveryAsync("xlsx");
    private async void OnExportPdfClick(object sender, RoutedEventArgs args) => await TrackDeliveryAsync("pdf");

    private async Task TrackDeliveryAsync(string action)
    {
        if (IsWorkbenchBusy || !workspaceAvailable) return;
        activeOperationTask = DeliverAsync(action);
        await activeOperationTask;
    }

    private async Task DeliverAsync(string action)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(workbenchLifetime.Token);
        activeWorkbenchOperation = operation;
        GlobalInfoBar.IsOpen = false;
        OperationText.Text = "正在准备已保存内容…";
        DeliveryStatusText.Text = "正在处理…";
        UpdateWorkbenchFromViewModel();
        try
        {
            await Task.Yield();
            nint owner = WinRT.Interop.WindowNative.GetWindowHandle(this);
            CancellationToken token = operation.Token;
            if (action == "diagnostics")
            {
                await RenderDiagnosticsAsync(token);
            }
            else if (action == "support")
            {
                GatewayResult<bool>? result = await workspace.ExportSupportBundleAsync(owner, token);
                if (result is null) { PlaceholderText.Text = "已取消支持包导出。"; return; }
                if (!ShowDeliveryFailure(result)) return;
                PlaceholderText.Text = "技术支持包已保存，仅包含明示的技术字段。";
            }
            else if (action == "openPackage")
            {
                if (!await ConfirmDiscardDraftAsync()) return;
                GatewayResult<ProjectSnapshotDto>? result = await workspace.OpenProjectPackageAsync(owner, token);
                if (result is null) { OperationText.Text = "已取消打开工程包。"; return; }
                if (!ShowDeliveryFailure(result)) return;
                ResetDraftEditors();
                await RefreshProjectListAsync(0, token);
                if (await viewModel.LoadRunAsync(result.Value!.RunId, token))
                {
                    SelectProjectInList(result.Value.RunId);
                    SelectRestoredStage();
                    OperationText.Text = "已打开验证后的工程包，显示其中保存的 Run。";
                }
            }
            else if (viewModel.Run is { } run)
            {
                if (action == "savePackage")
                {
                    GatewayResult<ManagedProjectPackageDto>? result = await workspace.SaveProjectPackageAsync(owner, run.RunId, token);
                    if (result is null) { OperationText.Text = "已取消另存工程包。"; return; }
                    if (!ShowDeliveryFailure(result)) return;
                    ManagedProjectPackageDto receipt = result.Value!;
                    DeliveryStatusText.Text = $"工程包已保存：{receipt.DisplayName}\n{receipt.FileSizeBytes:N0} bytes\nSHA-256：{receipt.Sha256}";
                }
                else if (action == "preview")
                {
                    GatewayResult<ReportSnapshotDto> result = await workspace.CreateReportSnapshotAsync(run.RunId, token);
                    if (!ShowDeliveryFailure(result)) return;
                    ReportSnapshotDto snapshot = result.Value!;
                    ReportPreviewSummary.Text = $"已读取 revision {snapshot.Content.Evidence.Snapshot.Revision} · {snapshot.ReportId} · {snapshot.Content.Evidence.ProvenanceCompleteness}";
                    ReportDetailsText.Text = $"报告编号：{snapshot.ReportId}\n项目：{snapshot.Content.Evidence.ProjectName}\nRun revision：{snapshot.Content.Evidence.Snapshot.Revision}\n软件版本：{snapshot.Content.SoftwareVersion}\n生成时间：{snapshot.Content.CreatedUtc:O}\n快照 SHA-256：{snapshot.SnapshotSha256}\n证据状态：{snapshot.Content.Evidence.ProvenanceCompleteness}\n{snapshot.Content.ResearchUseNotice}";
                    DeliveryStatusText.Text = "快照已由后端冻结。导出操作会重新读取当前已提交内容，并返回实际输出的快照 hash。";
                }
                else
                {
                    GatewayResult<ReportExportDto>? result = await workspace.ExportReportAsync(owner, run.RunId, action, token);
                    if (result is null) { DeliveryStatusText.Text = "已取消报告导出。"; return; }
                    if (!ShowDeliveryFailure(result)) return;
                    ReportExportDto receipt = result.Value!;
                    DeliveryStatusText.Text = $"技术报告已保存：{receipt.DisplayName}\n格式：{receipt.Format} · {receipt.FileSizeBytes:N0} bytes\n文件 SHA-256：{receipt.Sha256}\n快照 SHA-256：{receipt.SnapshotSha256}";
                }
                OperationText.Text = "已完成保存内容的输出。";
            }
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            DeliveryStatusText.Text = "操作已取消。已提交的 Run 结果保持在工作区中。";
        }
        catch (Exception)
        {
            ShowWorkbenchError("SYSTEM_DELIVERY_FAILED", "输出或打开未完成。请检查格式、权限、磁盘空间；PDF 还需要本地 WebView2。原始 Run 不会由输出界面重算。");
        }
        finally { activeWorkbenchOperation = null; UpdateWorkbenchFromViewModel(); }
    }

    private bool ShowDeliveryFailure<T>(GatewayResult<T> result)
    {
        if (result.IsSuccess) return true;
        AppErrorDto error = result.Errors[0];
        ShowWorkbenchError(error.Code, $"{error.UserMessage} {error.RecommendedAction}\n关联编号：{result.CorrelationId}");
        DeliveryStatusText.Text = "操作未完成，请查看上方提示。";
        return false;
    }

    private void UpdateStageSurface()
    {
        bool hasSavedMapping = viewModel.CurrentEvidence?.Mapping is not null;
        int savedAssignmentCount = viewModel.CurrentEvidence?.Assignments.Count ?? 0;
        bool savedMappingReadOnly = hasSavedMapping && viewModel.Run?.AllowedActions.Contains(RunAction.ConfirmMapping) != true;
        bool savedPlateReadOnly = savedAssignmentCount > 0 && viewModel.Run?.AllowedActions.Contains(RunAction.AssignWells) != true;
        (StageTitle.Text, StageSubtitle.Text, PrimaryStageAction.Content) = activeStage switch
        {
            "import" => ("导入预览", "选择原始仪器文件并进行只读预览；此步骤不会产生正式判读。", "选择数据文件"),
            "mapping" when savedMappingReadOnly => ("通道与数据映射", "复核该 Run 已保存的 FAM / ROX 映射；当前阶段为只读。", "映射已保存 · 只读"),
            "mapping" => ("通道与数据映射", "明确确认 FAM / ROX 工作表、表头、时间列和每个孔列。", "确认数据映射"),
            "plate" when savedPlateReadOnly => ("孔位与重复孔", "复核该 Run 已保存的孔角色、Sample ID 和重复孔序号；当前阶段为只读。", "孔位分配已保存 · 只读"),
            "plate" => ("孔位与重复孔", "为每个已映射孔设置角色，明确 Sample ID 与重复孔序号。", "提交孔位分配"),
            "calibration" => ("校准", viewModel.Run?.MethodVersion == "1.1"
                ? "FAM：阳性对照平台均值；ROX：方法指定端点。随后独立验证各 NTC 孔。"
                : "按当前运行方法快照校准：先验证 100% Positive 平台，再独立验证各 NTC 孔。", "运行校准"),
            "control" => ("对照质控", "使用已持久化的校准结果评估 WT 与 LQC。", "运行对照质控"),
            "analysis" => ("样本分析", "每孔独立判读后检查重复孔一致性；冲突输出 INVALID。", "分析样本"),
            "results" => ("结果与追溯", "查看样本判读、逐孔数据与计算依据。", "判读已完成"),
            _ => ("科研工作台", "选择一个引导式分析阶段。", "继续"),
        };
        foreach (Button button in StageButtons()) button.FontWeight = Equals(button.Tag, activeStage) ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        ImportPanel.Visibility = activeStage == "import" ? Visibility.Visible : Visibility.Collapsed;
        MappingPanel.Visibility = activeStage == "mapping" ? Visibility.Visible : Visibility.Collapsed;
        PlatePanel.Visibility = activeStage == "plate" ? Visibility.Visible : Visibility.Collapsed;
        MappingGuidanceText.Text = hasSavedMapping
            ? "以下为该 Run 已保存的映射。行列编号从 1 开始；历史映射不会因重新打开而重新推断。"
            : "分别选择 FAM 与 ROX 数据区。候选区仅是建议，确认前请核对原始文件。下方行列编号从 1 开始。";
        PlateGuidanceText.Text = savedPlateReadOnly
            ? "以下孔角色与样本信息来自已保存的分配。当前只读，可定位各孔进行复核。"
            : "为每个已映射孔明确选择角色。样本孔必须填写 Sample ID 和组内重复孔序号；不自动推断角色。";
        StageResultsPanel.Visibility = activeStage is "calibration" or "control" or "analysis" or "results" ? Visibility.Visible : Visibility.Collapsed;
        bool canEditMapping = !IsWorkbenchBusy && viewModel.CanConfirmMapping;
        foreach (Control control in new Control[] { FamCandidateBox, FamHeaderBox, FamTimeBox, FamColumnsBox, RoxCandidateBox, RoxHeaderBox, RoxTimeBox, RoxColumnsBox,
            FamSheetBox, RoxSheetBox, FamEndRowBox, RoxEndRowBox, ImportEncodingBox, ImportDelimiterBox, ImportCultureBox, ImportTimeUnitBox })
            control.IsEnabled = canEditMapping;
        RefreshWellAppearance();
        PrimaryStageAction.IsEnabled = workspaceAvailable && !IsWorkbenchBusy && (activeStage switch
        {
            "import" => viewModel.CanImport,
            "mapping" => viewModel.CanConfirmMapping && viewModel.CurrentPreview is not null,
            "plate" => viewModel.CanAssignWells && wellEditors.Count > 0,
            "calibration" => viewModel.CanRunCalibration,
            "control" => viewModel.CanRunControlQc,
            "analysis" => viewModel.CanAnalyzeSamples,
            _ => false,
        });
        StageStateTitle.Text = IsWorkbenchBusy ? "正在处理，请稍候…" : $"当前运行：{StateLabel(viewModel.RunState)}";
        bool runInvalid = viewModel.RunState is RunStateNames.RunInvalid or RunStateNames.CalibrationFailed or RunStateNames.ControlQcFailed;
        StageStateTitle.Foreground = WorkbenchBrush(runInvalid ? "Danger" : "PrimaryText");
        StageStatusCard.BorderBrush = WorkbenchBrush(runInvalid ? "Danger" : "Border");
        StageStateDetail.Text = activeStage switch
        {
            "import" => viewModel.CurrentSource is null ? "尚未导入原始数据。支持 XLSX、XLS、CSV、TSV、TXT。" : "托管源文件已就绪，可在数据映射页确认通道。",
            "mapping" when hasSavedMapping => savedMappingReadOnly ? "已保存 FAM / ROX 映射，当前只读；无需重新导入。" : "已恢复保存的 FAM / ROX 映射，可按后端允许的操作继续。",
            "mapping" => viewModel.CurrentPreview is null ? "先导入并预览原始文件。" : "两个通道必须覆盖相同的逻辑孔；候选区不会自动成为正式映射。",
            "plate" when savedPlateReadOnly => $"已保存 {savedAssignmentCount} 个孔的角色与样本信息，当前只读。",
            "plate" => wellEditors.Count == 0 ? "先确认 FAM / ROX 数据映射。" : $"已映射 {wellEditors.Count} 个孔。请为每个孔明确设置角色。",
            "calibration" => viewModel.Run?.CalibrationSummary is { } calibration ? $"校准状态：{StatusLabel(calibration.Status)}" : "完整提交孔位分配后可运行校准。",
            "control" => viewModel.Run?.ControlSummary is { } control ? $"对照质控状态：{StatusLabel(control.Status)}" : "校准通过后可评估 WT 和 LQC。",
            "analysis" or "results" => viewModel.Run?.AnalysisSummary is { } analysis ? $"分析状态：{StatusLabel(analysis.Status)}；样本 {analysis.SampleCount}，阳性 {analysis.PositiveCount}，阴性 {analysis.NegativeCount}，无效 {analysis.InvalidCount}。" : "对照质控通过后可进行样本分析。",
            _ => "可执行操作由后端 Run 快照控制。",
        };
        RenderStageResults();
        RenderIssues();
        TracePanel.Visibility = activeStage is "control" or "analysis" or "results" ? Visibility.Visible : Visibility.Collapsed;
        ScientificEvidencePanel.Visibility = activeStage is "calibration" or "control" or "analysis" or "results" ? Visibility.Visible : Visibility.Collapsed;
        UpdateStageNavigation();
        UpdatePlateActions();
        UpdateScientificPointActions();
        if (scrollRunId != viewModel.Run?.RunId || scrollStage != activeStage)
        {
            scrollRunId = viewModel.Run?.RunId;
            scrollStage = activeStage;
            StageContentScroll.ChangeView(null, 0, null, disableAnimation: true);
        }
    }

    private void BindStoredEvidence()
    {
        RunEvidenceDto? evidence = viewModel.CurrentEvidence;
        if (ReferenceEquals(displayedEvidence, evidence)) return;
        RunEvidenceDto? previous = displayedEvidence;
        displayedEvidence = evidence;
        BindScientificEvidence(evidence);
        if (evidence is null) return;
        // A failed command can refresh a new DTO at the same revision. Keep
        // the user's rejected draft intact so they can correct it in place.
        bool rebuildDraft = previous is null
            || previous.Snapshot.RunId != evidence.Snapshot.RunId
            || previous.Snapshot.Revision != evidence.Snapshot.Revision
            || !SameMapping(previous.Mapping, evidence.Mapping);
        if (rebuildDraft && evidence.Mapping is { } mapping)
        {
            RestoreImportOptions(mapping.Options);
            FamSheetBox.Text = mapping.Fam.SheetName; RoxSheetBox.Text = mapping.Rox.SheetName;
            FamEndRowBox.Value = mapping.Fam.EndRowIndex is { } famEnd ? famEnd + 1 : 0;
            RoxEndRowBox.Value = mapping.Rox.EndRowIndex is { } roxEnd ? roxEnd + 1 : 0;
            if (viewModel.CurrentPreview is null)
            {
                FamCandidateBox.Items.Clear();
                RoxCandidateBox.Items.Clear();
                AddStoredMapping(FamCandidateBox, mapping.Fam);
                AddStoredMapping(RoxCandidateBox, mapping.Rox);
                PreviewDetailsText.Text = "显示该 Run 已保存的数据映射；没有重新解析或猜测源文件。";
            }
            BuildWellEditors(mapping.Fam.WellColumns, evidence.Assignments);
            mappingBaseline = MappingDraftFingerprint();
        }
        string? selectedTraceId = (TraceChooser.SelectedItem as ComboBoxItem)?.Tag as string;
        TraceChooser.Items.Clear();
        foreach (CalculationTraceDto trace in evidence.Traces)
            TraceChooser.Items.Add(new ComboBoxItem { Content = $"{trace.Well} · {trace.Channel} · {trace.TraceId}", Tag = trace.TraceId });
        if (TraceChooser.Items.Count > 0)
        {
            TraceChooser.SelectedItem = TraceChooser.Items.Cast<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, selectedTraceId))
                ?? TraceChooser.Items[0];
        }
        else TraceDetailsText.Text = "未保存计算 trace。当前页面仅展示已存储的阶段摘要，不补算中间值。";
    }

    private static bool SameMapping(RunMappingDto? left, RunMappingDto? right) =>
        ReferenceEquals(left, right) || (left is not null && right is not null
            && left.Options == right.Options && SameChannelMapping(left.Fam, right.Fam) && SameChannelMapping(left.Rox, right.Rox));

    private static bool SameChannelMapping(ChannelRegionMappingDto left, ChannelRegionMappingDto right) =>
        left.Channel == right.Channel && left.SheetName == right.SheetName
        && left.HeaderRowIndex == right.HeaderRowIndex && left.TimeColumnIndex == right.TimeColumnIndex
        && left.EndRowIndex == right.EndRowIndex
        && left.WellColumns.SequenceEqual(right.WellColumns);

    private static void AddStoredMapping(ComboBox chooser, ChannelRegionMappingDto mapping)
    {
        var candidate = new RegionCandidateDto(mapping.SheetName, mapping.HeaderRowIndex, mapping.TimeColumnIndex, mapping.WellColumns, mapping.Channel);
        chooser.Items.Add(new ComboBoxItem { Content = $"已保存 · {mapping.SheetName} · {mapping.Channel}", Tag = candidate });
        chooser.SelectedIndex = 0;
    }

    private void OnTraceSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!uiInitialized || TraceChooser.SelectedItem is not ComboBoxItem { Tag: string traceId }) return;
        CalculationTraceDto? trace = viewModel.CurrentEvidence?.Traces.FirstOrDefault(item => item.TraceId == traceId);
        if (trace is null) { TraceDetailsText.Text = "所选计算证据未保存。"; return; }
        var lines = new List<string>
        {
            $"孔位：{trace.Well} / {trace.Channel} · 数值计算状态：{StatusLabel(trace.FinalStatus)}",
            $"Trace ID：{trace.TraceId}", $"算法版本：{trace.Algorithm}",
            $"Method SHA-256：{trace.MethodSnapshotHash}",
            $"源点索引（从 0 开始）：{trace.SourcePointIndex?.ToString(CultureInfo.InvariantCulture) ?? "未保存"}",
            "中间值（按保存顺序，完整 double 精度）：",
        };
        lines.AddRange(trace.IntermediateValues.Select(value => $"{TraceValueLabel(value.Name)} [{value.Name}] = {FullPrecision(value.Value)}"));
        lines.Add($"比较运算符：{trace.ComparisonOperator}；原始阈值：{FullPrecision(trace.ThresholdRaw)}");
        lines.Add($"原始结果：{(trace.ResultRaw is { } result ? FullPrecision(result) : "未保存")}");
        lines.Add($"工程证据完整性：{trace.ProvenanceCompleteness}；不代表科学方法批准。");
        if (trace.Provenance is { } provenance)
        {
            lines.Add($"输入 SHA-256：{provenance.Identity.InputSnapshotSha256}\n映射 SHA-256：{provenance.Identity.MappingSha256}\n源文件 SHA-256：{provenance.Identity.SourceSha256}");
            foreach (SourcePointEvidenceDto point in provenance.SourcePoints)
                lines.Add($"{point.Channel} · {point.SheetName} · 源行列（0起） {point.SourceRowIndex}/{point.SourceColumnIndex} · 点 {point.PointIndex} · ticks {point.ElapsedTicks} · Raw RFU {FullPrecision(point.RawRfu)}");
            lines.Add($"已保存错误码：{(provenance.ErrorCodes.Count == 0 ? "无" : string.Join("、", provenance.ErrorCodes))}");
        }
        else lines.Add("历史记录缺少输入身份或各通道端点坐标，界面没有推断或重算缺失内容。");
        TraceDetailsText.Text = string.Join("\n", lines);
    }

    private static string FullPrecision(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
    private static string TraceValueLabel(string name) => name switch
    {
        "F_FAM_120" => "FAM 原始终点 RFU", "F_ROX_120" => "ROX 原始终点 RFU",
        "Fb_FAM" => "FAM 背景值 Fb", "Fb_ROX" => "ROX 背景值 Fb", "Fm_FAM" => "FAM 最大值 Fm", "Fm_ROX" => "ROX 最大值 Fm",
        "Y_FAM" => "FAM 归一化值", "Y_ROX" => "ROX 归一化值", "WT_Y_FAM_mean" => "WT FAM 参考均值", "WT_Y_ROX_mean" => "WT ROX 参考均值",
        _ => "已存储中间值",
    };

    private void BindImportPreview()
    {
        if (viewModel.CurrentSource is { } source)
            SourceDetailsText.Text = $"源文件：{source.OriginalFileName}\n大小：{source.FileSizeBytes:N0} bytes\nSHA-256：{source.Sha256}";
        else
            SourceDetailsText.Text = "请选择完整的 FAM / ROX 仪器导出文件。";
        if (ReferenceEquals(displayedPreview, viewModel.CurrentPreview)) return;
        displayedPreview = viewModel.CurrentPreview;
        FamCandidateBox.Items.Clear();
        RoxCandidateBox.Items.Clear();
        if (displayedPreview is null)
        {
            PreviewDetailsText.Text = "尚无候选数据区。";
            return;
        }
        PreviewDetailsText.Text = $"格式：{displayedPreview.DetectedFormat}；工作表：{string.Join("、", displayedPreview.SheetNames)}；候选区：{displayedPreview.CandidateRegionCount}。";
        for (int index = 0; index < displayedPreview.CandidateRegions.Count; index++)
        {
            RegionCandidateDto candidate = displayedPreview.CandidateRegions[index];
            string label = $"{index + 1}. {candidate.SheetName} · 表头 {candidate.HeaderRowIndex + 1} · 孔列 {candidate.WellColumns.Count} · 建议 {candidate.SuggestedChannel ?? "未识别"}";
            FamCandidateBox.Items.Add(new ComboBoxItem { Content = label, Tag = candidate });
            RoxCandidateBox.Items.Add(new ComboBoxItem { Content = label, Tag = candidate });
        }
        mappingBaseline = MappingDraftFingerprint();
    }

    private void OnFamCandidateChanged(object sender, SelectionChangedEventArgs args) => LoadCandidate(FamCandidateBox, FamHeaderBox, FamTimeBox, FamColumnsBox);
    private void OnRoxCandidateChanged(object sender, SelectionChangedEventArgs args) => LoadCandidate(RoxCandidateBox, RoxHeaderBox, RoxTimeBox, RoxColumnsBox);

    private void LoadCandidate(ComboBox chooser, NumberBox header, NumberBox time, TextBox columns)
    {
        if (!uiInitialized || chooser.SelectedItem is not ComboBoxItem { Tag: RegionCandidateDto candidate }) return;
        (ReferenceEquals(chooser, FamCandidateBox) ? FamSheetBox : RoxSheetBox).Text = candidate.SheetName;
        header.Value = candidate.HeaderRowIndex is { } row ? row + 1 : double.NaN;
        time.Value = candidate.TimeColumnIndex is { } column ? column + 1 : double.NaN;
        columns.Text = string.Join(", ", candidate.WellColumns.Select(item => $"{item.ColumnIndex + 1}={item.Well}"));
        UpdateStageSurface();
        UpdateDraftStatus();
    }

    private ChannelRegionMappingDto ReadMapping(string channel, ComboBox chooser, NumberBox header, NumberBox time, TextBox columns)
    {
        string sheet = (channel == ChannelNames.Fam ? FamSheetBox : RoxSheetBox).Text.Trim();
        if (string.IsNullOrWhiteSpace(sheet)) throw new ArgumentException("Enter the source sheet name.");
        double end = (channel == ChannelNames.Fam ? FamEndRowBox : RoxEndRowBox).Value;
        int? endRow = end == 0 ? null : ReadPositiveInteger(end) - 1;
        int headerIndex = ReadPositiveInteger(header.Value) - 1;
        int timeIndex = ReadPositiveInteger(time.Value) - 1;
        var wells = new List<WellColumnMappingDto>();
        foreach (string item in columns.Text.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = item.Split('=', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index < 1)
                throw new ArgumentException("Invalid well-column mapping.");
            wells.Add(new WellColumnMappingDto(index - 1, parts[1]));
        }
        return new ChannelRegionMappingDto(channel, sheet, headerIndex, timeIndex, wells, endRow);
    }

    private static int ReadPositiveInteger(double value)
    {
        if (!double.IsFinite(value) || value < 1 || value > int.MaxValue || value != Math.Truncate(value)) throw new ArgumentException("Expected a positive integer.");
        return (int)value;
    }

    private void BuildWellEditors(IReadOnlyList<WellColumnMappingDto> mappedWells, IReadOnlyList<WellAssignmentDto>? assignments = null)
    {
        updatingDraftControls = true;
        plateDraft.Load(mappedWells.Select(item => item.Well), assignments);
        wellEditors.Clear();
        wellButtons.Clear();
        WellEditors.Children.Clear();
        PlateOverview.Children.Clear();
        PlateOverview.ColumnDefinitions.Clear();
        PlateOverview.RowDefinitions.Clear();
        for (int column = 0; column < 12; column++) PlateOverview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        for (int row = 0; row < 8; row++) PlateOverview.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        HashSet<string> mapped = mappedWells.Select(item => item.Well).ToHashSet(StringComparer.Ordinal);
        for (int row = 0; row < 8; row++)
        for (int column = 1; column <= 12; column++)
        {
            string well = $"{(char)('A' + row)}{column}";
            var button = new ToggleButton { Content = well, Tag = well, Width = 44, Height = 44, Padding = new Thickness(2), IsEnabled = mapped.Contains(well), FontSize = 11 };
            AutomationProperties.SetName(button, $"孔位 {well}，未选择");
            Grid.SetRow(button, row); Grid.SetColumn(button, column - 1);
            button.Click += OnWellSelectionClick;
            button.KeyDown += OnWellKeyDown;
            PlateOverview.Children.Add(button);
            if (!mapped.Contains(well)) continue;
            wellButtons.Add(well, button);
            var role = new ComboBox { PlaceholderText = "选择角色", MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (RoleChoice choice in RoleChoices) role.Items.Add(new ComboBoxItem { Content = choice.Label, Tag = choice.Value });
            var sample = new TextBox { PlaceholderText = "Sample ID", MinWidth = 100 };
            var replicate = new NumberBox { Minimum = 1, Value = 1, Width = 90, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
            var group = new TextBox { Header = "Group", PlaceholderText = "可选分组" };
            var target = new TextBox { Header = "Target", PlaceholderText = "可选研究目标" };
            var notes = new TextBox { Header = "Notes", PlaceholderText = "可选科研备注，不填写患者身份信息", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 120 };
            foreach ((Control control, string label) in new[] { ((Control)role, "角色"), (sample, "Sample ID"), (replicate, "重复孔序号"), (group, "Group"), (target, "Target"), (notes, "Notes") })
                AutomationProperties.SetName(control, $"{well} {label}");
            var line = new Grid { ColumnSpacing = 6 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            line.Children.Add(new TextBlock { Text = well, VerticalAlignment = VerticalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            Grid.SetColumn(role, 1); line.Children.Add(role);
            Grid.SetColumn(sample, 2); line.Children.Add(sample);
            Grid.SetColumn(replicate, 3); line.Children.Add(replicate);
            var metadata = new StackPanel { Spacing = 6 };
            metadata.Children.Add(group); metadata.Children.Add(target); metadata.Children.Add(notes);
            var more = new Expander { Header = $"{well} 的 Group / Target / Notes", Content = metadata, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            var rowPanel = new StackPanel { Spacing = 5 };
            rowPanel.Children.Add(line); rowPanel.Children.Add(more);
            var frame = new Border { Padding = new Thickness(8), CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"], Child = rowPanel };
            WellEditors.Children.Add(frame);
            var editor = new WellEditor(well, role, sample, replicate, group, target, notes, frame);
            wellEditors.Add(well, editor);
            role.SelectionChanged += (_, _) => CaptureWellDraft(editor);
            sample.TextChanged += (_, _) => CaptureWellDraft(editor);
            group.TextChanged += (_, _) => CaptureWellDraft(editor);
            target.TextChanged += (_, _) => CaptureWellDraft(editor);
            notes.TextChanged += (_, _) => CaptureWellDraft(editor);
            replicate.ValueChanged += (_, _) => CaptureWellDraft(editor);
        }
        ApplyDraftToControls();
        updatingDraftControls = false;
        UpdatePlateActions();
        UpdateDraftStatus();
    }

    private void CaptureWellDraft(WellEditor editor)
    {
        // WinUI may deliver queued TextChanged/ValueChanged from detached
        // controls after switching Runs. Match the current editor identity,
        // including when the new Run uses the same well name.
        if (updatingDraftControls || !wellEditors.TryGetValue(editor.Well, out WellEditor? current)
            || !ReferenceEquals(current, editor)) return;
        plateDraft.Update(new WellDraft(editor.Well, (editor.Role.SelectedItem as ComboBoxItem)?.Tag as string,
            editor.Sample.Text, editor.Group.Text, editor.Replicate.Value, editor.Target.Text, editor.Notes.Text));
        RefreshWellAppearance();
        UpdateDraftStatus();
        UpdatePlateActions();
    }

    private void ApplyDraftToControls()
    {
        bool wasUpdating = updatingDraftControls;
        updatingDraftControls = true;
        foreach (WellDraft draft in plateDraft.Rows)
        {
            if (!wellEditors.TryGetValue(draft.Well, out WellEditor? editor)) continue;
            editor.Role.SelectedItem = editor.Role.Items.Cast<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, draft.Role));
            editor.Sample.Text = draft.SampleId; editor.Group.Text = draft.Group; editor.Replicate.Value = draft.ReplicateIndex;
            editor.Target.Text = draft.Target; editor.Notes.Text = draft.Notes;
        }
        updatingDraftControls = wasUpdating;
        RefreshWellAppearance();
    }

    private void RefreshWellAppearance()
    {
        HashSet<string> selected = plateDraft.SelectedWells.ToHashSet(StringComparer.Ordinal);
        bool editable = workspaceAvailable && !IsWorkbenchBusy && viewModel.CanAssignWells;
        foreach (WellDraft draft in plateDraft.Rows)
        {
            if (!wellEditors.TryGetValue(draft.Well, out WellEditor? editor)) continue;
            bool sample = draft.Role == WellRoleNames.Sample;
            editor.Role.IsEnabled = editable;
            foreach (Control control in new Control[] { editor.Sample, editor.Group, editor.Replicate, editor.Target, editor.Notes }) control.IsEnabled = editable && sample;
            if (wellButtons.TryGetValue(draft.Well, out ToggleButton? button))
            {
                button.IsChecked = selected.Contains(draft.Well);
                button.Content = $"{draft.Well}\n{ShortRole(draft.Role)}";
                AutomationProperties.SetName(button, $"{draft.Well}，{ShortRole(draft.Role)}，{(button.IsChecked == true ? "已选中" : "未选中")}");
            }
            editor.Frame.BorderThickness = new Thickness(selected.Contains(draft.Well) ? 2 : 1);
            editor.Frame.BorderBrush = WorkbenchBrush(selected.Contains(draft.Well) ? "SelectionBorder" : "Border");
        }
        PlateSelectionText.Text = selected.Count == 0 ? "单击选孔；Ctrl 多选；Shift 矩形扩选；方向键移动。" : $"已选 {selected.Count} 孔：{string.Join("、", plateDraft.SelectedWells)}";
    }

    private static string ShortRole(string? role) => role switch
    {
        WellRoleNames.Ntc => "NTC", WellRoleNames.Positive100 => "POS", WellRoleNames.WildType => "WT", WellRoleNames.Lqc01Percent => "LQC",
        WellRoleNames.Sample => "样本", WellRoleNames.Ignore => "忽略", _ => "—",
    };
    private static bool KeyIsDown(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void OnWellSelectionClick(object sender, RoutedEventArgs args)
    {
        if (sender is not ToggleButton { Tag: string well }) return;
        plateDraft.Select(well, KeyIsDown(VirtualKey.Control), KeyIsDown(VirtualKey.Shift));
        RefreshWellAppearance(); UpdatePlateActions();
        SelectExplorerWellFromPlate(well);
    }

    private void OnWellKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (sender is not ToggleButton { Tag: string well }) return;
        (int row, int column) = args.Key switch { VirtualKey.Left => (0, -1), VirtualKey.Right => (0, 1), VirtualKey.Up => (-1, 0), VirtualKey.Down => (1, 0), _ => (0, 0) };
        if (row == 0 && column == 0) return;
        string? next = plateDraft.Adjacent(well, row, column);
        if (next is not null && wellButtons.TryGetValue(next, out ToggleButton? button))
        {
            plateDraft.Select(next, KeyIsDown(VirtualKey.Control), KeyIsDown(VirtualKey.Shift));
            button.Focus(FocusState.Keyboard);
            RefreshWellAppearance(); UpdatePlateActions();
            SelectExplorerWellFromPlate(next);
        }
        args.Handled = true;
    }

    private void OnSelectAllWellsClick(object sender, RoutedEventArgs args) { plateDraft.SelectAll(); RefreshWellAppearance(); UpdatePlateActions(); }
    private void OnClearWellsSelectionClick(object sender, RoutedEventArgs args) { plateDraft.ClearSelection(); RefreshWellAppearance(); UpdatePlateActions(); }
    private void OnUndoPlateClick(object sender, RoutedEventArgs args) { plateDraft.Undo(); ApplyDraftToControls(); UpdatePlateActions(); UpdateDraftStatus(); }
    private void OnRedoPlateClick(object sender, RoutedEventArgs args) { plateDraft.Redo(); ApplyDraftToControls(); UpdatePlateActions(); UpdateDraftStatus(); }

    private void OnApplyBatchClick(object sender, RoutedEventArgs args)
    {
        if (!viewModel.CanAssignWells || IsWorkbenchBusy) return;
        try
        {
            plateDraft.ApplyBatch(new PlateBatchEdit((BatchRoleBox.SelectedItem as ComboBoxItem)?.Tag as string,
                BatchSampleCheck.IsChecked == true, BatchSampleBox.Text, BatchGroupCheck.IsChecked == true, BatchGroupBox.Text,
                BatchTargetCheck.IsChecked == true, BatchTargetBox.Text, BatchNotesCheck.IsChecked == true, BatchNotesBox.Text,
                BatchReplicateCheck.IsChecked == true, BatchReplicateCheck.IsChecked == true ? ReadPositiveInteger(BatchReplicateBox.Value) : 1));
            ApplyDraftToControls(); UpdatePlateActions(); UpdateDraftStatus();
        }
        catch (ArgumentException) { ShowWorkbenchError("SYSTEM_BATCH_INPUT_INVALID", "请检查批量角色和起始重复孔序号。未勾选的字段保持原值。"); }
    }

    private void UpdatePlateActions()
    {
        bool canEdit = workspaceAvailable && !IsWorkbenchBusy && viewModel.CanAssignWells;
        ApplyBatchButton.IsEnabled = canEdit && plateDraft.SelectedWells.Count > 0;
        UndoPlateButton.IsEnabled = canEdit && plateDraft.CanUndo;
        RedoPlateButton.IsEnabled = canEdit && plateDraft.CanRedo;
        SelectAllWellsButton.IsEnabled = plateDraft.Rows.Count > 0;
        ClearWellsSelectionButton.IsEnabled = plateDraft.SelectedWells.Count > 0;
    }

    private WellAssignmentDto[] ReadAssignments() => plateDraft.ToAssignments().ToArray();
    private async void OnPrimaryStageActionClick(object sender, RoutedEventArgs args)
    {
        if (IsWorkbenchBusy) return;
        activeOperationTask = ExecuteStageAsync();
        await activeOperationTask;
    }

    private async Task ExecuteStageAsync()
    {
        if (IsWorkbenchBusy || !PrimaryStageAction.IsEnabled) return;
        string requestedStage = activeStage;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(workbenchLifetime.Token);
        activeWorkbenchOperation = operation;
        GlobalInfoBar.IsOpen = false;
        OperationText.Text = "正在处理…";
        UpdateWorkbenchFromViewModel();
        try
        {
            await Task.Yield();
            CancellationToken token = operation.Token;
            bool success;
            switch (requestedStage)
            {
                case "import":
                    if (!await ConfirmDiscardDraftAsync()) return;
                    GatewayResult<SourceFileDto>? staged = await workspace.PickAndStageAsync(WinRT.Interop.WindowNative.GetWindowHandle(this), token);
                    if (staged is null) { OperationText.Text = "已取消文件选择。"; break; }
                    if (!staged.IsSuccess)
                    {
                        AppErrorDto error = staged.Errors[0];
                        ShowWorkbenchError(error.Code, $"{error.UserMessage} {error.RecommendedAction}\n关联编号：{staged.CorrelationId}");
                        break;
                    }
                    var progress = new Progress<OperationProgressDto>(item =>
                    {
                        if (!disposed && ReferenceEquals(activeWorkbenchOperation, operation) && !operation.IsCancellationRequested)
                            OperationText.Text = $"{ImportStageLabel(item.Stage)}{(item.PercentComplete is { } percent ? $" {percent:F0}%" : string.Empty)}";
                    });
                    ImportPreviewDto? preview = await viewModel.InspectImportAsync(staged.Value!, token, progress, ReadImportOptions());
                    if (preview is not null) activeStage = "mapping";
                    OperationText.Text = preview is null ? "预览未完成，请查看提示。" : "预览完成，请分别选择并确认 FAM 与 ROX。";
                    break;
                case "mapping":
                    ChannelRegionMappingDto fam = ReadMapping(ChannelNames.Fam, FamCandidateBox, FamHeaderBox, FamTimeBox, FamColumnsBox);
                    ChannelRegionMappingDto rox = ReadMapping(ChannelNames.Rox, RoxCandidateBox, RoxHeaderBox, RoxTimeBox, RoxColumnsBox);
                    success = await viewModel.ConfirmMappingAsync(fam, rox, token, ReadImportOptions());
                    if (success) { BuildWellEditors(fam.WellColumns); mappingBaseline = MappingDraftFingerprint(); activeStage = "plate"; }
                    OperationText.Text = success ? "数据映射已提交，请设置孔位角色。" : "映射未提交，请查看提示。";
                    break;
                case "plate":
                    success = await viewModel.AssignWellsAsync(ReadAssignments(), token);
                    if (success) { plateDraft.AcceptChanges(); activeStage = "calibration"; }
                    OperationText.Text = success ? "孔位分配已提交。" : "孔位分配未提交，请查看提示。";
                    break;
                case "calibration":
                    success = await viewModel.RunCalibrationAsync(token);
                    OperationText.Text = success ? "校准已完成，请复核后端状态与问题列表。" : "校准操作未完成，请查看提示。";
                    break;
                case "control":
                    success = await viewModel.RunControlQcAsync(token);
                    OperationText.Text = success ? "对照质控已完成，请复核后端状态与问题列表。" : "对照质控操作未完成，请查看提示。";
                    break;
                case "analysis":
                    success = await viewModel.AnalyzeSamplesAsync(token);
                    if (success) activeStage = "results";
                    OperationText.Text = success ? "样本分析已完成，请复核逐孔结果。" : "样本分析操作未完成，请查看提示。";
                    break;
            }
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            OperationText.Text = "操作已取消。请以当前 Run 修订和后端状态为准。";
        }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
        {
            ShowWorkbenchError("SYSTEM_UI_INPUT_INVALID", "请检查所选数据区、从 1 开始的整数行列编号、孔列映射，以及每个孔的角色和样本重复孔信息。");
        }
        catch (Exception)
        {
            ShowWorkbenchError("SYSTEM_WORKBENCH_OPERATION_FAILED", "操作未完成，请查看当前 Run 状态并重试。源文件和历史结果不会由界面重算。");
        }
        finally
        {
            activeWorkbenchOperation = null;
            UpdateWorkbenchFromViewModel();
        }
    }

    private void OnCancelOperationClick(object sender, RoutedEventArgs args)
    {
        activeWorkbenchOperation?.Cancel();
        CancelOperationButton.IsEnabled = false;
        CancelDeliveryButton.IsEnabled = false;
        OperationText.Text = "正在取消…";
    }

    private void RenderStageResults()
    {
        if (renderedStage == activeStage && ReferenceEquals(renderedSnapshot, viewModel.Run) && ReferenceEquals(renderedSamples, viewModel.SampleResults)) return;
        renderedStage = activeStage; renderedSnapshot = viewModel.Run; renderedSamples = viewModel.SampleResults;
        StageResultsPanel.Children.Clear();
        if (activeStage == "calibration")
        {
            CalibrationSummaryDto? summary = viewModel.Run?.CalibrationSummary;
            if (summary is not null)
            {
                AddResultText($"校准 · {StatusLabel(summary.Status)}", true);
                AddResultText(viewModel.Run?.MethodVersion == "1.1"
                    ? "Fm：FAM 取合格平台均值；ROX 取方法指定端点。多阳性孔分别取值后求均值；实际时间与窗口见计算来源。"
                    : "Fm 按本 Run 保存的方法版本计算；历史 1.0 仍使用双通道平台均值。");
                AddResultTable(["通道", "Run Fb", "Run Fm"],
                    [["FAM", Number(summary.RunFbFam), Number(summary.RunFmFam)], ["ROX", Number(summary.RunFbRox), Number(summary.RunFmRox)]]);
            }
            else AddResultText("尚未运行校准。完成孔位分配后，可执行当前阶段。");
            WellDiagnosticDto[] diagnostics = [.. viewModel.CalibrationResult?.PerWellDiagnostics ?? []];
            if (diagnostics.Length > 0) AddResultTable(["孔位", "通道", "问题", "后端数值"],
                diagnostics.Select(item => new[] { item.Well, item.Channel, $"{item.Code} · {item.Message}", Number(item.Value) }));
        }
        else if (activeStage == "control")
        {
            ControlSummaryDto? summary = viewModel.Run?.ControlSummary;
            if (summary is not null)
            {
                AddResultText($"对照质控 · {StatusLabel(summary.Status)}", true);
                AddResultTable(["项目", "FAM / 结论", "ROX / mean dr′"],
                    [["WT 参考", Number(summary.WtYFamMean), Number(summary.WtYRoxMean)], ["LQC 一致方向", LqcDirectionLabel(summary.LqcCall), Number(summary.LqcMeanDrPrime)]]);
            }
            else AddResultText("尚无对照质控结果。校准通过后，可运行 WT / LQC 质控。");
        }
        else if (activeStage is "analysis" or "results")
        {
            bool runPassed = viewModel.Run?.AnalysisSummary?.Status == StatusNames.Pass;
            if (viewModel.SampleResults.Count == 0)
            {
                AddResultText("尚无样本结果", true);
                AddResultText("对照质控通过后执行样本分析。失败与未计算状态不会显示成阴性。");
                return;
            }
            AddResultText("样本汇总", true);
            AddResultTable(["Sample ID", "状态", "科研判读", "mean dr′", "重复孔数"],
                viewModel.SampleResults.Select(sample => new[] { sample.SampleId, StatusLabel(sample.Status),
                    runPassed && sample.Status == StatusNames.Pass ? CallLabel(sample.Call) : "INVALID · 不输出有效判读",
                    Number(sample.MeanDrPrime), sample.PerWell.Count.ToString(CultureInfo.InvariantCulture) }));
            AddResultText("逐孔结果 · 选择计算证据查看完整精度", true);
            var grid = CreateTable(["样本 / 孔", "Y FAM", "Y ROX", "dr′", "单孔状态", "证据"]);
            int row = 1;
            foreach (SampleAnalysisDto sample in viewModel.SampleResults)
            foreach (PerWellResultDto well in sample.PerWell)
            {
                AddTableRow(grid, row, [$"{sample.SampleId} / {well.Well}", Number(well.YFam), Number(well.YRox), Number(well.DrPrime),
                    runPassed ? CallLabel(well.Call) : "INVALID · 仅供复核"]);
                string traceId = well.TraceId;
                var button = new Button { Content = "查看计算", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(3),
                    IsEnabled = viewModel.CurrentEvidence?.Traces.Any(trace => trace.TraceId == traceId) == true };
                AutomationProperties.SetName(button, $"查看样本 {sample.SampleId} 孔 {well.Well} 的已保存计算证据");
                button.Click += (_, _) => SelectTrace(traceId);
                Grid.SetRow(button, row++); Grid.SetColumn(button, 5); grid.Children.Add(button);
            }
            StageResultsPanel.Children.Add(new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
            AddResultText("显示值不参与判读。报告及工程包读取已提交内容，未提交编辑不会进入输出。");
        }
    }

    private void SelectTrace(string traceId)
    {
        foreach (object item in TraceChooser.Items)
            if (item is ComboBoxItem { Tag: string id } option && id == traceId)
            {
                TraceChooser.SelectedItem = option;
                TracePanel.IsExpanded = true;
                TraceChooser.Focus(FocusState.Programmatic);
                TracePanel.StartBringIntoView();
                return;
            }
    }

    private void AddResultTable(string[] headers, IEnumerable<string[]> rows)
    {
        Grid grid = CreateTable(headers);
        int index = 1;
        foreach (string[] row in rows) AddTableRow(grid, index++, row);
        StageResultsPanel.Children.Add(new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
    }

    private Grid CreateTable(string[] headers)
    {
        var table = new Grid { MinWidth = headers.Length * 110, ColumnSpacing = 2, RowSpacing = 2 };
        foreach (string _ in headers) table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddTableRow(table, 0, headers, heading: true);
        return table;
    }

    private void AddTableRow(Grid table, int row, string[] values, bool heading = false)
    {
        while (table.RowDefinitions.Count <= row) table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int column = 0; column < values.Length; column++)
        {
            var text = new TextBlock { Text = values[column], TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = !heading,
                Margin = new Thickness(12, 10, 12, 10), FontSize = 14, Foreground = WorkbenchBrush("PrimaryText"),
                FontWeight = heading ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal };
            var cell = new Border { Child = text, Background = WorkbenchBrush(heading ? "SubtleSurface" : "Surface"),
                BorderBrush = WorkbenchBrush("Border"), BorderThickness = new Thickness(0, 0, 0, 1) };
            Grid.SetRow(cell, row); Grid.SetColumn(cell, column); table.Children.Add(cell);
        }
    }

    private void AddResultText(string text, bool heading = false) => StageResultsPanel.Children.Add(new TextBlock
    {
        Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
        FontSize = heading ? 20 : 14,
        Foreground = WorkbenchBrush(heading ? "PrimaryText" : "SecondaryText"),
        FontWeight = heading ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
    });
    private void RenderIssues()
    {
        IssuesPanel.Children.Clear();
        IEnumerable<IssueDto> issues = activeStage is "import" or "mapping"
            ? viewModel.CurrentPreview?.ValidationIssues ?? []
            : (viewModel.Run?.BlockingIssues ?? []).Concat(viewModel.Run?.WarningIssues ?? []);
        foreach (IssueDto issue in issues)
        {
            IssuesPanel.Children.Add(new TextBlock
            {
                Text = $"⚠ {issue.Code}{(issue.WellId is { } well ? $" · {well}" : string.Empty)}{(issue.Channel is { } channel ? $" / {channel}" : string.Empty)}\n{issue.UserMessage}\n建议：{issue.RecommendedAction}",
                TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
            });
        }
    }

    private void ShowWorkbenchError(string code, string message)
    {
        if (disposed) return;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            GlobalInfoBar.Title = code;
            GlobalInfoBar.Message = message;
            GlobalInfoBar.Severity = InfoBarSeverity.Error;
            GlobalInfoBar.IsOpen = true;
        })) Title = $"FluoAxis - {code}";
    }

    private static string Number(double? value) => value?.ToString("G10", CultureInfo.CurrentCulture) ?? "未计算";
    private static string StatusLabel(string status) => status switch { StatusNames.Pass => "PASS 通过", StatusNames.Fail => "FAIL 失败", StatusNames.Invalid => "INVALID 无效", StatusNames.Valid => "有效", _ => status };
    private static string CallLabel(string? call) => call switch { CallNames.Positive => "BRAF V600E 阳性", CallNames.Negative => "BRAF V600E 阴性", _ => "未判定" };
    private static string StateLabel(string state) => state switch
    {
        RunStateNames.New => "新建", RunStateNames.Imported => "已导入", RunStateNames.Mapped => "已分配孔位",
        RunStateNames.Calibrating => "校准中", RunStateNames.CalibrationPassed => "校准通过", RunStateNames.CalibrationFailed => "校准失败",
        RunStateNames.ControlQc => "对照质控中", RunStateNames.ControlQcFailed => "对照质控失败", RunStateNames.ReadyForAnalysis => "可以分析样本",
        RunStateNames.Analyzing => "分析中", RunStateNames.Analyzed => "分析完成", RunStateNames.ReplicateConflict => "重复孔冲突",
        RunStateNames.RunInvalid => "INVALID 运行无效", RunStateNames.Exported => "已导出", _ => state,
    };
    private static string ImportStageLabel(string stage) => stage switch
    {
        ImportStageNames.DetectingFormat => "识别格式", ImportStageNames.ReadingSheet => "读取工作表",
        ImportStageNames.DiscoveringRegion => "发现候选区", ImportStageNames.Validating => "验证数据", ImportStageNames.Hashing => "计算内容校验值", _ => "导入处理中",
    };
    private static readonly RoleChoice[] RoleChoices =
    [
        new(WellRoleNames.Ntc, "NTC"), new(WellRoleNames.Positive100, "100% Positive"), new(WellRoleNames.WildType, "WT"),
        new(WellRoleNames.Lqc01Percent, "LQC 0.1%"), new(WellRoleNames.Sample, "样本"), new(WellRoleNames.Ignore, "忽略"),
    ];
    private sealed record RoleChoice(string Value, string Label);
    private sealed record WellEditor(string Well, ComboBox Role, TextBox Sample, NumberBox Replicate, TextBox Group, TextBox Target, TextBox Notes, Border Frame);
    private void OnTrustedMessageReceived(object? sender, TrustedPlotMessageEventArgs args)
    {
        if (closeRequested || disposed) return;
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
            if (!DispatcherQueue.TryEnqueue(() => TrackPlotOperation(RenderExplorerAfterActivationAsync)))
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
            renderedPlotAvailable = true;
            Title = activePlotIsDemo ? "FluoAxis - 演示图已渲染" : "FluoAxis - 科研数据视图";
            StatusText.Text = $"{PlotLabel(activeKind)} · {(activePlotIsDemo ? "演示" : "Raw RFU 当前页")} {activePlotPointCount:N0} 点 · {renderTimer.ElapsedMilliseconds} ms";
            UpdateRawControls();
            return;
        }

        if (args.Message.Payload is PlotExportCompletedPayload completed)
        {
            if (closeRequested) return;
            exportTasks.RemoveAll(task => task.IsCompleted);
            exportTasks.Add(SaveSpikeExportAsync(completed));
            return;
        }

        if (args.Message.Payload is PlotPointPayload point)
        {
            if (activePlotIsDemo) StatusText.Text = $"选中演示点 PointId：{point.PointId}";
            else
            {
                SelectExplorerRows([point.PointId]);
                ShowRawPoint(point.PointId);
                SendExplorerSelection([point.PointId]);
            }
            return;
        }

        if (args.Message.Payload is PlotSelectionPayload selection)
        {
            // Acknowledgements consume the request but cannot overwrite a more
            // recent table selection. Unsolicited lasso/box events update rows.
            if (!selectionRequests.Remove(args.Message.Envelope.RequestId) && !activePlotIsDemo)
            {
                SelectExplorerRows(selection.PointIds);
                if (selection.PointIds.Count == 1) ShowRawPoint(selection.PointIds[0]);
            }
            return;
        }

        if (args.Message.Payload is PlotCameraPayload camera)
        {
            StatusText.Text = $"相机：{camera.Camera.EyeX:F2}, {camera.Camera.EyeY:F2}, {camera.Camera.EyeZ:F2}";
            return;
        }

        if (args.Message.Payload is PlotErrorPayload error)
        {
            selectionRequests.Remove(args.Message.Envelope.RequestId);
            Title = $"FluoAxis - {error.Code}";
            StatusText.Text = $"图形操作未完成：{error.Code}。请检查数据或改用支持的格式。";
            return;
        }

    }

    private void TrackPlotOperation(Func<Task> operation)
    {
        if (closeRequested || disposed) return;
        plotTasks.RemoveAll(task => task.IsCompleted);
        plotTasks.Add(ObservePlotOperationAsync(operation));
    }

    private async Task ObservePlotOperationAsync(Func<Task> operation)
    {
        try { await operation(); }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!closeRequested && !disposed) ShowPlotFallback("PLOT_OPERATION_FAILED");
        }
    }

    private async Task RenderAsync(PlotKind kind)
    {
        if (!plotReady || !explorerDemoMode) return;
        renderPreparation?.Cancel();
        renderPreparation?.Dispose();
        renderPreparation = new CancellationTokenSource();
        CancellationToken cancellationToken = renderPreparation.Token;

        activeKind = kind;
        activePlotIsDemo = true;
        activePlotPointCount = fixturePointCount;
        renderedPlotAvailable = false;
        activePlotId = Guid.NewGuid();
        generation++;
        responseGate.Activate(activePlotId, generation);
        bool is3D = kind is PlotKind.Scatter3D or PlotKind.Line3D;
        Guid plotId = activePlotId;
        long renderGeneration = generation;
        StatusText.Text = $"正在准备{PlotLabel(kind)}（{fixturePointCount} 个演示点）…";
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
        PlotFallback.Visibility = Visibility.Collapsed;
        PlotWebView.Visibility = Visibility.Visible;
        renderTimer.Restart();
        plotHost?.PostJson(prepared.Json);
        UpdateRawControls();
        StatusText.Text = $"正在绘制{PlotLabel(kind)}…";
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
            is3D ? new AxisSpec("时间 / min（演示）") : null,
            [new SeriesSpec("research-series", "演示序列", markerSize: 8)],
            is3D ? new CameraSpec(1.35, 1.35, 1.15) : null,
            showLegend: true,
            title: $"FluoAxis {PlotLabel(kind)} · 演示数据");
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

    private void OnScatter2DClick(object sender, RoutedEventArgs args)
    {
        SetExplorerPlotKind(PlotKind.Scatter2D);
    }
    private void OnScatter3DClick(object sender, RoutedEventArgs args) => SetExplorerPlotKind(PlotKind.Scatter3D);
    private void OnLine3DClick(object sender, RoutedEventArgs args) => SetExplorerPlotKind(PlotKind.Line3D);
    private void OnExportPngClick(object sender, RoutedEventArgs args) => Export(PlotExportFormat.Png);
    private void OnExportSvgClick(object sender, RoutedEventArgs args) => Export(PlotExportFormat.Svg);

    private void Export(PlotExportFormat format)
    {
        if (closeRequested || disposed || !plotReady || !renderedPlotAvailable
            || (format == PlotExportFormat.Svg && (activeKind is PlotKind.Scatter3D or PlotKind.Line3D || activePlotPointCount >= 10_000))) return;
        try
        {
            Post(PlotMessageTypes.Export, JsonSerializer.SerializeToElement(
                new PlotExportPayload { Format = format, Width = 1600, Height = 900, Scale = 2 },
                PlotProtocolJsonContext.Default.PlotExportPayload));
            StatusText.Text = $"正在导出当前{(activePlotIsDemo ? "演示图" : "Raw RFU 页")} {format}…";
        }
        catch (Exception) { ShowPlotFallback("PLOT_EXPORT_REQUEST_FAILED"); }
    }

    private Guid Post(string type, JsonElement payload)
    {
        if (!plotReady) return Guid.Empty;
        PlotMessageEnvelope message = new()
        {
            SchemaVersion = PlotMessageCodec.CurrentSchemaVersion, Type = type, RequestId = Guid.NewGuid(),
            PlotId = activePlotId, Generation = generation, Payload = payload,
        };
        responseGate.RegisterCommand(activePlotId, generation, message.RequestId, message.Type);
        plotHost?.PostJson(JsonSerializer.Serialize(message, PlotProtocolJsonContext.Default.PlotMessageEnvelope));
        return message.RequestId;
    }

    private async Task SaveSpikeExportAsync(PlotExportCompletedPayload completed)
    {
        try
        {
            string prefix = $"data:{completed.MediaType};base64,";
            byte[] bytes = Convert.FromBase64String(completed.DataUrl[prefix.Length..]);
            if (completed.Format == PlotExportFormat.Png && await TryAttachReportFigureAsync(bytes)) return;
            string extension = completed.Format == PlotExportFormat.Png ? "png" : "svg";
            GatewayResult<bool>? saved = await DesktopWorkspace.ExportPlotImageAsync(WinRT.Interop.WindowNative.GetWindowHandle(this), bytes, extension, workbenchLifetime.Token);
            StatusText.Text = saved is null ? "已取消图形保存" : saved.IsSuccess ? $"已保存当前图形 {completed.Format}" : "图形保存失败";
        }
        catch (OperationCanceledException) when (workbenchLifetime.IsCancellationRequested)
        {
            StatusText.Text = "图形保存已取消";
        }
        catch (Exception)
        {
            StatusText.Text = "图形保存失败（PLOT_EXPORT_WRITE_FAILED）";
        }
    }

    private async Task InitializePlotHostAsync()
    {
        if (plotHost is not null) return;
        PlotHostWebViewAdapter adapter = new(PlotWebView, DesktopWebViewEnvironment.CreateAsync);
        plotHost = adapter;
        adapter.TrustedMessageReceived += OnTrustedMessageReceived;
        StatusText.Text = "正在启动本地图形引擎…";
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
        PlotFallback.Text = $"图形功能暂不可用（{errorCode}）。请检查 WebView2 运行时后重试。";
        Title = $"FluoAxis - {errorCode}";
    }

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (closeAllowed) return;
        args.Cancel = true;
        if (closeRequested || draftDialogOpen) return;
        if (!IsWorkbenchBusy)
        {
            if (!await ConfirmDiscardDraftAsync()) return;
            abandonDraftOnClose = true;
        }
        closeRequested = true;
        OperationText.Text = "正在结束当前操作并关闭工作区…";
        activeWorkbenchOperation?.Cancel();
        CancelRawRead();
        renderPreparation?.Cancel();
        UpdateWorkbenchFromViewModel();
        try
        {
            await Task.WhenAll(initializationTask, activeOperationTask);
            if (!abandonDraftOnClose && !await ConfirmDiscardDraftAsync())
            {
                closeRequested = false;
                OperationText.Text = "已返回编辑。当前操作已停止，未提交编辑保持不变。";
                UpdateWorkbenchFromViewModel();
                return;
            }
            workbenchLifetime.Cancel();
            await Task.WhenAll(plotTasks.ToArray());
            await Task.WhenAll(exportTasks.ToArray());
            await workspace.CloseAsync();
            closeAllowed = true;
            Close();
        }
        catch (Exception)
        {
            workspaceAvailable = false;
            closeRequested = false;
            ShowWorkbenchError("SYSTEM_WORKSPACE_CLOSE_FAILED", "工作区未能正常关闭。已提交结果保留在持久存储中，请再次关闭；不要继续修改当前 Run。");
            UpdateWorkbenchFromViewModel();
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Dispose();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        RootSurface.ActualThemeChanged -= OnWorkbenchThemeChanged;
        PalettePrimary.UnregisterPropertyChangedCallback(Border.BackgroundProperty, primaryPaletteCallback);
        PaletteAccentSoft.UnregisterPropertyChangedCallback(Border.BackgroundProperty, surfacePaletteCallback);
        CancelRawRead();
        activeWorkbenchOperation?.Cancel();
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
        AppWindow.Closing -= OnAppWindowClosing;
        Activated -= OnActivated;
        Closed -= OnClosed;
        GC.SuppressFinalize(this);
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        windowActivated.TrySetResult();
    }

    private sealed record PreparedRender(Guid RequestId, string Json);

    private static string PlotLabel(PlotKind kind) => kind switch
    {
        PlotKind.Scatter2D => "二维散点", PlotKind.Line2D => "二维折线", PlotKind.ScatterLine2D => "二维散点折线",
        PlotKind.Scatter3D => "三维散点", PlotKind.Line3D => "三维折线", _ => "图形",
    };
}
