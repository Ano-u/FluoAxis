namespace FluoAxis.Infrastructure;

using System.Security.Cryptography;
using System.Text.Json;
using FluoAxis.Application;
using FluoAxis.Contracts;
using FluoAxis.Domain;
using FluoAxis.Persistence;
using FluoAxis.ProjectFormat;
using Microsoft.Data.Sqlite;

public sealed partial class PersistentDesktopStore
{
    /// <summary>Portable transfer is intentionally smaller than the general archive reader's limits.</summary>
    public const long MaximumPortablePackageBytes = 128L * 1024 * 1024;
    public const long MaximumPortableEntryBytes = 64L * 1024 * 1024;
    private static readonly RelativePath PortableDatabaseEntry = RelativePath.Parse("project.sqlite");
    private static readonly PackageLimits PortableLimits = new()
    {
        MaxTotalUncompressedBytes = MaximumPortablePackageBytes,
        MaxEntryUncompressedBytes = MaximumPortableEntryBytes,
        MaxEntryCount = 4,
        MaxManifestUncompressedBytes = 64 * 1024,
        MaxCompressionRatio = 200,
    };

    /// <summary>Creates a validated portable package containing only the selected project/Run.</summary>
    public Task<GatewayResult<ManagedProjectPackageDto>> CreateProjectPackageAsync(Guid runId, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            if (runId == Guid.Empty) return PackageFailure<ManagedProjectPackageDto>("PROJECT_RUN_NOT_FOUND");
            RelativePath scratch = RelativePath.Parse($"package-tmp/{Guid.NewGuid():N}");
            string scratchRoot = Resolve(scratch);
            Directory.CreateDirectory(scratchRoot);
            RelativePath databaseEntry = scratch.Combine("project.sqlite");
            try
            {
                PortableRunIdentity identity = await repository.ExportRunDatabaseAsync(runId,
                    () => PortableConnection(databaseEntry, readOnly: false), cancellationToken).ConfigureAwait(false);
                await ValidatePortableTextPathsAsync(databaseEntry, cancellationToken).ConfigureAwait(false);
                byte[] database = await ReadManagedBoundedAsync(databaseEntry, MaximumPortableEntryBytes, cancellationToken).ConfigureAwait(false);
                List<ProjectPackageContentFile> files =
                [new() { Path = PortableDatabaseEntry, EntryType = ProjectEntryType.Database, Content = database }];
                if (identity.Source is { } source)
                {
                    ValidatePortableSourceIdentity(source);
                    byte[] content = await ReadManagedBoundedAsync(source.ResourcePath, MaximumPortableEntryBytes, cancellationToken).ConfigureAwait(false);
                    if (content.LongLength != source.SizeBytes || !string.Equals(Hash(content), source.Sha256, StringComparison.Ordinal))
                        return PackageFailure<ManagedProjectPackageDto>("PROJECT_PACKAGE_SOURCE_MISMATCH");
                    files.Add(new ProjectPackageContentFile { Path = source.ResourcePath, EntryType = ProjectEntryType.SourceRaw, Content = content });
                }
                RelativePath explorerEntry = RelativePath.Parse($"explorer/{runId:N}/view.json");
                if (File.Exists(Resolve(explorerEntry)))
                {
                    byte[] view = await ReadManagedBoundedAsync(explorerEntry, MaximumExplorerFileBytes, cancellationToken).ConfigureAwait(false);
                    ValidatePortableExplorer(view, identity);
                    files.Add(new ProjectPackageContentFile { Path = explorerEntry, EntryType = ProjectEntryType.PlotSpec, Content = view });
                }

                RelativePath finalEntry = RelativePath.Parse($"packages/run-{runId:N}-{Guid.NewGuid():N}.fluoaxisproj");
                RelativePath temporaryEntry = RelativePath.Parse(finalEntry.Value + $".{Guid.NewGuid():N}.tmp");
                Directory.CreateDirectory(Resolve(finalEntry.Parent!));
                bool completed = false;
                try
                {
                    var contents = new ProjectPackageContents
                    {
                        ProjectId = identity.Run.ProjectId,
                        DisplayName = identity.Run.ProjectName,
                        CreatedAtUtc = identity.Run.CreatedAtUtc,
                        ModifiedAtUtc = identity.Run.UpdatedUtc,
                        MethodVersions = [$"{identity.Run.Method.MethodId}/{identity.Run.Method.Version}"],
                        MethodSnapshotHashes = [identity.Run.Method.ComputeCanonicalHash()],
                        Files = files,
                    };
                    await using (FileStream output = new(Resolve(temporaryEntry), FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        64 * 1024, FileOptions.Asynchronous))
                    {
                        Result<ProjectManifest> saved = await FluoAxisProjectArchive.SaveAsync(output, contents, cancellationToken).ConfigureAwait(false);
                        if (!saved.IsSuccess) return PackageFailure<ManagedProjectPackageDto>(saved.Errors[0].Code);
                        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                        output.Flush(flushToDisk: true);
                    }
                    await using (FileStream verify = new(Resolve(temporaryEntry), FileMode.Open, FileAccess.Read, FileShare.Read,
                        64 * 1024, FileOptions.Asynchronous))
                    {
                        if (verify.Length > MaximumPortablePackageBytes) return PackageFailure<ManagedProjectPackageDto>("PROJECT_PACKAGE_LIMIT_EXCEEDED");
                        Result<ProjectPackage> checkedPackage = await FluoAxisProjectArchive.OpenAsync(verify, PortableLimits, cancellationToken).ConfigureAwait(false);
                        if (!checkedPackage.IsSuccess) return PackageFailure<ManagedProjectPackageDto>(checkedPackage.Errors[0].Code);
                        ValidateManifestIdentity(checkedPackage.Value!, identity);
                    }
                    // Verify the closed database independently before publishing the package.
                    _ = await SqliteProjectRunRepository.InspectPackageDatabaseAsync(
                        () => PortableConnection(databaseEntry, readOnly: true), cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    Result<FileInfo> published = AtomicFile.Replace(Resolve(temporaryEntry), Resolve(finalEntry));
                    if (!published.IsSuccess) return PackageFailure<ManagedProjectPackageDto>(published.Errors[0].Code);
                    await using FileStream packageStream = new(Resolve(finalEntry), FileMode.Open, FileAccess.Read, FileShare.Read,
                        64 * 1024, FileOptions.Asynchronous);
                    string sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(packageStream, cancellationToken).ConfigureAwait(false));
                    completed = true;
                    return GatewayResult.Ok(new ManagedProjectPackageDto(finalEntry.FileName, finalEntry.Value, sha, packageStream.Length));
                }
                finally
                {
                    DeleteManagedFileIfPresent(temporaryEntry);
                    if (!completed) DeleteManagedFileIfPresent(finalEntry);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (KeyNotFoundException) { return PackageFailure<ManagedProjectPackageDto>("PROJECT_RUN_NOT_FOUND"); }
            catch (Exception) { return PackageFailure<ManagedProjectPackageDto>("PROJECT_PACKAGE_EXPORT_FAILED"); }
            finally { DeletePackageScratch(scratch); }
        }, cancellationToken);

    /// <summary>Consumes one transient picker path; validated content never overwrites an existing project or source.</summary>
    public Task<GatewayResult<ProjectSnapshotDto>> ImportProjectPackageAsync(string transientExternalPath, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            RelativePath scratch = RelativePath.Parse($"package-tmp/{Guid.NewGuid():N}");
            RelativePath databaseEntry = scratch.Combine("project.sqlite");
            RelativePath? createdSource = null;
            RelativePath? createdView = null;
            bool committed = false;
            try
            {
                if (string.IsNullOrWhiteSpace(transientExternalPath) || !Path.IsPathFullyQualified(transientExternalPath)
                    || !string.Equals(Path.GetExtension(transientExternalPath), ".fluoaxisproj", StringComparison.OrdinalIgnoreCase)
                    || File.GetAttributes(transientExternalPath).HasFlag(FileAttributes.ReparsePoint))
                    return PackageFailure<ProjectSnapshotDto>("PROJECT_NOT_A_PACKAGE");
                ProjectPackage package;
                await using (FileStream input = new(transientExternalPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    64 * 1024, FileOptions.Asynchronous))
                {
                    if (input.Length <= 0 || input.Length > MaximumPortablePackageBytes)
                        return PackageFailure<ProjectSnapshotDto>("PROJECT_PACKAGE_LIMIT_EXCEEDED");
                    Result<ProjectPackage> opened = await FluoAxisProjectArchive.OpenAsync(input, PortableLimits, cancellationToken).ConfigureAwait(false);
                    if (!opened.IsSuccess) return PackageFailure<ProjectSnapshotDto>(opened.Errors[0].Code);
                    package = opened.Value!;
                }
                IReadOnlyList<ManifestEntry> databases = package.EntriesOfType(ProjectEntryType.Database);
                if (package.Manifest.FormatVersion != ProjectManifest.CurrentFormatVersion
                    || databases.Count != 1
                    || databases[0].RelativePath != PortableDatabaseEntry
                    || package.DeclaredEntries.Any(entry => entry.EntryType is not (ProjectEntryType.Database or ProjectEntryType.SourceRaw or ProjectEntryType.PlotSpec)))
                    return PackageFailure<ProjectSnapshotDto>("PROJECT_PACKAGE_CONTENTS_INVALID");
                Directory.CreateDirectory(Resolve(scratch));
                await WriteManagedNewAsync(databaseEntry, package.ReadEntry(PortableDatabaseEntry), cancellationToken).ConfigureAwait(false);
                await SqliteProjectRunRepository.UpgradePortableDatabaseAsync(
                    () => PortableConnection(databaseEntry, readOnly: false), cancellationToken).ConfigureAwait(false);
                PortableRunIdentity identity = await SqliteProjectRunRepository.InspectPackageDatabaseAsync(
                    () => PortableConnection(databaseEntry, readOnly: true), cancellationToken).ConfigureAwait(false);
                await ValidatePortableTextPathsAsync(databaseEntry, cancellationToken).ConfigureAwait(false);
                ValidateManifestIdentity(package, identity);
                ManifestEntry? explorer = package.EntriesOfType(ProjectEntryType.PlotSpec).SingleOrDefault();
                if (explorer is not null) ValidatePortableExplorer(package.ReadEntry(explorer.RelativePath), identity);
                // Identity checks precede any managed source publication.
                if (await repository.GetByRunIdAsync(identity.Run.RunId, cancellationToken).ConfigureAwait(false) is not null)
                    return PackageFailure<ProjectSnapshotDto>("PROJECT_PACKAGE_IDENTITY_CONFLICT");
                if (identity.Source is { } source)
                {
                    ValidatePortableSourceIdentity(source);
                    if (File.Exists(Resolve(source.ResourcePath)))
                        return PackageFailure<ProjectSnapshotDto>("PROJECT_PACKAGE_SOURCE_CONFLICT");
                    Directory.CreateDirectory(Resolve(source.ResourcePath.Parent!));
                    await WriteManagedNewAsync(source.ResourcePath, package.ReadEntry(source.ResourcePath), cancellationToken).ConfigureAwait(false);
                    createdSource = source.ResourcePath;
                }
                if (explorer is not null)
                {
                    if (File.Exists(Resolve(explorer.RelativePath))) return PackageFailure<ProjectSnapshotDto>("PROJECT_PACKAGE_VIEW_CONFLICT");
                    Directory.CreateDirectory(Resolve(explorer.RelativePath.Parent!));
                    await WriteManagedNewAsync(explorer.RelativePath, package.ReadEntry(explorer.RelativePath), cancellationToken).ConfigureAwait(false);
                    createdView = explorer.RelativePath;
                }
                ProjectRunAggregate imported = await repository.ImportRunDatabaseAsync(
                    () => PortableConnection(databaseEntry, readOnly: true), cancellationToken).ConfigureAwait(false);
                committed = true;
                return GatewayResult.Ok(new ProjectSnapshotDto(imported.ProjectId, imported.ProjectName, imported.RunId,
                    imported.RunRevision, RunStateNames.FromDomain(imported.RunState), imported.CreatedAtUtc));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (InvalidOperationException error) when (error.Message == "PROJECT_PACKAGE_IDENTITY_CONFLICT")
            { return PackageFailure<ProjectSnapshotDto>("PROJECT_PACKAGE_IDENTITY_CONFLICT"); }
            catch (Exception) { return PackageFailure<ProjectSnapshotDto>("PROJECT_PACKAGE_IMPORT_FAILED"); }
            finally
            {
                if (!committed && createdSource is not null) DeleteManagedFileIfPresent(createdSource);
                if (!committed && createdView is not null) DeleteManagedFileIfPresent(createdView);
                DeletePackageScratch(scratch);
            }
        }, cancellationToken);

    private SqliteConnection PortableConnection(RelativePath entry, bool readOnly) => new(new SqliteConnectionStringBuilder
    {
        DataSource = Resolve(entry),
        Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
        Pooling = false,
        DefaultTimeout = 5,
    }.ToString());

    private static void ValidateManifestIdentity(ProjectPackage package, PortableRunIdentity identity)
    {
        ProjectManifest manifest = package.Manifest;
        if (manifest.ProjectId != identity.Run.ProjectId || manifest.DisplayName != identity.Run.ProjectName
            || manifest.CreatedAtUtc != identity.Run.CreatedAtUtc || manifest.ModifiedAtUtc != identity.Run.UpdatedUtc
            || !manifest.MethodVersions.SequenceEqual([$"{identity.Run.Method.MethodId}/{identity.Run.Method.Version}"], StringComparer.Ordinal)
            || !manifest.MethodSnapshotHashes.SequenceEqual([identity.Run.Method.ComputeCanonicalHash()], StringComparer.Ordinal))
            throw new InvalidDataException("The package manifest and database identity differ.");
        IReadOnlyList<ManifestEntry> sources = package.EntriesOfType(ProjectEntryType.SourceRaw);
        IReadOnlyList<ManifestEntry> views = package.EntriesOfType(ProjectEntryType.PlotSpec);
        if (views.Count > 1 || views.Count == 1 && views[0].RelativePath.Value != $"explorer/{identity.Run.RunId:N}/view.json")
            throw new InvalidDataException("The package Explorer identity is invalid.");
        if (identity.Source is not { } source)
        {
            if (sources.Count != 0 || package.DeclaredEntries.Count != 1 + views.Count) throw new InvalidDataException("The package has unreferenced sources.");
            return;
        }
        if (sources.Count != 1 || package.DeclaredEntries.Count != 2 + views.Count || sources[0].RelativePath != source.ResourcePath
            || sources[0].Sha256.Value != source.Sha256 || sources[0].SizeBytes != source.SizeBytes)
            throw new InvalidDataException("The package source metadata differs from the database.");
    }

    private static void ValidatePortableExplorer(byte[] bytes, PortableRunIdentity identity)
    {
        if (bytes.Length > MaximumExplorerFileBytes) throw new InvalidDataException("The package Explorer view exceeds its budget.");
        SavedExplorerViewDto view = JsonSerializer.Deserialize(bytes, ExplorerJsonContext.Default.SavedExplorerViewDto)
            ?? throw new InvalidDataException("The package Explorer view is absent.");
        string hash = Hash(JsonSerializer.SerializeToUtf8Bytes(view.Definition, ExplorerJsonContext.Default.ExplorerViewDefinitionDto));
        if (view.Definition.RunId != identity.Run.RunId || view.Sha256 != hash
            || view.Definition.Mode == "raw" && view.Definition.SourceSha256 != identity.Source?.Sha256)
            throw new InvalidDataException("The package Explorer view identity is inconsistent.");
    }

    private async Task ValidatePortableTextPathsAsync(RelativePath databaseEntry, CancellationToken token)
    {
        // Only the fixed current-schema tables can reach this scan; import
        // validates that schema before this method, while export creates it.
        // Check all stored text, including user notes, rather than relying on
        // filesystem-reference column names alone.
        await using SqliteConnection connection = PortableConnection(databaseEntry, readOnly: true);
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using SqliteCommand tables = connection.CreateCommand();
        tables.CommandText = "SELECT name FROM sqlite_schema WHERE type='table' AND name NOT GLOB 'sqlite_*';";
        List<string> names = [];
        await using (SqliteDataReader reader = await tables.ExecuteReaderAsync(token).ConfigureAwait(false))
            while (await reader.ReadAsync(token).ConfigureAwait(false)) names.Add(reader.GetString(0));
        foreach (string name in names)
        {
            if (name.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
                throw new InvalidDataException("The portable table identifier is invalid.");
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {name};";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                for (int index = 0; index < reader.FieldCount; index++)
                    if (reader.GetValue(index) is string text && SafeDiagnostics.RedactMachinePaths(text) != text)
                        throw new InvalidDataException("A portable database text value contains a machine path.");
        }
    }

    private static void ValidatePortableSourceIdentity(PreparedSourceIdentity source)
    {
        if (source.ResourcePath.Parent?.Value != "data/raw") throw new InvalidDataException("The package source is outside the managed raw directory.");
        _ = new SourceFileDto(source.DisplayName, source.ResourcePath.Value, source.Sha256, source.SizeBytes, DateTimeOffset.UnixEpoch);
    }

    private async Task<byte[]> ReadManagedBoundedAsync(RelativePath entry, long limit, CancellationToken token)
    {
        await using FileStream stream = new(Resolve(entry), FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
        if (stream.Length <= 0 || stream.Length > limit) throw new InvalidDataException("A portable entry exceeds its size limit.");
        byte[] bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (stream.ReadByte() != -1) throw new InvalidDataException("A portable entry changed while being read.");
        return bytes;
    }

    private async Task WriteManagedNewAsync(RelativePath entry, byte[] bytes, CancellationToken token)
    {
        bool ownsFile = false;
        try
        {
            await using FileStream stream = new(Resolve(entry), FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous);
            ownsFile = true;
            await stream.WriteAsync(bytes, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }
        catch
        {
            if (ownsFile) DeleteManagedFileIfPresent(entry);
            throw;
        }
    }

    private void DeleteManagedFileIfPresent(RelativePath entry)
    {
        try { File.Delete(Resolve(entry)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { }
    }

    private void DeletePackageScratch(RelativePath scratch)
    {
        // Only files created by this transfer are removed. No imported entry
        // supplies cleanup targets, and no recursive delete follows a redirect.
        foreach (string suffix in new[] { "project.sqlite", "project.sqlite-journal", "project.sqlite-wal", "project.sqlite-shm" })
            DeleteManagedFileIfPresent(scratch.Combine(suffix));
        try { if (Directory.Exists(Resolve(scratch))) Directory.Delete(Resolve(scratch), recursive: false); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static GatewayResult<T> PackageFailure<T>(string code) => GatewayResult.Fail<T>(new AppErrorDto(
        code, SeverityNames.Blocking, "工程包不能安全保存或打开。", "请保留原工程，核对文件完整性、版本和是否已经存在同一项目；现有项目不会被覆盖。"));
}
