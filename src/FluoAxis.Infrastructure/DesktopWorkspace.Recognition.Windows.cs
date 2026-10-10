namespace FluoAxis.Infrastructure;

using FluoAxis.Contracts;

public sealed partial class DesktopWorkspace
{
    public Task<GatewayResult<AutomaticImportProposalDto>> RecognizeImportAsync(SourceFileDto source,
        CancellationToken cancellationToken, ImportOptionsDto? configuredOptions = null) =>
        store.RecognizeImportAsync(source, cancellationToken, configuredOptions);
}
