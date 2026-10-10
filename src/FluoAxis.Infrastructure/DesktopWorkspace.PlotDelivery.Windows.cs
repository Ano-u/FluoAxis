namespace FluoAxis.Infrastructure;

using FluoAxis.Contracts;
using FluoAxis.PlotProtocol;
using FluoAxis.Visualization;
using Windows.Storage;

public sealed partial class DesktopWorkspace
{
    public static async Task<GatewayResult<bool>?> ExportPlotImageAsync(nint owner, byte[] bytes, string extension, CancellationToken cancellationToken)
    {
        try
        {
            if (extension is not ("png" or "svg") || bytes.Length is 0 or > 32 * 1024 * 1024) return DeliveryFailure<bool>();
            var selected = await SelectDestinationAsync(owner, extension, "FluoAxis-research-plot", cancellationToken);
            if (selected is null) return null;
            return await WriteBytesTransactionAsync(selected, bytes, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return DeliveryFailure<bool>(); }
    }

    public static async Task<GatewayResult<bool>?> ExportOfflinePlotAsync(nint owner, PlotSpec spec, PlotDataSnapshot data, CancellationToken cancellationToken)
    {
        try
        {
            var selected = await SelectDestinationAsync(owner, "html", "FluoAxis-offline-research-plot", cancellationToken);
            if (selected is null) return null;
            // Only the platform adapter resolves the packaged asset at runtime.
            byte[] renderer = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "PlotHost", "vendor", "plotly.min.js"), cancellationToken);
            byte[] document = await Task.Run(() => OfflinePlotDocument.Create(spec, data, renderer), cancellationToken);
            return await WriteBytesTransactionAsync(selected, document, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return DeliveryFailure<bool>(); }
    }

    private static async Task<GatewayResult<bool>> WriteBytesTransactionAsync(StorageFile selected, byte[] bytes, CancellationToken cancellationToken)
    {
        using StorageStreamTransaction transaction = await selected.OpenTransactedWriteAsync();
        using Stream output = transaction.Stream.AsStreamForWrite();
        output.SetLength(0);
        await output.WriteAsync(bytes, cancellationToken);
        await output.FlushAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync();
        return GatewayResult.Ok(true);
    }
}
