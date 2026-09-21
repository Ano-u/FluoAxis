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

/// <summary>Presentation state for the Phase 1 capability-honest workbench shell.</summary>
public sealed class WorkbenchViewModel : INotifyPropertyChanged
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

    public WorkbenchViewModel(IFluoAxisGateway gateway)
    {
        this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
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

    public bool IsBusy => UiState == WorkbenchUiState.Loading;

    public bool HasError => ErrorCode is not null;
    public bool CanImport => HasAction(RunAction.Import);
    public bool CanConfirmMapping => HasAction(RunAction.ConfirmMapping);
    public bool CanAssignWells => HasAction(RunAction.AssignWells);
    public bool CanRunCalibration => HasAction(RunAction.RunCalibration);
    public bool CanRunControlQc => HasAction(RunAction.RunControlQc);
    public bool CanAnalyzeSamples => HasAction(RunAction.AnalyzeSamples);
    public bool CanExport => HasAction(RunAction.Export);

    public bool IsStageAvailable(GuidedStageItem stage) =>
        HasAction(stage.RequiredAction);

    public void SelectArea(string area)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(area);
        ActiveArea = area;
    }

    public async Task InitializeAsync(string requestedProjectName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedProjectName);
        long generation = Interlocked.Increment(ref loadGeneration);
        SetLoading();

        try
        {
            GatewayResult<ProjectSnapshotDto> created = await gateway.CreateProjectAsync(
                new CreateProjectCommand(Guid.NewGuid(), requestedProjectName, "local-researcher", DateTimeOffset.UtcNow),
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
            Run = loaded.Value;
            RunState = loaded.Value!.DomainState;
            MethodIdentity = $"{loaded.Value.MethodId} v{loaded.Value.MethodVersion}";
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

    private bool HasAction(string action) =>
        Run?.AllowedActions.Contains(action, StringComparer.Ordinal) == true;

    private void SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value)) return;
        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
