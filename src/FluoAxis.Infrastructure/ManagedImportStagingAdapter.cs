namespace FluoAxis.Infrastructure;

using System.Buffers;
using System.Security.Cryptography;
using FluoAxis.Domain;
using FluoAxis.Import;

/// <summary>
/// Path-free metadata for a source copied into managed storage. This is a
/// staging fact, not proof that the source has been committed to a Run.
/// </summary>
public sealed record StagedImportResource
{
    public string DisplayName { get; }
    public RelativePath ResourcePath { get; }
    public HexHash Sha256 { get; }
    public long SizeBytes { get; }
    public string MediaType { get; }

    public StagedImportResource(
        string displayName,
        RelativePath resourcePath,
        HexHash sha256,
        long sizeBytes,
        string mediaType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(resourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        if (displayName.IndexOfAny(['/', '\\']) >= 0)
        {
            throw new ArgumentException("DisplayName must be a bare file name.", nameof(displayName));
        }

        if (sizeBytes <= 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeBytes);
        }

        DisplayName = displayName;
        ResourcePath = resourcePath;
        Sha256 = sha256;
        SizeBytes = sizeBytes;
        MediaType = mediaType;
    }
}

/// <summary>
/// Copies one transient operating-system picker result into managed storage.
/// The external path is consumed only by this Infrastructure call and is
/// never retained in the result or normalized errors.
/// </summary>
public sealed class ManagedImportStagingAdapter
{
    private const int BufferSize = 64 * 1024;
    private readonly string managedRoot;
    private readonly RelativePath destinationDirectory;
    private readonly long maxBytes;
    private readonly Func<Guid> createId;

    public ManagedImportStagingAdapter(
        string managedRoot,
        RelativePath? destinationDirectory = null,
        long? maxBytes = null,
        Func<Guid>? createId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        if (!Path.IsPathFullyQualified(managedRoot))
        {
            throw new ArgumentException("The managed root must be fully qualified at the Infrastructure boundary.", nameof(managedRoot));
        }

        long limit = maxBytes ?? ImportLimits.Default.MaxBytes;
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        string resolvedRoot = Path.GetFullPath(managedRoot);
        string volumeRoot = Path.GetPathRoot(resolvedRoot)
            ?? throw new ArgumentException("The managed root has no filesystem root.", nameof(managedRoot));
        this.managedRoot = resolvedRoot.Length == volumeRoot.Length
            ? resolvedRoot
            : resolvedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        this.destinationDirectory = destinationDirectory ?? RelativePath.Parse("data/raw");
        this.maxBytes = limit;
        this.createId = createId ?? Guid.NewGuid;
    }

    public async Task<Result<StagedImportResource>> StageAsync(
        string transientExternalPath,
        CancellationToken cancellationToken) =>
        await StageAsync(transientExternalPath, progress: null, cancellationToken).ConfigureAwait(false);

    public async Task<Result<StagedImportResource>> StageAsync(
        string transientExternalPath,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transientExternalPath);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(transientExternalPath))
        {
            return Result<StagedImportResource>.Fail(SourceUnavailable());
        }

        string displayName = Path.GetFileName(transientExternalPath);
        string extension = Path.GetExtension(displayName).ToLowerInvariant();
        string? mediaType = MediaTypeFor(extension);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Result<StagedImportResource>.Fail(SourceUnavailable());
        }

        if (mediaType is null)
        {
            return Result<StagedImportResource>.Fail(UnsupportedFormat());
        }

        string? directory = PrepareDestinationDirectory();
        if (directory is null)
        {
            return Result<StagedImportResource>.Fail(UnsafeManagedResource());
        }

        string id = createId().ToString("N");
        RelativePath finalRelative = destinationDirectory.Combine($"source-{id}{extension}");
        RelativePath tempRelative = destinationDirectory.Combine($".staging-{id}.tmp");
        string? finalPath = ResolveManaged(finalRelative);
        string? tempPath = ResolveManaged(tempRelative);
        if (finalPath is null || tempPath is null || HasReparsePoint(destinationDirectory))
        {
            return Result<StagedImportResource>.Fail(UnsafeManagedResource());
        }

        bool published = false;
        bool ownsTemporaryFile = false;
        StagingIoPhase ioPhase = StagingIoPhase.Source;
        try
        {
            FileInfo sourceInfo = new(transientExternalPath);
            if (!sourceInfo.Exists || sourceInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return Result<StagedImportResource>.Fail(SourceUnavailable());
            }

            long expectedLength = sourceInfo.Length;
            if (expectedLength <= 0)
            {
                return Result<StagedImportResource>.Fail(SourceUnavailable());
            }

            if (expectedLength > maxBytes)
            {
                return Result<StagedImportResource>.Fail(FileTooLarge());
            }

            await using FileStream source = new(
                transientExternalPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (source.Length != expectedLength)
            {
                return Result<StagedImportResource>.Fail(SourceChanged());
            }

            ioPhase = StagingIoPhase.DestinationCreate;
            await using (FileStream destination = new(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                ownsTemporaryFile = true;
                using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
                long copied = 0;
                try
                {
                    while (true)
                    {
                        ioPhase = StagingIoPhase.Source;
                        int read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }

                        copied = checked(copied + read);
                        if (copied > maxBytes || copied > expectedLength)
                        {
                            return Result<StagedImportResource>.Fail(
                                copied > maxBytes ? FileTooLarge() : SourceChanged());
                        }

                        hash.AppendData(buffer, 0, read);
                        ioPhase = StagingIoPhase.DestinationWrite;
                        await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        progress?.Report(copied);
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    if (copied != expectedLength || source.Length != expectedLength)
                    {
                        return Result<StagedImportResource>.Fail(SourceChanged());
                    }

                    ioPhase = StagingIoPhase.DestinationWrite;
                    await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                    destination.Flush(flushToDisk: true);
                    HexHash digest = HexHash.FromBytes(hash.GetHashAndReset());

                    if (HasReparsePoint(destinationDirectory))
                    {
                        return Result<StagedImportResource>.Fail(UnsafeManagedResource());
                    }

                    destination.Close();
                    ioPhase = StagingIoPhase.Publish;
                    File.Move(tempPath, finalPath, overwrite: false);
                    published = true;
                    return Result<StagedImportResource>.Ok(new StagedImportResource(
                        displayName, finalRelative, digest, copied, mediaType));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            if (ioPhase == StagingIoPhase.Source)
            {
                return Result<StagedImportResource>.Fail(SourceUnavailable());
            }

            if ((ioPhase == StagingIoPhase.DestinationCreate && File.Exists(tempPath))
                || (ioPhase == StagingIoPhase.Publish && File.Exists(finalPath)))
            {
                return Result<StagedImportResource>.Fail(StagingCollision());
            }

            return Result<StagedImportResource>.Fail(UnsafeManagedResource());
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Result<StagedImportResource>.Fail(
                ioPhase == StagingIoPhase.Source ? SourceUnavailable() : UnsafeManagedResource());
        }
        finally
        {
            if (!published && ownsTemporaryFile)
            {
                DeletePartial(tempPath);
            }
        }
    }

    private string? PrepareDestinationDirectory()
    {
        try
        {
            Directory.CreateDirectory(managedRoot);
            if (new DirectoryInfo(managedRoot).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return null;
            }

            string current = managedRoot;
            foreach (string segment in destinationDirectory.Value.Split('/'))
            {
                current = Path.Combine(current, segment);
                if (Directory.Exists(current) && new DirectoryInfo(current).Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    return null;
                }

                Directory.CreateDirectory(current);
                if (new DirectoryInfo(current).Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    return null;
                }
            }

            return current;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string? ResolveManaged(RelativePath relativePath)
    {
        string resolved = Path.GetFullPath(Path.Combine(
            managedRoot, relativePath.Value.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = managedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? managedRoot
            : managedRoot + Path.DirectorySeparatorChar;
        return resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? resolved : null;
    }

    private bool HasReparsePoint(RelativePath relativePath)
    {
        string current = managedRoot;
        if (Directory.Exists(current) && new DirectoryInfo(current).Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return true;
        }

        foreach (string segment in relativePath.Value.Split('/'))
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current) && new DirectoryInfo(current).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }
        }

        return false;
    }

    private void DeletePartial(string path)
    {
        string? resolved = ResolveManaged(RelativePath.Parse(Path.GetRelativePath(managedRoot, path)));
        if (resolved is null || !string.Equals(resolved, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            File.Delete(resolved);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cleanup is best effort; no runtime path may escape through an exception.
        }
    }

    private AppError FileTooLarge() => new(
        ImportErrorCodes.ImportFileTooLarge, ErrorSeverity.Blocking,
        $"The selected file exceeds the import size limit of {maxBytes} bytes.",
        $"maxBytes={maxBytes}");

    private static string? MediaTypeFor(string extension) => extension switch
    {
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".csv" => "text/csv",
        ".tsv" => "text/tab-separated-values",
        ".txt" => "text/plain",
        _ => null,
    };

    private static AppError SourceUnavailable() => new(
        ErrorCodes.ImportSourceUnavailable, ErrorSeverity.Blocking,
        "The selected import source could not be opened safely.", "sourceUnavailable");

    private static AppError SourceChanged() => new(
        ErrorCodes.ImportSourceChanged, ErrorSeverity.Blocking,
        "The selected import source changed while it was being copied.", "sourceChangedDuringCopy");

    private static AppError UnsupportedFormat() => new(
        ImportErrorCodes.ImportFormatMismatch, ErrorSeverity.Blocking,
        "The selected file name does not identify a supported import format.", "unsupportedSourceExtension");

    private static AppError StagingCollision() => new(
        ErrorCodes.ImportStagingCollision, ErrorSeverity.Blocking,
        "The managed import resource could not be published without overwriting an existing resource.",
        "managedResourceCollision");

    private static AppError UnsafeManagedResource() => new(
        ErrorCodes.ImportManagedResourceUnsafe, ErrorSeverity.Blocking,
        "The managed import destination could not be prepared safely.", "unsafeManagedDestination");

    private enum StagingIoPhase
    {
        Source,
        DestinationCreate,
        DestinationWrite,
        Publish,
    }
}
