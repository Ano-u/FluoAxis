namespace FluoAxis.App;

using System.Globalization;
using FluoAxis.Contracts;
using FluoAxis.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

public sealed partial class MainWindow
{
    private bool updatingSmartProfiles;
    private SourceFileDto? smartSource;
    private AutomaticImportProposalDto? smartProposal;

    private PlateProfileDto? SelectedPlateProfile => (SmartProfileChooser.SelectedItem as ComboBoxItem)?.Tag as PlateProfileDto;

    private async Task RefreshPlateProfilesAsync(CancellationToken token, Guid? selectedId = null)
    {
        var result = await workspace.ListPlateProfilesAsync(token);
        if (!ShowDeliveryFailure(result)) return;
        if (result.Value!.Count == 0)
        {
            var created = await workspace.SavePlateProfileAsync("四角对照 · B2–G11 样本区", UserPlateDefaults.CreateAssignments(), false, true, token);
            if (!ShowDeliveryFailure(created)) return;
            result = await workspace.ListPlateProfilesAsync(token);
            if (!ShowDeliveryFailure(result)) return;
        }
        updatingSmartProfiles = true;
        SmartProfileChooser.Items.Clear();
        SmartProfileChooser.Items.Add(new ComboBoxItem { Content = "仅使用文件中的明确角色" });
        foreach (PlateProfileDto profile in result.Value!)
            SmartProfileChooser.Items.Add(new ComboBoxItem { Content = $"{profile.Definition.Name}{(profile.IsDefault ? " · 默认" : string.Empty)}", Tag = profile });
        SmartProfileChooser.SelectedItem = SmartProfileChooser.Items.Cast<ComboBoxItem>().FirstOrDefault(item => item.Tag is PlateProfileDto profile && profile.ProfileId == selectedId)
            ?? SmartProfileChooser.Items.Cast<ComboBoxItem>().FirstOrDefault(item => item.Tag is PlateProfileDto { IsDefault: true })
            ?? SmartProfileChooser.Items[0];
        updatingSmartProfiles = false;
        UpdateSmartProfileSummary();
    }

    private void OnSmartProfileChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!uiInitialized || updatingSmartProfiles) return;
        UpdateSmartProfileSummary();
    }

    private void UpdateSmartProfileSummary()
    {
        if (SelectedPlateProfile is not { } profile)
        {
            SmartProfileSummary.Text = "根据文件的明确角色标记识别；只有孔号时请选择固定孔位方案。";
            return;
        }
        string controls = string.Join("；", profile.Definition.Wells.Where(well => well.Role is not (WellRoleNames.Sample or WellRoleNames.Ignore))
            .Select(well => $"{well.Well}={ShortRole(well.Role)}"));
        SmartProfileSummary.Text = $"{controls} · {PlateProfileDisplay.DescribeCoverage(profile.Definition)}。缺对照会停止；未提供样本编号时以孔号标识。";
    }

    private void UpdateSmartControls()
    {
        bool available = workspaceAvailable && !IsWorkbenchBusy;
        SmartImportButton.IsEnabled = available;
        PreviewOnlyImportButton.IsEnabled = available;
        ManagePlateProfileButton.IsEnabled = available;
        SmartProfileChooser.IsEnabled = available;
        ApplyPlateProfileButton.IsEnabled = available && viewModel.CanAssignWells && SelectedPlateProfile is not null;
        SaveCurrentPlateProfileButton.IsEnabled = available && plateDraft.Rows.Count > 0;
        ContinueSmartButton.IsEnabled = available && (viewModel.CanConfirmMapping || viewModel.CanAssignWells || viewModel.CanRunCalibration || viewModel.CanRunControlQc || viewModel.CanAnalyzeSamples);
        ContinueSmartButton.Visibility = ContinueSmartButton.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
        RefineSmartButton.IsEnabled = available && (smartSource is not null || viewModel.Run?.SourceFiles.Count > 0);
        if (SmartOutcomePanel.Visibility == Visibility.Collapsed && viewModel.Run?.SourceFiles.Count > 0)
        {
            SmartOutcomePanel.Visibility = Visibility.Visible;
            SmartOutcomeTitle.Text = "已保存运行 · 可继续复核";
            SmartOutcomeText.Text = "当前结果和来源保持不变。需要更改映射、样本编号或重复孔时，可从此文件建立新的分析设置。";
        }
    }

    private async void OnSmartImportClick(object sender, RoutedEventArgs args) => await StartSmartImportAsync(true);
    private async void OnPreviewOnlyImportClick(object sender, RoutedEventArgs args) => await StartSmartImportAsync(false);

    private async Task StartSmartImportAsync(bool analyze, DataPackageView? dropped = null, SourceFileDto? reusedSource = null)
    {
        if (IsWorkbenchBusy || !workspaceAvailable) return;
        activeOperationTask = ImportFileAsync();
        await activeOperationTask;

        async Task ImportFileAsync()
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(workbenchLifetime.Token);
            activeWorkbenchOperation = cancellation;
            UpdateWorkbenchFromViewModel();
            try
            {
                if (!await ConfirmDiscardDraftAsync()) return;
                var staged = reusedSource is not null ? GatewayResult.Ok(reusedSource)
                    : dropped is not null ? await workspace.StageDroppedFileAsync(dropped, cancellation.Token)
                    : await workspace.PickAndStageAsync(WinRT.Interop.WindowNative.GetWindowHandle(this), cancellation.Token);
                if (staged is null) { OperationText.Text = "已取消选择文件。"; return; }
                if (!ShowDeliveryFailure(staged)) return;
                GlobalInfoBar.IsOpen = false;
                SourceFileDto source = staged.Value!;
                PlateProfileDto? profile = SelectedPlateProfile;
                ImportOptionsDto? configuredOptions = analyze ? profile?.Definition.Options : ReadImportOptions();
                ResetDraftEditors();
                if (configuredOptions is not null) RestoreImportOptions(configuredOptions);
                smartSource = source;
                smartProposal = null;
                SmartOutcomePanel.Visibility = Visibility.Visible;
                SmartOutcomeTitle.Text = analyze ? "正在识别并分析" : "导入预览 · 可继续人工设置";
                SmartOutcomeText.Text = "读取文件结构、通道与孔位…";
                string label = source.OriginalFileName;
                int extension = label.LastIndexOf('.');
                if (extension > 0) label = label[..extension];
                if (label.Length > 90) label = label[..90];
                await viewModel.InitializeAsync($"{label} · {DateTimeOffset.Now:MM-dd HH:mm:ss}", cancellation.Token);
                if (viewModel.Run is null || viewModel.HasError) return;
                await RefreshProjectListAsync(0, cancellation.Token);
                SelectProjectInList(viewModel.Run.RunId);
                activeStage = "import";
                PrimaryNavigation.SelectedItem = PrimaryNavigation.MenuItems[0];
                ShowArea("workbench");
                var recognized = await workspace.RecognizeImportAsync(source, cancellation.Token, configuredOptions);
                if (!ShowDeliveryFailure(recognized)) return;
                smartProposal = recognized.Value!;
                ShowRecognition(smartProposal, profile);
                if (smartProposal.Options is { } options) RestoreImportOptions(options);
                if (analyze && smartProposal.MappingReady)
                {
                    bool passed = await viewModel.AnalyzeAutomaticallyAsync(source, smartProposal, cancellation.Token, SmartProgress(cancellation), profile);
                    if (viewModel.Run?.DomainState != RunStateNames.New)
                    {
                        SelectAutomaticResultStage();
                        SmartOutcomeTitle.Text = passed ? "自动分析完成" : "已停止自动分析，请复核";
                        OperationText.Text = passed ? "已完成校准、对照质控和样本判读。可查看逐孔证据，或从此文件重新设置。" : "后端已停止后续步骤，结果与问题保留在当前运行。";
                        return;
                    }
                    if (viewModel.ErrorMessage is { Length: > 0 } reason)
                        SmartOutcomeText.Text += $"\n自动分析暂停：{reason}";
                }
                await LoadManualRecognitionAsync(source, smartProposal, cancellation.Token);
                SmartOutcomeTitle.Text = "需要复核识别设置";
                OperationText.Text = "请核对映射或孔位方案；确认后可继续自动分析。";
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { OperationText.Text = "已取消。已提交内容保留，请查看当前运行状态。"; }
            catch (Exception) { ShowWorkbenchError("SYSTEM_AUTO_IMPORT_FAILED", "自动导入未完成。请检查文件和固定孔位方案，或使用仅导入预览。已有结果保持不变。"); }
            finally { activeWorkbenchOperation = null; UpdateWorkbenchFromViewModel(); }
        }
    }

    private Progress<OperationProgressDto> SmartProgress(CancellationTokenSource operation) => new(progress =>
    {
        if (disposed || operation.IsCancellationRequested || !ReferenceEquals(activeWorkbenchOperation, operation)) return;
        OperationText.Text = progress.Message;
        if (viewModel.AutomaticStage is "mapping" or "plate" or "calibration" or "control" or "analysis") activeStage = viewModel.AutomaticStage;
        UpdateStageSurface();
    });

    private void SelectAutomaticResultStage()
    {
        activeStage = viewModel.Run?.DomainState switch
        {
            RunStateNames.CalibrationFailed => "calibration",
            RunStateNames.ControlQcFailed => "control",
            RunStateNames.RunInvalid when viewModel.AutomaticStage is "calibration" or "control" => viewModel.AutomaticStage,
            RunStateNames.Analyzed or RunStateNames.Exported or RunStateNames.RunInvalid => "results",
            _ => viewModel.AutomaticStage is "mapping" or "plate" or "calibration" or "control" or "analysis" ? viewModel.AutomaticStage : "plate",
        };
    }

    private void ShowRecognition(AutomaticImportProposalDto proposal, PlateProfileDto? profile)
    {
        SmartOutcomePanel.Visibility = Visibility.Visible;
        var lines = new List<string> { $"文件：{smartSource?.OriginalFileName} · 格式：{proposal.DetectedFormat}",
            profile is null ? "角色来源：文件明确标记" : $"固定方案：{profile.Definition.Name} · 样本使用本文件编号或孔号技术ID" };
        lines.AddRange(proposal.Reasons);
        foreach (IssueDto issue in proposal.Issues)
            if (profile is null || issue.Code != "IMPORT_AUTO_ROLES_REQUIRED") lines.Add($"{issue.UserMessage} {issue.RecommendedAction}");
        SmartOutcomeText.Text = string.Join("\n", lines);
    }

    private async Task LoadManualRecognitionAsync(SourceFileDto source, AutomaticImportProposalDto proposal, CancellationToken token)
    {
        await viewModel.InspectImportAsync(source, token, options: proposal.Options ?? ReadImportOptions());
        UpdateWorkbenchFromViewModel();
        if (proposal.Fam is { } fam) FillRecognizedRegion(fam, FamCandidateBox, FamSheetBox, FamHeaderBox, FamTimeBox, FamEndRowBox, FamColumnsBox);
        if (proposal.Rox is { } rox) FillRecognizedRegion(rox, RoxCandidateBox, RoxSheetBox, RoxHeaderBox, RoxTimeBox, RoxEndRowBox, RoxColumnsBox);
        mappingBaseline = MappingDraftFingerprint();
        activeStage = "mapping";
    }

    private static void FillRecognizedRegion(ChannelRegionMappingDto region, ComboBox candidates, TextBox sheet, NumberBox header, NumberBox time, NumberBox end, TextBox columns)
    {
        AddStoredMapping(candidates, region);
        candidates.SelectedIndex = candidates.Items.Count - 1;
        sheet.Text = region.SheetName; header.Value = region.HeaderRowIndex + 1; time.Value = region.TimeColumnIndex + 1; end.Value = region.EndRowIndex is { } last ? last + 1 : 0;
        columns.Text = string.Join(", ", region.WellColumns.Select(item => $"{item.ColumnIndex + 1}={item.Well}"));
    }

    private async void OnContinueSmartClick(object sender, RoutedEventArgs args)
    {
        if (!ContinueSmartButton.IsEnabled || IsWorkbenchBusy) return;
        activeOperationTask = ContinueAsync();
        await activeOperationTask;
        async Task ContinueAsync()
        {
            GlobalInfoBar.IsOpen = false;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(workbenchLifetime.Token);
            activeWorkbenchOperation = cancellation; UpdateWorkbenchFromViewModel();
            try
            {
                if (viewModel.CanConfirmMapping)
                {
                    var fam = ReadMapping(ChannelNames.Fam, FamCandidateBox, FamHeaderBox, FamTimeBox, FamColumnsBox);
                    var rox = ReadMapping(ChannelNames.Rox, RoxCandidateBox, RoxHeaderBox, RoxTimeBox, RoxColumnsBox);
                    if (!await viewModel.ConfirmMappingAsync(fam, rox, cancellation.Token, ReadImportOptions())) return;
                    UpdateWorkbenchFromViewModel();
                }
                if (viewModel.CanAssignWells)
                {
                    if (!plateDraft.IsDirty && smartProposal?.Issues.Any(issue => issue.Code is "IMPORT_AUTO_SAMPLE_METADATA_REQUIRED" or "IMPORT_AUTO_ROLE_CONFLICT") == true)
                    {
                        activeStage = "plate";
                        ShowWorkbenchError("PLATE_AUTO_METADATA_REVIEW_REQUIRED", "文件的样本信息不完整或角色冲突。请核对并编辑孔位，或明确应用当前方案后继续。");
                        return;
                    }
                    if (!plateDraft.IsDirty && SelectedPlateProfile is { } profile && viewModel.CurrentEvidence?.Mapping is { } mapping)
                    {
                        var applied = PlateProfileApplication.Apply(profile, mapping.Fam.WellColumns.Select(item => item.Well).ToArray(), smartProposal?.Assignments);
                        if (!ShowDeliveryFailure(applied)) { activeStage = "plate"; return; }
                        foreach (WellAssignmentDto assignment in applied.Value!) plateDraft.Update(new WellDraft(assignment.Well, assignment.Role, assignment.SampleId ?? "", ReplicateIndex: assignment.ReplicateIndex ?? 1));
                        ApplyDraftToControls();
                    }
                    else if (!plateDraft.IsDirty && SelectedPlateProfile is null && smartProposal is { Assignments.Count: > 0 } proposal
                        && plateDraft.Rows.Select(row => row.Well).ToHashSet(StringComparer.Ordinal).SetEquals(proposal.Assignments.Select(item => item.Well)))
                    {
                        foreach (WellAssignmentDto assignment in proposal.Assignments) plateDraft.Update(new WellDraft(assignment.Well, assignment.Role, assignment.SampleId ?? "", ReplicateIndex: assignment.ReplicateIndex ?? 1));
                        ApplyDraftToControls();
                    }
                    if (!await viewModel.AssignWellsAsync(ReadAssignments(), cancellation.Token)) return;
                    plateDraft.AcceptChanges();
                }
                bool completed = await viewModel.ContinueAnalysisAutomaticallyAsync(cancellation.Token, SmartProgress(cancellation));
                SelectAutomaticResultStage();
                SmartOutcomeTitle.Text = completed ? "自动分析完成" : "自动分析已停止";
                OperationText.Text = completed ? "已完成校准、对照质控和样本判读。可查看逐孔证据，或从此文件重新设置。" : "后续分析已停止，请查看当前阶段的问题与计算证据。";
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { OperationText.Text = "已取消后续分析。"; }
            catch (Exception) { ShowWorkbenchError("SYSTEM_AUTO_SETTINGS_INVALID", "请补齐通道映射、孔位角色和样本信息，然后继续。"); activeStage = "plate"; }
            finally { activeWorkbenchOperation = null; UpdateWorkbenchFromViewModel(); }
        }
    }

    private async void OnRefineSmartClick(object sender, RoutedEventArgs args)
    {
        SourceFileDto? source = viewModel.Run is { SourceFiles.Count: > 0 } run ? run.SourceFiles[0] : smartSource;
        if (source is not null) await StartSmartImportAsync(false, reusedSource: source);
    }

    private void OnApplyPlateProfileClick(object sender, RoutedEventArgs args)
    {
        if (!ApplyPlateProfileButton.IsEnabled || SelectedPlateProfile is not { } profile) return;
        var applied = PlateProfileApplication.Apply(profile, plateDraft.Rows.Select(row => row.Well).ToArray());
        if (!ShowDeliveryFailure(applied)) return;
        plateDraft.ApplyAssignments(applied.Value!);
        ApplyDraftToControls(); UpdatePlateActions(); UpdateDraftStatus();
        SmartOutcomePanel.Visibility = Visibility.Visible;
        SmartOutcomeTitle.Text = "方案已填入孔板草稿";
        SmartOutcomeText.Text = "可继续修改样本编号/重复孔关系，再点击确认设置并继续自动分析。";
    }
}
