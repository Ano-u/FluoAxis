namespace FluoAxis.Contracts;

/// <summary>
/// The single frontend-backend boundary (plan.md §5.1). Presentation code
/// calls only these methods; it never touches repositories, domain types,
/// or SQL. The Application layer implements the in-process adapter.
/// Responses use the uniform <see cref="GatewayResult{T}"/> envelope; every
/// method takes a <see cref="CancellationToken"/> and the long-running
/// inspection reports progress.
/// </summary>
public interface IFluoAxisGateway
{
    Task<GatewayResult<ProjectSnapshotDto>> CreateProjectAsync(
        CreateProjectCommand command,
        CancellationToken cancellationToken);

    Task<GatewayResult<ImportPreviewDto>> InspectImportAsync(
        InspectImportCommand command,
        IProgress<OperationProgressDto>? progress,
        CancellationToken cancellationToken);

    Task<GatewayResult<RunSnapshotDto>> ConfirmMappingAsync(
        ConfirmMappingCommand command,
        CancellationToken cancellationToken);

    Task<GatewayResult<RunSnapshotDto>> AssignWellsAsync(
        AssignWellsCommand command,
        CancellationToken cancellationToken);

    Task<GatewayResult<CalibrationResultDto>> RunCalibrationAsync(
        RunCalibrationCommand command,
        CancellationToken cancellationToken);

    Task<GatewayResult<ControlQcResultDto>> RunControlQcAsync(
        RunControlQcCommand command,
        CancellationToken cancellationToken);

    Task<GatewayResult<IReadOnlyList<SampleAnalysisDto>>> AnalyzeSamplesAsync(
        AnalyzeSamplesCommand command,
        CancellationToken cancellationToken);

    Task<GatewayResult<RunSnapshotDto>> GetRunAsync(
        GetRunQuery query,
        CancellationToken cancellationToken);
}
