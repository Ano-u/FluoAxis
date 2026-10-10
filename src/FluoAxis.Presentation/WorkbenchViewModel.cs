namespace FluoAxis.Presentation;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using FluoAxis.Contracts;

public enum WorkbenchUiState
{
    Initial,
    Loading,
    Ready,
    Empty,
    RecoverableError,
    FatalError,
}

public sealed record GuidedStageItem(string Key, string Label, string RequiredAction);

public sealed record GuidedStageProgress(string State, string Label);

/// <summary>Presentation state for the Phase 1 capability-honest workbench shell.</summary>
public sealed partial class WorkbenchViewModel : INotifyPropertyChanged
{
    private readonly GuidedStageItem[] stageDefinitions =
    [
        new("import", "Import Preview", RunAction.Import),
        new("mapping", "Data Mapping", RunAction.ConfirmMapping),
        new("plate", "Plate Map", RunAction.AssignWells),
        new("calibration", "Calibration", RunAction.RunCalibration),
        new("control", "Control QC", RunAction.RunControlQc),
        new("analysis", "Sample Analysis", RunAction.AnalyzeSamples),
        new("results", "Results / Export", RunAction.Export),
    ];

    private readonly IFluoAxisGateway gateway;
    private readonly string actorId;
    private readonly TimeProvider timeProvider;
    private long loadGeneration;

    private WorkbenchUiState uiState = WorkbenchUiState.Initial;
    private string activeArea = "home";
    private string projectName = "No project loaded";
    private string runState = "Not loaded";
    private string methodIdentity = "Method not loaded";
    private string? errorCode;
    private string? errorMessage;
    private string? recommendedAction;
    private Guid? correlationId;
    private RunSnapshotDto? run;
    private ImportPreviewDto? currentPreview;
    private SourceFileDto? currentSource;
    private string? currentSourceMediaType;
    private CalibrationResultDto? calibrationResult;
    private ControlQcResultDto? controlQcResult;
    private IReadOnlyList<SampleAnalysisDto> sampleResults = [];
    private RunEvidenceDto? currentEvidence;
    private bool snapshotIsCurrent;

    public WorkbenchViewModel(IFluoAxisGateway gateway, string actorId = "local-researcher", TimeProvider? timeProvider = null)
    {
        this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        this.actorId = actorId;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IReadOnlyList<GuidedStageItem> GuidedStages => stageDefinitions;
    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkbenchUiState UiState { get => uiState; private set => SetProperty(ref uiState, value); }
    public string ActiveArea { get => activeArea; private set => SetProperty(ref activeArea, value); }
    public string ProjectName { get => projectName; private set => SetProperty(ref projectName, value); }
    public string RunState { get => runState; private set => SetProperty(ref runState, value); }
    public string MethodIdentity { get => methodIdentity; private set => SetProperty(ref methodIdentity, value); }
    public string? ErrorCode { get => errorCode; private set => SetProperty(ref errorCode, value); }
    public string? ErrorMessage { get => errorMessage; private set => SetProperty(ref errorMessage, value); }
    public string? RecommendedAction { get => recommendedAction; private set => SetProperty(ref recommendedAction, value); }
    public Guid? CorrelationId { get => correlationId; private set => SetProperty(ref correlationId, value); }
    public RunSnapshotDto? Run { get => run; private set => SetProperty(ref run, value); }

    public bool IsBusy => automaticWorkflowActive || UiState == WorkbenchUiState.Loading;

    public bool HasError => ErrorCode is not null;
    public bool CanImport => HasAction(RunAction.Import);
    public bool CanConfirmMapping => HasAction(RunAction.ConfirmMapping);
    public bool CanAssignWells => HasAction(RunAction.AssignWells);
    public bool CanRunCalibration => HasAction(RunAction.RunCalibration);
    public bool CanRunControlQc => HasAction(RunAction.RunControlQc);
    public bool CanAnalyzeSamples => HasAction(RunAction.AnalyzeSamples);
    public bool CanExport => HasAction(RunAction.Export);

    public ImportPreviewDto? CurrentPreview
    {
        get => currentPreview;
        private set => SetProperty(ref currentPreview, value);
    }

    public SourceFileDto? CurrentSource { get => currentSource; private set => SetProperty(ref currentSource, value); }
    public CalibrationResultDto? CalibrationResult { get => calibrationResult; private set => SetProperty(ref calibrationResult, value); }
    public ControlQcResultDto? ControlQcResult { get => controlQcResult; private set => SetProperty(ref controlQcResult, value); }
    public IReadOnlyList<SampleAnalysisDto> SampleResults { get => sampleResults; private set => SetProperty(ref sampleResults, value); }
    public RunEvidenceDto? CurrentEvidence { get => currentEvidence; private set => SetProperty(ref currentEvidence, value); }

    public bool IsStageAvailable(GuidedStageItem stage) =>
        HasAction(stage.RequiredAction);

    /// <summary>Display-only progress from saved evidence and backend actions; never decides workflow permission.</summary>
    public GuidedStageProgress StageProgress(string stage)
    {
        string? savedStatus = stage switch
        {
            "calibration" => Run?.CalibrationSummary?.Status,
            "control" => Run?.ControlSummary?.Status,
            "analysis" or "results" => Run?.AnalysisSummary?.Status,
            _ => null,
        };
        if (savedStatus is StatusNames.Fail or StatusNames.Invalid) return new("failed", "失败 / 无效");
        if (savedStatus is StatusNames.Pass or StatusNames.Valid) return new("complete", "已完成");
        bool complete = stage switch
        {
            "import" => CurrentPreview is not null || Run?.SourceFiles.Count > 0,
            "mapping" => CurrentEvidence?.Mapping is not null || Run?.SourceFiles.Count > 0,
            "plate" => CurrentEvidence?.Assignments.Count > 0 || Run?.MappingSummary?.AssignedWells > 0,
            _ => false,
        };
        if (complete) return new("complete", "已完成");
        GuidedStageItem? definition = stageDefinitions.FirstOrDefault(item => item.Key == stage);
        if (definition is not null && Run?.AllowedActions.Contains(definition.RequiredAction, StringComparer.Ordinal) == true)
            return new("available", "可操作");
        return new("pending", Run is null ? "未选择项目" : "待开始");
    }

    public void SelectArea(string area)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(area);
        ActiveArea = area;
    }

    public async Task InitializeAsync(string requestedProjectName, CancellationToken cancellationToken)
    {
        if (automaticWorkflowActive) return;
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedProjectName);
        long generation = Interlocked.Increment(ref loadGeneration);
        ClearLoadedData();
        SetLoading();

        try
        {
            GatewayResult<ProjectSnapshotDto> created = await gateway.CreateProjectAsync(
                new CreateProjectCommand(Guid.NewGuid(), requestedProjectName, actorId, timeProvider.GetUtcNow()),
                cancellationToken);
            if (!IsCurrent(generation)) return;
            if (!created.IsSuccess)
            {
                SetFailure(created.Errors, created.CorrelationId);
                return;
            }

            GatewayResult<RunSnapshotDto> loaded = await gateway.GetRunAsync(
                new GetRunQuery(Guid.NewGuid(), created.Value!.RunId), cancellationToken);
            if (!IsCurrent(generation)) return;
            if (!loaded.IsSuccess)
            {
                SetFailure(loaded.Errors, loaded.CorrelationId);
                return;
            }

            ProjectName = created.Value.ProjectName;
            ApplySnapshot(loaded.Value!);
            CurrentSource = null;
            CurrentPreview = null;
            currentSourceMediaType = null;
            CalibrationResult = null;
            ControlQcResult = null;
            SampleResults = [];
            CurrentEvidence = null;
            CorrelationId = loaded.CorrelationId;
            UiState = WorkbenchUiState.Ready;
            NotifyDerivedState();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!IsCurrent(generation)) return;
            UiState = WorkbenchUiState.Initial;
            NotifyDerivedState();
            throw;
        }
    }

    /// <summary>Restores stored evidence through a read-only query; no analysis command is replayed.</summary>
    public async Task<bool> LoadRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        if (automaticWorkflowActive) return false;
        if (runId == Guid.Empty) throw new ArgumentException("A Run ID is required.", nameof(runId));
        long generation = Interlocked.Increment(ref loadGeneration);
        ClearLoadedData();
        SetLoading();
        try
        {
            if (gateway is not IRunEvidenceGateway evidenceGateway)
            {
                SetFailure([new AppErrorDto(GatewayErrorCodes.CapabilityUnavailable, SeverityNames.Blocking,
                    "已保存结果读取功能不可用。", "请使用支持证据读取的工作区后重试。")], Guid.NewGuid());
                return false;
            }

            GatewayResult<RunEvidenceDto> result = await evidenceGateway.GetRunEvidenceAsync(
                new GetRunQuery(Guid.NewGuid(), runId), cancellationToken);
            if (!IsCurrent(generation)) return false;
            if (!result.IsSuccess)
            {
                SetFailure(result.Errors, result.CorrelationId);
                return false;
            }
            ApplyEvidence(result.Value!, resetImportPreview: true);
            CompleteOperation(result.CorrelationId);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (IsCurrent(generation))
            {
                UiState = Run is null ? WorkbenchUiState.Empty : WorkbenchUiState.RecoverableError;
                NotifyDerivedState();
            }
            throw;
        }
        catch (Exception)
        {
            SetUnexpectedFailure(generation);
            return false;
        }
    }

    public void ShowEmptyWorkspace()
    {
        if (automaticWorkflowActive) return;
        Interlocked.Increment(ref loadGeneration);
        ClearLoadedData();
        UiState = WorkbenchUiState.Empty;
        ErrorCode = null;
        ErrorMessage = null;
        RecommendedAction = null;
        CorrelationId = null;
        NotifyDerivedState();
    }

    private void ClearLoadedData()
    {
        Run = null;
        snapshotIsCurrent = false;
        ProjectName = "尚未选择项目";
        RunState = "尚未加载";
        MethodIdentity = "尚未加载方法";
        CurrentEvidence = null;
        CurrentSource = null;
        CurrentPreview = null;
        currentSourceMediaType = null;
        CalibrationResult = null;
        ControlQcResult = null;
        SampleResults = [];
    }

    private bool IsCurrent(long generation) => Volatile.Read(ref loadGeneration) == generation;

    private void SetLoading()
    {
        UiState = WorkbenchUiState.Loading;
        ErrorCode = null;
        ErrorMessage = null;
        RecommendedAction = null;
        CorrelationId = null;
        NotifyDerivedState();
    }

    private void SetFailure(IReadOnlyList<AppErrorDto> errors, Guid correlation)
    {
        AppErrorDto error = errors[0];
        ErrorCode = error.Code;
        ErrorMessage = error.UserMessage;
        RecommendedAction = error.RecommendedAction;
        CorrelationId = correlation;
        UiState = error.Severity == SeverityNames.Fatal
            ? WorkbenchUiState.FatalError
            : WorkbenchUiState.RecoverableError;
        NotifyDerivedState();
    }

    private void NotifyDerivedState()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBusy)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasError)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GuidedStages)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanImport)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanConfirmMapping)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanAssignWells)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRunCalibration)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRunControlQc)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanAnalyzeSamples)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanExport)));
    }

    /// <summary>Inspects only a source already staged by an Infrastructure boundary.</summary>
    public async Task<ImportPreviewDto?> InspectImportAsync(
        SourceFileDto source,
        CancellationToken cancellationToken,
        IProgress<OperationProgressDto>? progress = null,
        ImportOptionsDto? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        string mediaType = MediaTypeFor(source.OriginalFileName);
        if (!TryStartOperation(out RunSnapshotDto snapshot, out long generation)) return null;
        CurrentPreview = null;
        CurrentSource = null;
        currentSourceMediaType = null;

        try
        {
            GatewayResult<ImportPreviewDto> result = await gateway.InspectImportAsync(
                new InspectImportCommand(Guid.NewGuid(), snapshot.RunId, source.OriginalFileName,
                    source.FileSizeBytes, source.RelativeResourcePath, mediaType, options: options),
                progress,
                cancellationToken);
            if (!IsCurrent(generation)) return null;
            if (!result.IsSuccess)
            {
                SetFailure(result.Errors, result.CorrelationId);
                return null;
            }

            CurrentSource = source;
            currentSourceMediaType = mediaType;
            CurrentPreview = result.Value;
            CompleteOperation(result.CorrelationId);
            return result.Value;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RestoreAfterCancellationAsync(snapshot.RunId, generation);
            throw;
        }
        catch (Exception)
        {
            SetUnexpectedFailure(generation);
            return null;
        }
    }

    /// <summary>Commits explicit user-confirmed FAM and ROX regions, never guessed candidates.</summary>
    public Task<bool> ConfirmMappingAsync(
        ChannelRegionMappingDto fam,
        ChannelRegionMappingDto rox,
        CancellationToken cancellationToken,
        ImportOptionsDto? options = null)
    {
        if (automaticWorkflowActive && !automaticStepAuthorized) return Task.FromResult(false);
        ArgumentNullException.ThrowIfNull(fam);
        ArgumentNullException.ThrowIfNull(rox);
        SourceFileDto? source = CurrentSource;
        string? mediaType = currentSourceMediaType;
        if (CurrentPreview is null || source is null || mediaType is null)
        {
            SetFailure(
                [new AppErrorDto("SYSTEM_NO_PREVIEW_LOADED", SeverityNames.Blocking,
                    "No managed import preview is loaded.", "Inspect a staged source before confirming its mapping.")],
                Guid.NewGuid());
            return Task.FromResult(false);
        }

        return ExecuteSnapshotMutationAsync(
            snapshot => gateway.ConfirmMappingAsync(
                new ConfirmMappingCommand(Guid.NewGuid(), snapshot.RunId, snapshot.Revision, actorId, timeProvider.GetUtcNow(),
                    fam, rox, source.OriginalFileName, source.RelativeResourcePath, source.Sha256,
                    source.FileSizeBytes, mediaType, options: options), cancellationToken),
            cancellationToken);
    }

    public Task<bool> AssignWellsAsync(
        IReadOnlyList<WellAssignmentDto> assignments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        return ExecuteSnapshotMutationAsync(
            snapshot => gateway.AssignWellsAsync(
                new AssignWellsCommand(Guid.NewGuid(), snapshot.RunId, snapshot.Revision, actorId,
                    timeProvider.GetUtcNow(), assignments), cancellationToken),
            cancellationToken);
    }

    public Task<bool> RunCalibrationAsync(CancellationToken cancellationToken) =>
        ExecuteStageMutationAsync(
            snapshot => gateway.RunCalibrationAsync(
                new RunCalibrationCommand(Guid.NewGuid(), snapshot.RunId, snapshot.Revision, actorId, timeProvider.GetUtcNow()),
                cancellationToken),
            result => CalibrationResult = result,
            cancellationToken);

    public Task<bool> RunControlQcAsync(CancellationToken cancellationToken) =>
        ExecuteStageMutationAsync(
            snapshot => gateway.RunControlQcAsync(
                new RunControlQcCommand(Guid.NewGuid(), snapshot.RunId, snapshot.Revision, actorId, timeProvider.GetUtcNow()),
                cancellationToken),
            result => ControlQcResult = result,
            cancellationToken);

    public Task<bool> AnalyzeSamplesAsync(CancellationToken cancellationToken) =>
        ExecuteStageMutationAsync(
            snapshot => gateway.AnalyzeSamplesAsync(
                new AnalyzeSamplesCommand(Guid.NewGuid(), snapshot.RunId, snapshot.Revision, actorId, timeProvider.GetUtcNow()),
                cancellationToken),
            result => SampleResults = result,
            cancellationToken);

    private async Task<bool> ExecuteSnapshotMutationAsync(
        Func<RunSnapshotDto, Task<GatewayResult<RunSnapshotDto>>> operation,
        CancellationToken cancellationToken)
    {
        if (!TryStartOperation(out RunSnapshotDto snapshot, out long generation)) return false;
        try
        {
            GatewayResult<RunSnapshotDto> result = await operation(snapshot);
            if (!IsCurrent(generation)) return false;
            if (!result.IsSuccess)
            {
                await RefreshAfterMutationFailureAsync(snapshot.RunId, result.Errors, result.CorrelationId, generation, cancellationToken);
                return false;
            }

            if (!await RefreshStoredRunAsync(snapshot.RunId, generation, cancellationToken)) return false;
            CompleteOperation(result.CorrelationId);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RestoreAfterCancellationAsync(snapshot.RunId, generation);
            throw;
        }
        catch (Exception)
        {
            SetUnexpectedFailure(generation);
            return false;
        }
    }

    private async Task<bool> ExecuteStageMutationAsync<T>(
        Func<RunSnapshotDto, Task<GatewayResult<T>>> operation,
        Action<T> publishResult,
        CancellationToken cancellationToken)
    {
        if (!TryStartOperation(out RunSnapshotDto snapshot, out long generation)) return false;
        try
        {
            GatewayResult<T> result = await operation(snapshot);
            if (!IsCurrent(generation)) return false;
            if (!result.IsSuccess)
            {
                await RefreshAfterMutationFailureAsync(snapshot.RunId, result.Errors, result.CorrelationId, generation, cancellationToken);
                return false;
            }

            if (!await RefreshStoredRunAsync(snapshot.RunId, generation, cancellationToken)) return false;
            if (gateway is not IRunEvidenceGateway) publishResult(result.Value!);
            CompleteOperation(result.CorrelationId);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RestoreAfterCancellationAsync(snapshot.RunId, generation);
            throw;
        }
        catch (Exception)
        {
            SetUnexpectedFailure(generation);
            return false;
        }
    }

    private async Task RefreshAfterMutationFailureAsync(Guid runId, IReadOnlyList<AppErrorDto> errors,
        Guid correlation, long generation, CancellationToken cancellationToken)
    {
        await RefreshStoredRunAsync(runId, generation, cancellationToken);
        if (!IsCurrent(generation)) return;
        SetFailure(errors, correlation);
    }

    private async Task<bool> RefreshStoredRunAsync(Guid runId, long generation, CancellationToken cancellationToken)
    {
        if (gateway is IRunEvidenceGateway evidenceGateway)
        {
            GatewayResult<RunEvidenceDto> evidence = await evidenceGateway.GetRunEvidenceAsync(
                new GetRunQuery(Guid.NewGuid(), runId), cancellationToken);
            if (!IsCurrent(generation)) return false;
            if (!evidence.IsSuccess)
            {
                snapshotIsCurrent = false;
                SetFailure(evidence.Errors, evidence.CorrelationId);
                return false;
            }
            ApplyEvidence(evidence.Value!, resetImportPreview: false);
            return true;
        }

        GatewayResult<RunSnapshotDto> result = await gateway.GetRunAsync(new GetRunQuery(Guid.NewGuid(), runId), cancellationToken);
        if (!IsCurrent(generation)) return false;
        if (!result.IsSuccess)
        {
            snapshotIsCurrent = false;
            SetFailure(result.Errors, result.CorrelationId);
            return false;
        }
        ApplySnapshot(result.Value!);
        return true;
    }

    private void ApplyEvidence(RunEvidenceDto evidence, bool resetImportPreview)
    {
        ApplySnapshot(evidence.Snapshot);
        ProjectName = evidence.ProjectName;
        CurrentEvidence = evidence;
        if (resetImportPreview)
        {
            CurrentPreview = null;
            CurrentSource = null;
            currentSourceMediaType = null;
        }
        if (evidence.Snapshot.SourceFiles.Count > 0)
        {
            SourceFileDto source = evidence.Snapshot.SourceFiles[0];
            CurrentSource = source;
            currentSourceMediaType = MediaTypeFor(source.OriginalFileName);
        }
        CalibrationResult = evidence.Calibration;
        ControlQcResult = evidence.Control;
        SampleResults = evidence.Samples;
    }

    private bool TryStartOperation(out RunSnapshotDto snapshot, out long generation)
    {
        snapshot = Run!;
        generation = 0;
        if (UiState == WorkbenchUiState.Loading || automaticWorkflowActive && !automaticStepAuthorized
            || (Run is not null && !snapshotIsCurrent)) return false;
        if (snapshot is null)
        {
            SetFailure(
                [new AppErrorDto("SYSTEM_NO_RUN_LOADED", SeverityNames.Blocking,
                    "No Run is loaded.", "Initialize the workbench first.")],
                Guid.NewGuid());
            return false;
        }

        generation = Interlocked.Increment(ref loadGeneration);
        SetLoading();
        return true;
    }

    private void ApplySnapshot(RunSnapshotDto snapshot)
    {
        snapshotIsCurrent = true;
        Run = snapshot;
        RunState = snapshot.DomainState;
        MethodIdentity = $"{snapshot.MethodId} v{snapshot.MethodVersion}";
    }

    private void CompleteOperation(Guid correlation)
    {
        CorrelationId = correlation;
        UiState = WorkbenchUiState.Ready;
        NotifyDerivedState();
    }

    private async Task RestoreAfterCancellationAsync(Guid runId, long generation)
    {
        if (!IsCurrent(generation)) return;

        // A cancellation can arrive after an atomic backend mutation has
        // committed but before its response reaches the UI. Refresh with a
        // non-cancelled token so the shell cannot keep an obsolete revision and
        // make the next user action fail with a stale-revision error.
        try
        {
            using var refreshTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await RefreshStoredRunAsync(runId, generation, refreshTimeout.Token);
        }
        catch (Exception)
        {
            // The original cancellation remains the user-visible outcome. A
            // later explicit refresh can recover if the read side is down.
            if (IsCurrent(generation)) snapshotIsCurrent = false;
        }

        if (!IsCurrent(generation)) return;
        UiState = snapshotIsCurrent ? WorkbenchUiState.Ready : WorkbenchUiState.RecoverableError;
        NotifyDerivedState();
    }

    private void SetUnexpectedFailure(long generation)
    {
        if (!IsCurrent(generation)) return;
        snapshotIsCurrent = false;
        SetFailure(
            [new AppErrorDto("SYSTEM_WORKBENCH_OPERATION_FAILED", SeverityNames.Blocking,
                "The operation could not be completed.", "Refresh the Run and retry the operation.")],
            Guid.NewGuid());
    }

    private bool HasAction(string action) =>
        !IsBusy && snapshotIsCurrent && Run?.AllowedActions.Contains(action, StringComparer.Ordinal) == true;

    private static string MediaTypeFor(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".xlsx" => SourceFileDto.MediaTypeNames.Xlsx,
        ".xls" => SourceFileDto.MediaTypeNames.Xls,
        ".csv" => SourceFileDto.MediaTypeNames.Csv,
        ".tsv" => SourceFileDto.MediaTypeNames.Tsv,
        ".txt" => SourceFileDto.MediaTypeNames.Txt,
        _ => throw new ArgumentException("The managed source format is unsupported.", nameof(name)),
    };
    private void SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value)) return;
        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
