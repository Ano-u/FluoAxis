namespace FluoAxis.Application;

using FluoAxis.Analysis;
using FluoAxis.Contracts;
using FluoAxis.Domain;

/// <summary>In-process implementation of the presentation-to-Application gateway.</summary>
public sealed partial class ApplicationGateway : IFluoAxisGateway
{
    private readonly IProjectRunRepository repository;
    private readonly IImportInspectionPort? importInspection;
    private readonly ISignalPreparationPort? signalPreparation;
    private readonly IAnalysisEngine engine;
    private readonly TimeProvider timeProvider;

    public ApplicationGateway(
        IProjectRunRepository repository,
        TimeProvider? timeProvider = null,
        IImportInspectionPort? importInspection = null,
        ISignalPreparationPort? signalPreparation = null,
        IAnalysisEngine? engine = null)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.importInspection = importInspection;
        this.signalPreparation = signalPreparation;
        this.engine = engine ?? new BrafAnalysisEngine();
    }

    public async Task<GatewayResult<ProjectSnapshotDto>> CreateProjectAsync(
        CreateProjectCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        GatewayResult<ProjectSnapshotDto>? versionFailure = ValidateVersion<ProjectSnapshotDto>(command.ContractVersion);
        if (versionFailure is not null)
        {
            return versionFailure;
        }

        var identity = new CreateProjectIdentity(
            command.RequestId, command.ProjectName, command.ActorId, command.IssuedUtc, command.ContractVersion);

        CreateProjectStoreResult stored = await repository.CreateOrReplayAsync(
            identity,
            () => CreateAggregate(command),
            cancellationToken).ConfigureAwait(false);

        if (stored.IsConflict)
        {
            return Fail<ProjectSnapshotDto>(
                ErrorCodes.SystemRequestIdConflict,
                "The request ID was already used with different project-creation data.",
                "Submit the operation with a new request ID.");
        }

        return GatewayResult.Ok(ToProjectSnapshot(stored.Aggregate));
    }

    public async Task<GatewayResult<ImportPreviewDto>> InspectImportAsync(
        InspectImportCommand command,
        IProgress<OperationProgressDto>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        GatewayResult<ImportPreviewDto>? versionFailure = ValidateVersion<ImportPreviewDto>(command.ContractVersion);
        if (versionFailure is not null)
        {
            return versionFailure;
        }

        ProjectRunAggregate? aggregate = await repository.GetByRunIdAsync(command.RunId, cancellationToken).ConfigureAwait(false);
        if (aggregate is null)
        {
            return Fail<ImportPreviewDto>(
                ErrorCodes.ProjectRunNotFound,
                "The requested Run does not exist.",
                "Refresh the project list and select an existing Run.");
        }

        if (aggregate.RunState != RunState.New)
        {
            return Fail<ImportPreviewDto>(
                ErrorCodes.SystemInvalidRunState,
                "Import inspection is allowed only while the Run is new.",
                "Refresh the Run snapshot and use one of its allowed actions.");
        }

        if (importInspection is null)
        {
            return CapabilityUnavailable<ImportPreviewDto>(
                "Import inspection requires a managed-resource Infrastructure adapter.");
        }

        var request = new ManagedImportRequest(
            command.RunId,
            command.OriginalFileName,
            command.FileSizeBytes,
            RelativePath.Parse(command.RelativeResourcePath),
            command.FormatHint);
        Result<ImportPreviewDto> inspected = await importInspection
            .InspectAsync(request, progress, cancellationToken)
            .ConfigureAwait(false);
        return inspected.ToGatewayResult(value => value, error => error.ToDto(RecommendedImportAction(error.Code)));
    }

    public Task<GatewayResult<RunSnapshotDto>> ConfirmMappingAsync(ConfirmMappingCommand command, CancellationToken cancellationToken) =>
        ConfirmMappingCoreAsync(command, cancellationToken);

    public Task<GatewayResult<RunSnapshotDto>> AssignWellsAsync(AssignWellsCommand command, CancellationToken cancellationToken) =>
        AssignWellsCoreAsync(command, cancellationToken);

    public Task<GatewayResult<CalibrationResultDto>> RunCalibrationAsync(
        RunCalibrationCommand command,
        CancellationToken cancellationToken) =>
        RunCalibrationCoreAsync(command, cancellationToken);

    public Task<GatewayResult<ControlQcResultDto>> RunControlQcAsync(
        RunControlQcCommand command,
        CancellationToken cancellationToken) =>
        RunControlQcCoreAsync(command, cancellationToken);

    public Task<GatewayResult<IReadOnlyList<SampleAnalysisDto>>> AnalyzeSamplesAsync(
        AnalyzeSamplesCommand command,
        CancellationToken cancellationToken) =>
        AnalyzeSamplesCoreAsync(command, cancellationToken);

    public async Task<GatewayResult<RunSnapshotDto>> GetRunAsync(GetRunQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        GatewayResult<RunSnapshotDto>? versionFailure = ValidateVersion<RunSnapshotDto>(query.ContractVersion);
        if (versionFailure is not null)
        {
            return versionFailure;
        }

        ProjectRunAggregate? aggregate = await repository.GetByRunIdAsync(query.RunId, cancellationToken).ConfigureAwait(false);
        return aggregate is null
            ? Fail<RunSnapshotDto>(ErrorCodes.ProjectRunNotFound, "The requested Run does not exist.", "Refresh the project list and select an existing Run.")
            : GatewayResult.Ok(await BuildSnapshotAsync(aggregate, cancellationToken).ConfigureAwait(false));
    }

    private ProjectRunAggregate CreateAggregate(CreateProjectCommand command)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        return new ProjectRunAggregate
        {
            ProjectId = Guid.CreateVersion7(),
            ProjectName = command.ProjectName,
            CreatedAtUtc = now,
            RunId = Guid.CreateVersion7(),
            RunRevision = 0,
            RunState = RunState.New,
            Method = BrafMethodSnapshot.Default,
            UpdatedUtc = now,
        };
    }

    private static ProjectSnapshotDto ToProjectSnapshot(ProjectRunAggregate aggregate) => new(
        aggregate.ProjectId, aggregate.ProjectName, aggregate.RunId, aggregate.RunRevision,
        RunStateNames.FromDomain(aggregate.RunState), aggregate.CreatedAtUtc);

    /// <summary>
    /// Pre-flight validation shared by every mutating workflow command. The
    /// order is deliberate and tested: contract version, then Run existence,
    /// then the observed revision, then the current state. Returns the
    /// loaded aggregate on success and null on any refusal; the caller turns
    /// a null into the matching structured error.
    /// </summary>
    private async Task<ProjectRunAggregate?> ValidateMutationAsync(
        CommandBase command, RunState expectedState, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (command.ContractVersion != ContractVersion.Current)
        {
            return null;
        }

        ProjectRunAggregate? aggregate = await repository.GetByRunIdAsync(command.RunId, cancellationToken).ConfigureAwait(false);
        if (aggregate is null || aggregate.RunRevision != command.ExpectedRunRevision || aggregate.RunState != expectedState)
        {
            return null;
        }

        return aggregate;
    }

    /// <summary>
    /// Classifies the same pre-flight checks into a structured error, so a
    /// refusal tells the operator whether to refresh, restage, or correct.
    /// </summary>
    private async Task<AppErrorDto?> ClassifyMutationFailureAsync(
        CommandBase command, RunState expectedState, CancellationToken cancellationToken)
    {
        if (command.ContractVersion != ContractVersion.Current)
        {
            return Error(
                ErrorCodes.SystemUnsupportedContractVersion,
                "The frontend-backend contract version is not supported.",
                "Refresh or update the application components together.");
        }

        ProjectRunAggregate? aggregate = await repository.GetByRunIdAsync(command.RunId, cancellationToken).ConfigureAwait(false);
        if (aggregate is null)
        {
            return Error(ErrorCodes.ProjectRunNotFound, "The requested Run does not exist.", "Refresh the project list and select an existing Run.");
        }

        if (aggregate.RunRevision != command.ExpectedRunRevision)
        {
            return Error(ErrorCodes.SystemStaleRunRevision, "The Run changed after this command was prepared.", "Refresh the Run snapshot before trying again.");
        }

        if (aggregate.RunState != expectedState)
        {
            return Error(ErrorCodes.SystemInvalidRunState, "The operation is not allowed in the current Run state.", "Refresh the Run snapshot and use one of its allowed actions.");
        }

        return null;
    }

    private static GatewayResult<T>? ValidateVersion<T>(int version) => version == ContractVersion.Current
        ? null
        : Fail<T>(ErrorCodes.SystemUnsupportedContractVersion, "The frontend-backend contract version is not supported.", "Refresh or update the application components together.");

    private static GatewayResult<T> CapabilityUnavailable<T>(string reason) =>
        Fail<T>(ErrorCodes.SystemCapabilityUnavailable, reason, "Complete the required data contract and adapter before retrying.");

    private static GatewayResult<T> Fail<T>(string code, string message, string action) =>
        GatewayResult.Fail<T>(Error(code, message, action));

    private static AppErrorDto Error(string code, string message, string action) =>
        new(code, SeverityNames.Blocking, message, action);

    private static string RecommendedImportAction(string code) => code switch
    {
        ErrorCodes.ImportManagedResourceNotFound => "Select the source again and restage it in the project workspace.",
        ErrorCodes.ImportManagedResourceSizeMismatch => "Restage the source and inspect the unchanged managed copy again.",
        ErrorCodes.ImportManagedResourceUnsafe => "Choose a regular file stored directly inside the managed project workspace.",
        _ => "Review the source format and import mapping, then retry with a supported unchanged file.",
    };
}
