namespace FluoAxis.ProjectFormat;

using FluoAxis.Domain;

/// <summary>
/// Atomic file replacement helper implementing the CLAUDE.md §21 save
/// flow: write a unique sibling temporary file, flush it, then swap it
/// into place. Absolute paths appear only inside this method as transient
/// runtime values at the OS I/O boundary — nothing is persisted (P0
/// invariant, CLAUDE.md §4.1).
/// </summary>
public static class AtomicFile
{
    private const int CopyBufferSize = 81920;

    /// <summary>
    /// Atomically replaces <paramref name="targetPath"/> with the contents
    /// of the already-written, already-flushed temporary file. If the target
    /// does not exist, the file is moved into place; if it exists, it is
    /// replaced. On any failure the original target is left untouched and
    /// the temporary file is deleted.
    /// </summary>
    public static Result<FileInfo> Replace(string tempPath, string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tempPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        if (!File.Exists(tempPath))
        {
            return Result<FileInfo>.Fail(new AppError(
                ProjectErrorCodes.ReplaceFailed,
                ErrorSeverity.Blocking,
                "The prepared temporary package file does not exist.",
                "temp"));
        }

        try
        {
            string? targetDirectory = Path.GetDirectoryName(Path.GetFullPath(targetPath));
            if (!string.IsNullOrEmpty(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            if (File.Exists(targetPath))
            {
                File.Replace(tempPath, targetPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, targetPath);
            }

            return Result<FileInfo>.Ok(new FileInfo(targetPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            TryDelete(tempPath);
            return Result<FileInfo>.Fail(new AppError(
                ProjectErrorCodes.ReplaceFailed,
                ErrorSeverity.Blocking,
                "The package could not be replaced atomically; the original file is untouched.",
                "replace"));
        }
    }

    /// <summary>
    /// Writes <paramref name="contents"/> to a unique temporary sibling of
    /// <paramref name="targetPath"/>, flushes to disk, and returns the temp
    /// file path for a subsequent <see cref="Replace"/>. The temp file is
    /// deleted on failure. The name ends in ".tmp" so a crash never leaves a
    /// file that looks like a valid package.
    /// </summary>
    public static async Task<Result<string>> WriteTempAsync(
        string targetPath,
        byte[] contents,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(contents);

        string? targetDirectory = Path.GetDirectoryName(Path.GetFullPath(targetPath));
        if (!string.IsNullOrEmpty(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        string tempPath = Path.Combine(
            targetDirectory ?? ".",
            Path.GetFileName(targetPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            await using FileStream stream = new(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                CopyBufferSize,
                FileOptions.SequentialScan | FileOptions.Asynchronous);
            await stream.WriteAsync(contents, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            return Result<string>.Ok(tempPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            TryDelete(tempPath);
            throw;
        }
    }

    internal static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
