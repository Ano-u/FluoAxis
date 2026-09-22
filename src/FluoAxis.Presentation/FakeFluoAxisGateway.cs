namespace FluoAxis.Presentation;

using FluoAxis.Contracts;

/// <summary>Method identity rendered by the frontend Fake Gateway.</summary>
public sealed record FakeMethodIdentity
{
    public string MethodId { get; }
    public string MethodVersion { get; }
    public string MethodSnapshotHash { get; }

    public FakeMethodIdentity(string methodId, string methodVersion, string methodSnapshotHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(methodId);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodVersion);
        if (methodSnapshotHash.Length != 64 || methodSnapshotHash.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("MethodSnapshotHash must be a 64-character SHA-256 hex value.", nameof(methodSnapshotHash));
        }

        MethodId = methodId;
        MethodVersion = methodVersion;
        MethodSnapshotHash = methodSnapshotHash.ToLowerInvariant();
    }
}

/// <summary>
/// Presentation-only development adapter for the capabilities already provided
/// by the real Application Gateway. Unsupported vertical operations remain
/// explicit failures so frontend fixtures cannot overstate backend readiness.
/// </summary>
public sealed class FakeFluoAxisGateway : IFluoAxisGateway
{
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, (CreateIdentity Identity, ProjectSnapshotDto Project)> requests = [];
    private readonly Dictionary<Guid, RunSnapshotDto> runs = [];
    private readonly FakeMethodIdentity method;
    private readonly TimeProvider timeProvider;
    private readonly Func<Guid> idFactory;
    private readonly Func<InspectImportCommand, IProgress<OperationProgressDto>?, CancellationToken, Task<GatewayResult<ImportPreviewDto>>>? importInspection;

    public FakeFluoAxisGateway(
        FakeMethodIdentity method,
        TimeProvider? timeProvider = null,
        Func<Guid>? idFactory = null,
        Func<InspectImportCommand, IProgress<OperationProgressDto>?, CancellationToken, Task<GatewayResult<ImportPreviewDto>>>? importInspection = null)
    {
        this.method = method ?? throw new ArgumentNullException(nameof(method));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.idFactory = idFactory ?? Guid.CreateVersion7;
        this.importInspection = importInspection;
    }

    public Task<GatewayResult<ProjectSnapshotDto>> CreateProjectAsync(
        CreateProjectCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        GatewayResult<ProjectSnapshotDto>? versionFailure = ValidateVersion<ProjectSnapshotDto>(command.ContractVersion);
        if (versionFailure is not null)
        {
            return Task.FromResult(versionFailure);
        }

        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var identity = new CreateIdentity(command.ProjectName, command.ActorId, command.IssuedUtc, command.ContractVersion);
            if (requests.TryGetValue(command.RequestId, out var existing))
            {
                return Task.FromResult(existing.Identity == identity
                    ? GatewayResult.Ok(existing.Project)
                    : Fail<ProjectSnapshotDto>(
                        GatewayErrorCodes.RequestIdConflict,
                        "The request ID was already used with different project-creation data.",
                        "Submit the operation with a new request ID."));
            }

            DateTimeOffset now = timeProvider.GetUtcNow();
            Guid projectId = idFactory();
            Guid runId = idFactory();
            var project = new ProjectSnapshotDto(projectId, command.ProjectName, runId, 0, RunStateNames.New, now);
            var run = new RunSnapshotDto(
                runId,
                0,
                RunStateNames.New,
                [RunAction.Import, RunAction.ConfirmMapping],
                blockingIssues: [],
                warningIssues: [],
                method.MethodId,
                method.MethodVersion,
                method.MethodSnapshotHash,
                sourceFiles: [],
                updatedUtc: now);
            requests.Add(command.RequestId, (identity, project));
            runs.Add(runId, run);
            return Task.FromResult(GatewayResult.Ok(project));
        }
    }

    public Task<GatewayResult<ImportPreviewDto>> InspectImportAsync(
        InspectImportCommand command,
        IProgress<OperationProgressDto>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        GatewayResult<ImportPreviewDto>? versionFailure = ValidateVersion<ImportPreviewDto>(command.ContractVersion);
        if (versionFailure is not null)
        {
            return Task.FromResult(versionFailure);
        }

        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!runs.TryGetValue(command.RunId, out RunSnapshotDto? run))
            {
                return Task.FromResult(Fail<ImportPreviewDto>(
                    GatewayErrorCodes.RunNotFound,
                    "The requested Run does not exist.",
                    "Refresh the project list and select an existing Run."));
            }

            if (run.DomainState != RunStateNames.New)
            {
                return Task.FromResult(Fail<ImportPreviewDto>(
                    GatewayErrorCodes.InvalidRunState,
                    "Import inspection is allowed only while the Run is new.",
                    "Refresh the Run snapshot and use one of its allowed actions."));
            }
        }

        return importInspection is null
            ? Task.FromResult(CapabilityUnavailable<ImportPreviewDto>(
                "Import inspection requires a managed-resource Infrastructure adapter."))
            : importInspection(command, progress, cancellationToken);
    }

    public Task<GatewayResult<RunSnapshotDto>> ConfirmMappingAsync(ConfirmMappingCommand command, CancellationToken cancellationToken) =>
        ValidateUnavailableMutationAsync(command, RunStateNames.New, "Mapping confirmation requires a staged managed source and a managed-resource Infrastructure adapter.", cancellationToken);

    public Task<GatewayResult<RunSnapshotDto>> AssignWellsAsync(AssignWellsCommand command, CancellationToken cancellationToken) =>
        ValidateUnavailableMutationAsync(command, RunStateNames.Imported, "Well assignment persistence is not available in this slice.", cancellationToken);

    public async Task<GatewayResult<CalibrationResultDto>> RunCalibrationAsync(
        RunCalibrationCommand command,
        CancellationToken cancellationToken)
    {
        AppErrorDto? failure = await ValidateMutationAsync(command, RunStateNames.Mapped, cancellationToken).ConfigureAwait(false);
        return failure is null
            ? CapabilityUnavailable<CalibrationResultDto>("Calibration orchestration requires an immutable prepared signal snapshot.")
            : GatewayResult.Fail<CalibrationResultDto>(failure);
    }

    public async Task<GatewayResult<ControlQcResultDto>> RunControlQcAsync(
        RunControlQcCommand command,
        CancellationToken cancellationToken)
    {
        AppErrorDto? failure = await ValidateMutationAsync(command, RunStateNames.CalibrationPassed, cancellationToken).ConfigureAwait(false);
        return failure is null
            ? CapabilityUnavailable<ControlQcResultDto>("Control QC orchestration requires persisted calibration inputs.")
            : GatewayResult.Fail<ControlQcResultDto>(failure);
    }

    public async Task<GatewayResult<IReadOnlyList<SampleAnalysisDto>>> AnalyzeSamplesAsync(
        AnalyzeSamplesCommand command,
        CancellationToken cancellationToken)
    {
        AppErrorDto? failure = await ValidateMutationAsync(command, RunStateNames.ReadyForAnalysis, cancellationToken).ConfigureAwait(false);
        return failure is null
            ? CapabilityUnavailable<IReadOnlyList<SampleAnalysisDto>>("Sample analysis requires an immutable prepared signal snapshot.")
            : GatewayResult.Fail<IReadOnlyList<SampleAnalysisDto>>(failure);
    }

    public Task<GatewayResult<RunSnapshotDto>> GetRunAsync(GetRunQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        GatewayResult<RunSnapshotDto>? versionFailure = ValidateVersion<RunSnapshotDto>(query.ContractVersion);
        if (versionFailure is not null)
        {
            return Task.FromResult(versionFailure);
        }

        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(runs.TryGetValue(query.RunId, out RunSnapshotDto? run)
                ? GatewayResult.Ok(run)
                : Fail<RunSnapshotDto>(
                    GatewayErrorCodes.RunNotFound,
                    "The requested Run does not exist.",
                    "Refresh the project list and select an existing Run."));
        }
    }

    private async Task<GatewayResult<RunSnapshotDto>> ValidateUnavailableMutationAsync(
        CommandBase command,
        string expectedState,
        string reason,
        CancellationToken cancellationToken)
    {
        AppErrorDto? failure = await ValidateMutationAsync(command, expectedState, cancellationToken).ConfigureAwait(false);
        return failure is null ? CapabilityUnavailable<RunSnapshotDto>(reason) : GatewayResult.Fail<RunSnapshotDto>(failure);
    }

    private Task<AppErrorDto?> ValidateMutationAsync(
        CommandBase command,
        string expectedState,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        if (command.ContractVersion != ContractVersion.Current)
        {
            return Task.FromResult<AppErrorDto?>(Error(
                GatewayErrorCodes.UnsupportedContractVersion,
                "The frontend-backend contract version is not supported.",
                "Refresh or update the application components together."));
        }

        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!runs.TryGetValue(command.RunId, out RunSnapshotDto? run))
            {
                return Task.FromResult<AppErrorDto?>(Error(
                    GatewayErrorCodes.RunNotFound,
                    "The requested Run does not exist.",
                    "Refresh the project list and select an existing Run."));
            }

            if (run.Revision != command.ExpectedRunRevision)
            {
                return Task.FromResult<AppErrorDto?>(Error(
                    GatewayErrorCodes.StaleRunRevision,
                    "The Run changed after this command was prepared.",
                    "Refresh the Run snapshot before trying again."));
            }

            return Task.FromResult<AppErrorDto?>(run.DomainState != expectedState
                ? Error(
                    GatewayErrorCodes.InvalidRunState,
                    "The operation is not allowed in the current Run state.",
                    "Refresh the Run snapshot and use one of its allowed actions.")
                : null);
        }
    }

    private static GatewayResult<T>? ValidateVersion<T>(int version) => version == ContractVersion.Current
        ? null
        : Fail<T>(GatewayErrorCodes.UnsupportedContractVersion, "The frontend-backend contract version is not supported.", "Refresh or update the application components together.");

    private static GatewayResult<T> CapabilityUnavailable<T>(string reason) =>
        Fail<T>(GatewayErrorCodes.CapabilityUnavailable, reason, "Complete the required data contract and adapter before retrying.");

    private static GatewayResult<T> Fail<T>(string code, string message, string action) =>
        GatewayResult.Fail<T>(Error(code, message, action));

    private static AppErrorDto Error(string code, string message, string action) =>
        new(code, SeverityNames.Blocking, message, action);

    private sealed record CreateIdentity(string ProjectName, string ActorId, DateTimeOffset IssuedUtc, int ContractVersion);
}
