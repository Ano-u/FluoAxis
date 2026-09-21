namespace FluoAxis.Contracts.Serialization;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// System.Text.Json source-generation context for the whole wire contract.
/// Every public DTO that crosses the frontend-backend boundary is
/// registered here, so serialization is trim-safe and reflection-free.
/// Settings are part of the frozen contract (plan.md §5.6): camelCase
/// property names, null values omitted. Enum-like values are plain strings
/// already, so no enum converter applies.
/// </summary>
[JsonSerializable(typeof(CommandBase))]
[JsonSerializable(typeof(CreateProjectCommand))]
[JsonSerializable(typeof(InspectImportCommand))]
[JsonSerializable(typeof(ConfirmMappingCommand))]
[JsonSerializable(typeof(AssignWellsCommand))]
[JsonSerializable(typeof(RunCalibrationCommand))]
[JsonSerializable(typeof(RunControlQcCommand))]
[JsonSerializable(typeof(AnalyzeSamplesCommand))]
[JsonSerializable(typeof(GetRunQuery))]
[JsonSerializable(typeof(WellAssignmentDto))]
[JsonSerializable(typeof(WellColumnMappingDto))]
[JsonSerializable(typeof(ChannelRegionMappingDto))]
[JsonSerializable(typeof(RegionCandidateDto))]
[JsonSerializable(typeof(SourceFileDto))]
[JsonSerializable(typeof(IssueDto))]
[JsonSerializable(typeof(WellDiagnosticDto))]
[JsonSerializable(typeof(CalibrationResultDto))]
[JsonSerializable(typeof(ControlQcResultDto))]
[JsonSerializable(typeof(SampleAnalysisDto))]
[JsonSerializable(typeof(PerWellResultDto))]
[JsonSerializable(typeof(RunSnapshotDto))]
[JsonSerializable(typeof(MappingSummaryDto))]
[JsonSerializable(typeof(CalibrationSummaryDto))]
[JsonSerializable(typeof(ControlSummaryDto))]
[JsonSerializable(typeof(AnalysisSummaryDto))]
[JsonSerializable(typeof(AppErrorDto))]
[JsonSerializable(typeof(OperationProgressDto))]
[JsonSerializable(typeof(ImportPreviewDto))]
[JsonSerializable(typeof(ProjectSnapshotDto))]
[JsonSerializable(typeof(GatewayResult<ProjectSnapshotDto>))]
[JsonSerializable(typeof(GatewayResult<ImportPreviewDto>))]
[JsonSerializable(typeof(GatewayResult<RunSnapshotDto>))]
[JsonSerializable(typeof(GatewayResult<CalibrationResultDto>))]
[JsonSerializable(typeof(GatewayResult<ControlQcResultDto>))]
[JsonSerializable(typeof(GatewayResult<IReadOnlyList<SampleAnalysisDto>>))]
[JsonSerializable(typeof(GatewayResult<GetRunQuery>))]
public sealed partial class ContractJsonContext : JsonSerializerContext
{
    /// <summary>
    /// Options matching the frozen contract: camelCase, nulls ignored,
    /// invariant, strictly round-trippable numbers (default writer keeps
    /// full precision). The source generator emits a constructor taking
    /// these options, which becomes the inherited
    /// <see cref="JsonSerializerContext.Options"/>.
    /// </summary>
    public static readonly JsonSerializerOptions WireOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}
