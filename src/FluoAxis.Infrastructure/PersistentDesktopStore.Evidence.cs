namespace FluoAxis.Infrastructure;

using FluoAxis.Contracts;

public sealed partial class PersistentDesktopStore
{
    public Task<GatewayResult<ScientificPointPageDto>> GetScientificPointPageAsync(Guid runId, string stage, string well,
        string channel, int offset, int pageSize, CancellationToken cancellationToken) =>
        ExecuteAsync(() => gateway.GetScientificPointPageAsync(runId, stage, well, channel, offset, pageSize, cancellationToken), cancellationToken);
}
