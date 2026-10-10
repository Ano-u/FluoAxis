namespace FluoAxis.Contracts;

/// <summary>Optional read-only evidence capability; does not expand the v3 mutation gateway.</summary>
public interface IRunEvidenceGateway
{
    Task<GatewayResult<RunEvidenceDto>> GetRunEvidenceAsync(
        GetRunQuery query,
        CancellationToken cancellationToken);
}
