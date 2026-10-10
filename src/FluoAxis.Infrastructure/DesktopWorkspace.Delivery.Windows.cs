namespace FluoAxis.Infrastructure;

using System.Security.Cryptography;
using FluoAxis.Contracts;
using FluoAxis.Reporting;
using Windows.Storage;
using Windows.Storage.Pickers;

public sealed partial class DesktopWorkspace
{
    public Task<GatewayResult<ReportSnapshotDto>> CreateReportSnapshotAsync(Guid runId, CancellationToken cancellationToken) =>
        store.CreateReportSnapshotAsync(runId, cancellationToken);

    public async Task<GatewayResult<ManagedProjectPackageDto>?> SaveProjectPackageAsync(nint owner, Guid runId, CancellationToken cancellationToken)
    {
        try
        {
            GatewayResult<ManagedProjectPackageDto> prepared = await store.CreateProjectPackageAsync(runId, cancellationToken);
            if (!prepared.IsSuccess) return prepared;
            ManagedProjectPackageDto package = prepared.Value!;
            StorageFile? selected = await SelectDestinationAsync(owner, "fluoaxisproj", "FluoAxis-project", cancellationToken);
            if (selected is null) return null;
            GatewayResult<bool> copied = await WriteManagedTransactionAsync(selected, package.RelativeResourcePath, package.Sha256, cancellationToken);
            return copied.IsSuccess ? GatewayResult.Ok(new ManagedProjectPackageDto(selected.Name,
                package.RelativeResourcePath, package.Sha256, package.FileSizeBytes))
                : GatewayResult.Fail<ManagedProjectPackageDto>(copied.Errors.ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return DeliveryFailure<ManagedProjectPackageDto>(); }
    }

    public async Task<GatewayResult<ProjectSnapshotDto>?> OpenProjectPackageAsync(nint owner, CancellationToken cancellationToken)
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add(".fluoaxisproj");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, owner);
            StorageFile? file = await picker.PickSingleFileAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return file is null ? null : await store.ImportProjectPackageAsync(file.Path, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return DeliveryFailure<ProjectSnapshotDto>(); }
    }

    public async Task<GatewayResult<ReportExportDto>?> ExportReportAsync(nint owner, Guid runId, string format, CancellationToken cancellationToken)
    {
        if (format is not ("html" or "xlsx" or "pdf")) return DeliveryFailure<ReportExportDto>();
        try
        {
            GatewayResult<ReportSnapshotDto> prepared = await store.CreateReportSnapshotAsync(runId, cancellationToken);
            if (!prepared.IsSuccess) return GatewayResult.Fail<ReportExportDto>(prepared.Errors.ToArray());
            ReportSnapshotDto snapshot = prepared.Value!;
            StorageFile? selected = await SelectDestinationAsync(owner, format, $"FluoAxis-{snapshot.ReportId}", cancellationToken);
            if (selected is null) return null;
            if (format == "pdf")
            {
                var renderer = new ResearchReportRenderer();
                byte[] bytes = await PdfReportRenderer.RenderAsync(renderer.RenderHtml(snapshot), cancellationToken);
                using StorageStreamTransaction transaction = await selected.OpenTransactedWriteAsync();
                using Stream output = transaction.Stream.AsStreamForWrite();
                output.SetLength(0);
                await output.WriteAsync(bytes, cancellationToken);
                await output.FlushAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await transaction.CommitAsync();
                return GatewayResult.Ok(new ReportExportDto(selected.Name, format,
                    Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.Length, snapshot.SnapshotSha256));
            }
            GatewayResult<ReportExportDto> exported = await store.CreateReportExportAsync(snapshot, format, cancellationToken);
            if (!exported.IsSuccess) return exported;
            GatewayResult<bool> copied = await WriteManagedTransactionAsync(selected,
                $"reports/{snapshot.SnapshotSha256}.{format}", exported.Value!.Sha256, cancellationToken);
            return copied.IsSuccess ? GatewayResult.Ok(new ReportExportDto(selected.Name, format,
                exported.Value.Sha256, exported.Value.FileSizeBytes, exported.Value.SnapshotSha256))
                : GatewayResult.Fail<ReportExportDto>(copied.Errors.ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return DeliveryFailure<ReportExportDto>(); }
    }

    private async Task<GatewayResult<bool>> WriteManagedTransactionAsync(StorageFile selected,
        string relativeEntry, string sha256, CancellationToken cancellationToken)
    {
        using StorageStreamTransaction transaction = await selected.OpenTransactedWriteAsync();
        using Stream output = transaction.Stream.AsStreamForWrite();
        output.SetLength(0);
        GatewayResult<bool> copied = await store.CopyManagedExportAsync(relativeEntry, sha256, output, cancellationToken);
        if (!copied.IsSuccess) return copied;
        await output.FlushAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync();
        return copied;
    }

    private static async Task<StorageFile?> SelectDestinationAsync(nint owner, string extension, string suggestedName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = suggestedName };
        picker.FileTypeChoices.Add("FluoAxis", new List<string> { $".{extension}" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, owner);
        StorageFile? selected = await picker.PickSaveFileAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return selected;
    }

    private static GatewayResult<T> DeliveryFailure<T>() => GatewayResult.Fail<T>(new AppErrorDto(
        "SYSTEM_DELIVERY_FAILED", SeverityNames.Blocking,
        "无法完成文件输出或打开。", "请检查文件访问权限、格式与磁盘空间；PDF 输出还需要可用的本地 WebView2 运行时。"));
}
