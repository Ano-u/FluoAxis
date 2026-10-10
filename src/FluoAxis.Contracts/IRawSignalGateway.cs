namespace FluoAxis.Contracts;

/// <summary>Read-only, bounded Raw RFU access for Explorer. This capability never calculates a scientific result.</summary>
public interface IRawSignalGateway
{
    Task<GatewayResult<RawSignalPageDto>> GetRawSignalPageAsync(
        Guid runId,
        string well,
        string channel,
        int offset,
        int pageSize,
        CancellationToken cancellationToken);
}
