namespace FluoAxis.Infrastructure;

using FluoAxis.Contracts;
using Windows.Storage.Pickers;

/// <summary>
/// Windows-only boundary compiled into the desktop composition root. Picker
/// handles and platform storage locations never cross into presentation DTOs.
/// The persistent workspace and project catalog survive process restart.
/// </summary>
public sealed partial class DesktopWorkspace : IAsyncDisposable
{
    private readonly PersistentDesktopStore store;

    public IFluoAxisGateway Gateway => store.Gateway;
    public Task Ready => store.Ready;

    public DesktopWorkspace()
    {
        // Platform-owned root is resolved only inside this Infrastructure adapter.
        try
        {
            string managedRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FluoAxis", "desktop-v1");
            store = new PersistentDesktopStore(managedRoot);
        }
        catch (Exception)
        {
            throw new InvalidOperationException("SYSTEM_WORKSPACE_OPEN_FAILED");
        }
    }

    public Task<GatewayResult<IReadOnlyList<ProjectSnapshotDto>>> ListProjectsAsync(
        CancellationToken cancellationToken, int offset = 0, int pageSize = 50) =>
        store.ListProjectsAsync(cancellationToken, offset, pageSize);

    public Task CloseAsync() => store.CloseAsync();

    public Task<GatewayResult<RawSignalPageDto>> GetRawSignalPageAsync(Guid runId, string well, string channel,
        int offset, int pageSize, CancellationToken cancellationToken) =>
        store.GetRawSignalPageAsync(runId, well, channel, offset, pageSize, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await store.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    public async Task<GatewayResult<SourceFileDto>?> PickAndStageAsync(nint owner, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileOpenPicker picker = new()
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                ViewMode = PickerViewMode.List,
            };
            foreach (string extension in new[] { ".xlsx", ".xls", ".csv", ".tsv", ".txt" })
            {
                picker.FileTypeFilter.Add(extension);
            }

            WinRT.Interop.InitializeWithWindow.Initialize(picker, owner);
            Windows.Storage.StorageFile? selected = await picker.PickSingleFileAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (selected is null) return null;

            return await store.StageAsync(selected.Path, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return GatewayResult.Fail<SourceFileDto>(new AppErrorDto(
                "SYSTEM_IMPORT_SELECTION_FAILED", SeverityNames.Blocking,
                "无法安全读取所选文件。", "请检查文件访问权限，然后重新选择。"));
        }
    }

    public async Task<bool> SavePlotAsync(byte[] bytes, string extension, CancellationToken cancellationToken)
    {
        GatewayResult<bool> saved = await store.SavePlotAsync(bytes, extension, cancellationToken).ConfigureAwait(false);
        return saved.IsSuccess && saved.Value;
    }

    public async Task<GatewayResult<SourceFileDto>> StageDroppedFileAsync(
        Windows.ApplicationModel.DataTransfer.DataPackageView dropped, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!dropped.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)) throw new InvalidDataException();
            IReadOnlyList<Windows.Storage.IStorageItem> items = await dropped.GetStorageItemsAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (items.Count != 1 || items[0] is not Windows.Storage.StorageFile file) throw new InvalidDataException();
            return await store.StageAsync(file.Path, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return GatewayResult.Fail<SourceFileDto>(new AppErrorDto("IMPORT_DROP_FAILED", SeverityNames.Blocking,
                "拖放内容必须是一个受支持的仪器文件。", "请选择单个 XLSX、XLS、CSV、TSV 或 TXT 文件。"));
        }
    }
}
