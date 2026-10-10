namespace FluoAxis.Infrastructure;

using FluoAxis.Application;
using FluoAxis.Contracts;
using FluoAxis.Domain;
using FluoAxis.Persistence;
using Microsoft.Data.Sqlite;

/// <summary>
/// Owns a persistent local workspace. Only this Infrastructure boundary receives
/// the injected platform root; public results contain relative source identities.
/// One exclusive lease and one operation slot cover initialization, database work,
/// staging and shutdown. SQLite calls are synchronous internally, so the serialized
/// workflow executes away from the dispatcher with at most one worker per store.
/// </summary>
public sealed partial class PersistentDesktopStore : IFluoAxisGateway, IRunEvidenceGateway, IRawSignalGateway, IScientificEvidenceGateway, IAsyncDisposable
{
    private readonly string root;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly Lock closeGate = new();
    private readonly SqliteProjectRunRepository repository;
    private readonly ApplicationGateway gateway;
    private readonly ManagedImportStagingAdapter staging;
    private FileStream? lease;
    private Task? closeTask;
    private int closing;

    public Task Ready { get; }
    public IFluoAxisGateway Gateway => this;

    public PersistentDesktopStore(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        if (!Path.IsPathFullyQualified(workspaceRoot))
            throw new ArgumentException("The injected workspace root must be fully qualified.", nameof(workspaceRoot));
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        if (string.Equals(root, Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A filesystem volume root cannot be used as a workspace.", nameof(workspaceRoot));
        repository = new SqliteProjectRunRepository(CreateConnection);
        staging = new ManagedImportStagingAdapter(root);
        gateway = new ApplicationGateway(repository,
            importInspection: new ManagedImportInspectionAdapter(root),
            signalPreparation: new ManagedSignalPreparationAdapter(root));
        Ready = Task.Run(InitializeAsync);
    }

    private async Task InitializeAsync()
    {
        try
        {
            // Check the root before creating children or following a redirect.
            RejectReparseAncestors(root);
            Directory.CreateDirectory(root);
            lease = new FileStream(Resolve(RelativePath.Parse("workspace.lock")),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var wal = new SqliteWalWorkspace(CreateConnection);
            await wal.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
            await OpenSessionMarkerAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            RecordDiagnostic("WORKSPACE_OPEN_FAILED", SafeDiagnostics.ClassifyException(exception));
            lease?.Dispose();
            lease = null;
            // Do not retain the original path-bearing exception as InnerException.
            throw new InvalidOperationException("SYSTEM_WORKSPACE_OPEN_FAILED");
        }
    }

    private SqliteConnection CreateConnection()
    {
        // Resolve afresh per call; never cache or serialize a connection string.
        string database = Resolve(RelativePath.Parse("workspace.db"));
        _ = Resolve(RelativePath.Parse("workspace.db-wal"));
        _ = Resolve(RelativePath.Parse("workspace.db-shm"));
        return new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database, Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false, DefaultTimeout = 5,
        }.ToString());
    }

    private string Resolve(RelativePath entry)
    {
        RejectReparseAncestors(root);
        string current = root;
        foreach (string segment in entry.Value.Split('/'))
        {
            current = Path.Combine(current, segment);
            if (Path.Exists(current) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException("SYSTEM_WORKSPACE_UNSAFE");
        }
        return current;
    }

    private static void RejectReparseAncestors(string path)
    {
        for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
        {
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException("SYSTEM_WORKSPACE_UNSAFE");
        }
    }

    public Task<GatewayResult<IReadOnlyList<ProjectSnapshotDto>>> ListProjectsAsync(
        CancellationToken cancellationToken, int offset = 0, int pageSize = 50) => ExecuteAsync(async () =>
    {
        if (offset < 0 || pageSize is < 1 or > 100)
            return Failure<IReadOnlyList<ProjectSnapshotDto>>("SYSTEM_CATALOG_PAGE_INVALID", "项目列表分页参数无效。");
        var entries = await repository.ListProjectsAsync(offset, pageSize, cancellationToken).ConfigureAwait(false);
        return GatewayResult.Ok<IReadOnlyList<ProjectSnapshotDto>>(Array.AsReadOnly(entries.Select(entry =>
            new ProjectSnapshotDto(entry.ProjectId, entry.ProjectName, entry.RunId, entry.RunRevision,
                RunStateNames.FromDomain(entry.RunState), entry.CreatedAtUtc)).ToArray()));
    }, cancellationToken);

    /// <summary>Consumes the picker path entirely within Infrastructure.</summary>
    public Task<GatewayResult<SourceFileDto>> StageAsync(string transientExternalPath, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            Result<StagedImportResource> staged = await staging.StageAsync(transientExternalPath, cancellationToken).ConfigureAwait(false);
            return staged.ToGatewayResult(source => new SourceFileDto(source.DisplayName, source.ResourcePath.Value,
                source.Sha256.Value, source.SizeBytes, DateTimeOffset.UtcNow));
        }, cancellationToken);

    public Task<GatewayResult<bool>> SavePlotAsync(byte[] bytes, string extension, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(bytes);
            if (extension is not ("png" or "svg") || bytes.Length > 32 * 1024 * 1024)
                return Failure<bool>("SYSTEM_PLOT_EXPORT_INVALID", "图形格式或大小无效。");
            string folder = Resolve(RelativePath.Parse("exports"));
            Directory.CreateDirectory(folder);
            string destination = Resolve(RelativePath.Parse($"exports/plot-{Guid.NewGuid():N}.{extension}"));
            bool published = false;
            try
            {
                await using (FileStream stream = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    64 * 1024, FileOptions.Asynchronous))
                {
                    await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                published = true;
                return GatewayResult.Ok(true);
            }
            finally
            {
                if (!published)
                {
                    try { File.Delete(destination); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }, cancellationToken);

    public Task<GatewayResult<ProjectSnapshotDto>> CreateProjectAsync(CreateProjectCommand command, CancellationToken cancellationToken) =>
        ExecuteAsync(() => gateway.CreateProjectAsync(command, cancellationToken), cancellationToken);

    public Task<GatewayResult<ImportPreviewDto>> InspectImportAsync(InspectImportCommand command,
        IProgress<OperationProgressDto>? progress, CancellationToken cancellationToken) =>
        ExecuteAsync(() => gateway.InspectImportAsync(command, progress, cancellationToken), cancellationToken);

    public Task<GatewayResult<RunSnapshotDto>> ConfirmMappingAsync(ConfirmMappingCommand command, CancellationToken cancellationToken) =>
        ExecuteAsync(() => gateway.ConfirmMappingAsync(command, cancellationToken), cancellationToken);

    public Task<GatewayResult<RunSnapshotDto>> AssignWellsAsync(AssignWellsCommand command, CancellationToken cancellationToken) =>
        ExecuteAsync(() => gateway.AssignWellsAsync(command, cancellationToken), cancellationToken);

    public Task<GatewayResult<CalibrationResultDto>> RunCalibrationAsync(RunCalibrationCommand command, CancellationToken cancellationToken) =>
        ExecuteAsync(() => gateway.RunCalibrationAsync(command, cancellationToken), cancellationToken);

    public Task<GatewayResult<ControlQcResultDto>> RunControlQcAsync(RunControlQcCommand command, CancellationToken cancellationToken) =>
        ExecuteAsync(() => gateway.RunControlQcAsync(command, cancellationToken), cancellationToken);

    public Task<GatewayResult<IReadOnlyList<SampleAnalysisDto>>> AnalyzeSamplesAsync(AnalyzeSamplesCommand command, CancellationToken cancellationToken) =>
        ExecuteAsync(() => gateway.AnalyzeSamplesAsync(command, cancellationToken), cancellationToken);

    public Task<GatewayResult<RunSnapshotDto>> GetRunAsync(GetRunQuery query, CancellationToken cancellationToken) =>
        ExecuteAsync(() => gateway.GetRunAsync(query, cancellationToken), cancellationToken);

    public Task<GatewayResult<RunEvidenceDto>> GetRunEvidenceAsync(GetRunQuery query, CancellationToken cancellationToken) =>
        ExecuteAsync(() => gateway.GetRunEvidenceAsync(query, cancellationToken), cancellationToken);

    private async Task<GatewayResult<T>> ExecuteAsync<T>(Func<Task<GatewayResult<T>>> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref closing) != 0) return Failure<T>("SYSTEM_WORKSPACE_CLOSED", "工作区正在关闭。");
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref closing) != 0) return Failure<T>("SYSTEM_WORKSPACE_CLOSED", "工作区正在关闭。");
            await Ready.WaitAsync(cancellationToken).ConfigureAwait(false);
            return await Task.Run(operation, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            RecordDiagnostic("WORKSPACE_OPERATION_FAILED", SafeDiagnostics.ClassifyException(exception));
            return Failure<T>("SYSTEM_WORKSPACE_OPERATION_FAILED", "无法读取或保存本地工作区，请检查工作区访问权限与数据完整性，并关闭其他 FluoAxis 实例后重试。");
        }
        finally { operationGate.Release(); }
    }

    private static GatewayResult<T> Failure<T>(string code, string message) => GatewayResult.Fail<T>(
        new AppErrorDto(code, SeverityNames.Blocking, message, "已有已提交记录不会被替换为新项目，请重试或重新打开应用。"));

    public Task CloseAsync()
    {
        lock (closeGate)
        {
            Interlocked.Exchange(ref closing, 1);
            return closeTask ??= CloseCoreAsync();
        }
    }

    private async Task CloseCoreAsync()
    {
        await operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            try { await Ready.ConfigureAwait(false); }
            catch (InvalidOperationException) { /* Initialization already has a path-free failure. */ }
            // WAL transactions survive process restart. Packaging checkpoint is
            // deliberately not a normal shutdown precondition.
            await repository.DisposeAsync().ConfigureAwait(false);
            if (sessionMarkerOwned)
            {
                try { File.Delete(Resolve(RelativePath.Parse("session.active"))); }
                catch (Exception exception) { RecordDiagnostic("SESSION_MARKER_CLEAR_FAILED", SafeDiagnostics.ClassifyException(exception)); }
                sessionMarkerOwned = false;
            }
            lease?.Dispose();
            lease = null;
        }
        finally
        {
            // Waiters must wake and observe closed; disposing the semaphore while
            // those waiters exist would introduce an ObjectDisposedException race.
            operationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
