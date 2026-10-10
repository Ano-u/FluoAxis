namespace FluoAxis.Infrastructure;

using FluoAxis.Contracts;

public sealed partial class DesktopWorkspace
{
    internal DesktopWorkspace(string validationRoot) => store = new PersistentDesktopStore(validationRoot);

#if DEBUG
    public static DesktopWorkspace CreateValidationWorkspace(string relativeEntry)
    {
        string relative = FluoAxis.Domain.RelativePath.Parse(relativeEntry).Value;
        if (!relative.StartsWith("artifacts/test-tmp/", StringComparison.Ordinal)) throw new ArgumentException("Validation workspaces must stay in test artifacts.", nameof(relativeEntry));
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "FluoAxis.slnx"))) repository = repository.Parent;
        if (repository is null) throw new InvalidOperationException("VALIDATION_REPOSITORY_UNAVAILABLE");
        return new DesktopWorkspace(Path.Combine(repository.FullName, relative));
    }

    public async Task SeedValidationPackageAsync(string relativeEntry)
    {
        string relative = FluoAxis.Domain.RelativePath.Parse(relativeEntry).Value;
        if (!relative.StartsWith("artifacts/ui-evidence/", StringComparison.Ordinal)) throw new ArgumentException("Validation seeds must be UI evidence artifacts.", nameof(relativeEntry));
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "FluoAxis.slnx"))) repository = repository.Parent;
        if (repository is null) throw new InvalidOperationException("VALIDATION_REPOSITORY_UNAVAILABLE");
        var result = await store.ImportProjectPackageAsync(Path.Combine(repository.FullName, relative), CancellationToken.None);
        if (!result.IsSuccess) throw new InvalidOperationException("VALIDATION_PACKAGE_FAILED");
    }

    public Task<GatewayResult<SourceFileDto>> StageValidationSourceAsync(string relativeEntry)
    {
        string relative = FluoAxis.Domain.RelativePath.Parse(relativeEntry).Value;
        if (!relative.StartsWith("artifacts/ui-evidence/", StringComparison.Ordinal)) throw new ArgumentException("Validation inputs must be UI evidence artifacts.", nameof(relativeEntry));
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "FluoAxis.slnx"))) repository = repository.Parent;
        if (repository is null) throw new InvalidOperationException("VALIDATION_REPOSITORY_UNAVAILABLE");
        return store.StageAsync(Path.Combine(repository.FullName, relative), CancellationToken.None);
    }
#endif
    public Task<GatewayResult<WorkspaceDiagnosticsDto>> GetDiagnosticsAsync(CancellationToken cancellationToken) => store.GetDiagnosticsAsync(cancellationToken);
    public Task<GatewayResult<bool>> AttachReportPlotFigureAsync(ReportPlotFigureDto figure, CancellationToken cancellationToken) => store.AttachReportPlotFigureAsync(figure, cancellationToken);
    public Task<GatewayResult<ScientificPointPageDto>> GetScientificPointPageAsync(Guid runId, string stage, string well,
        string channel, int offset, int pageSize, CancellationToken cancellationToken) =>
        store.GetScientificPointPageAsync(runId, stage, well, channel, offset, pageSize, cancellationToken);

    public async Task<GatewayResult<bool>?> ExportSupportBundleAsync(nint owner, CancellationToken cancellationToken)
    {
        try
        {
            var selected = await SelectDestinationAsync(owner, "zip", "FluoAxis-support", cancellationToken);
            if (selected is null) return null;
            GatewayResult<SupportBundleDto> bundle = await store.CreateSupportBundleAsync(cancellationToken);
            if (!bundle.IsSuccess) return GatewayResult.Fail<bool>(bundle.Errors.ToArray());
            return await WriteManagedTransactionAsync(selected, bundle.Value!.RelativeEntry, bundle.Value.Sha256, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return DeliveryFailure<bool>(); }
    }
}
