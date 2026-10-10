namespace FluoAxis.Infrastructure;

using FluoAxis.Contracts;

public sealed partial class DesktopWorkspace : IPlateProfileGateway
{
    public Task<GatewayResult<PlateProfileDto>> SavePlateProfileAsync(string name, IReadOnlyList<WellAssignmentDto> assignments,
        bool remainingWellsAreSamples, bool makeDefault, CancellationToken cancellationToken, ImportOptionsDto? options = null) =>
        store.SavePlateProfileAsync(name, assignments, remainingWellsAreSamples, makeDefault, cancellationToken, options);
    public Task<GatewayResult<IReadOnlyList<PlateProfileDto>>> ListPlateProfilesAsync(CancellationToken cancellationToken) => store.ListPlateProfilesAsync(cancellationToken);
    public Task<GatewayResult<PlateProfileDto?>> GetPlateProfileAsync(Guid profileId, CancellationToken cancellationToken) => store.GetPlateProfileAsync(profileId, cancellationToken);
    public Task<GatewayResult<PlateProfileDto>> SetDefaultPlateProfileAsync(Guid profileId, CancellationToken cancellationToken) => store.SetDefaultPlateProfileAsync(profileId, cancellationToken);
    public Task<GatewayResult<ExplorerPairPageDto>> GetExplorerPairPageAsync(Guid runId, string well, int offset, int pageSize, CancellationToken token) => store.GetExplorerPairPageAsync(runId, well, offset, pageSize, token);
    public Task<GatewayResult<SavedExplorerViewDto>> SaveExplorerViewAsync(ExplorerViewDefinitionDto definition, CancellationToken token) => store.SaveExplorerViewAsync(definition, token);
    public Task<GatewayResult<SavedExplorerViewDto?>> LoadExplorerViewAsync(Guid runId, CancellationToken token) => store.LoadExplorerViewAsync(runId, token);
}
