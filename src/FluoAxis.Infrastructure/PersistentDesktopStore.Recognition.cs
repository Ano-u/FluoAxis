namespace FluoAxis.Infrastructure;

using FluoAxis.Contracts;

public sealed partial class PersistentDesktopStore
{
    public Task<GatewayResult<AutomaticImportProposalDto>> RecognizeImportAsync(SourceFileDto source,
        CancellationToken cancellationToken, ImportOptionsDto? configuredOptions = null) =>
        ExecuteAsync(() => new ManagedAutomaticImportAdapter(root).RecognizeAsync(source, cancellationToken, configuredOptions), cancellationToken);
}
