namespace FluoAxis.Infrastructure;

using System.Text;
using FluoAxis.Application;
using FluoAxis.Contracts;
using FluoAxis.Domain;
using FluoAxis.Import;

/// <summary>
/// Reads one staged managed resource into validated per-well, per-channel
/// series. The external picker location never reaches this type: it receives
/// only a managed package-relative resource and resolves it beneath the
/// injected managed root for the duration of one call.
/// <para>
/// This adapter deliberately knows nothing about the Analysis engine. It
/// returns Domain <see cref="SignalSeries"/> and lets the Application layer
/// build the engine's per-well shape once the plate roles are assigned,
/// which keeps the Infrastructure-to-Analysis reference out of the graph.
/// </para>
/// </summary>
public sealed class ManagedSignalPreparationAdapter : ISignalPreparationPort
{
    private readonly string managedRoot;
    private readonly ImportLimits limits;
    private readonly IImporter xlsxImporter;
    private readonly IImporter delimitedImporter;

    public ManagedSignalPreparationAdapter(
        string managedRoot,
        ImportLimits? limits = null,
        IImporter? xlsxImporter = null,
        IImporter? delimitedImporter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        if (!Path.IsPathFullyQualified(managedRoot))
        {
            throw new ArgumentException("The managed root must be fully qualified at the Infrastructure boundary.", nameof(managedRoot));
        }

        string resolvedRoot = Path.GetFullPath(managedRoot);
        string volumeRoot = Path.GetPathRoot(resolvedRoot)
            ?? throw new ArgumentException("The managed root has no filesystem root.", nameof(managedRoot));
        this.managedRoot = resolvedRoot.Length == volumeRoot.Length
            ? resolvedRoot
            : resolvedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        this.limits = limits ?? ImportLimits.Default;
        this.xlsxImporter = xlsxImporter ?? new XlsxImporter();
        this.delimitedImporter = delimitedImporter ?? new DelimitedTextImporter();
    }

    public async Task<Result<PreparedRunSignals>> PrepareAsync(
        PreparedSignalRequest request,
        IProgress<OperationProgressDto>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report(new OperationProgressDto(ImportStageNames.DetectingFormat, 0));

        string? resource = ResolveSafeResource(request.Source.ResourcePath);
        if (resource is null || HasReparsePoint(request.Source.ResourcePath))
        {
            return Result<PreparedRunSignals>.Fail(UnsafeResource());
        }

        FileInfo file;
        try
        {
            file = new FileInfo(resource);
            if (!file.Exists)
            {
                return Result<PreparedRunSignals>.Fail(ResourceNotFound());
            }

            if (file.Length != request.Source.SizeBytes)
            {
                return Result<PreparedRunSignals>.Fail(SizeMismatch(request.Source.SizeBytes, file.Length));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<PreparedRunSignals>.Fail(UnsafeResource());
        }

        try
        {
            await using var stream = new FileStream(
                resource, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length != request.Source.SizeBytes)
            {
                return Result<PreparedRunSignals>.Fail(SizeMismatch(request.Source.SizeBytes, stream.Length));
            }

            bool hasZipMagic = await HasZipMagicAsync(stream, cancellationToken).ConfigureAwait(false);
            bool looksLikeText = !hasZipMagic && await LooksLikeTextAsync(stream, cancellationToken).ConfigureAwait(false);
            IImporter? importer = SelectImporter(request.Source, hasZipMagic, looksLikeText);
            if (importer is null)
            {
                return Result<PreparedRunSignals>.Fail(FormatMismatch());
            }

            progress?.Report(new OperationProgressDto(ImportStageNames.ReadingSheet, 20));
            Result<ImportedDataset> read = await importer
                .ReadAsync(stream, ToDataMapping(request.Mapping), limits, cancellationToken)
                .ConfigureAwait(false);
            if (!read.IsSuccess)
            {
                return Result<PreparedRunSignals>.Fail([.. read.Errors]);
            }

            progress?.Report(new OperationProgressDto(ImportStageNames.Validating, 90));
            PreparedRunSignals prepared = new(
                request.Source,
                request.Mapping,
                read.Value!.Series,
                read.Value.Diagnostics);
            progress?.Report(new OperationProgressDto(ImportStageNames.Validating, 100));
            return Result<PreparedRunSignals>.Ok(prepared);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<PreparedRunSignals>.Fail(UnsafeResource());
        }
    }

    /// <summary>
    /// Converts the Application-owned mapping into the import adapter's own
    /// shape. Both describe the same logical coordinates; this is the only
    /// place the two assemblies' types meet.
    /// </summary>
    private static DataMapping ToDataMapping(PreparedDataMapping mapping) => new(
        mapping.RunId,
        ToChannelRegion(mapping.Fam),
        ToChannelRegion(mapping.Rox));

    private static ChannelRegionMapping ToChannelRegion(PreparedChannelRegion region) => new(
        region.Channel,
        region.SheetName,
        region.HeaderRowIndex,
        region.TimeColumnIndex,
        new Dictionary<int, WellId>(region.WellColumns));

    private string? ResolveSafeResource(RelativePath relativePath)
    {
        string combined = Path.Combine(managedRoot, relativePath.Value.Replace('/', Path.DirectorySeparatorChar));
        string resolved = Path.GetFullPath(combined);
        string rootPrefix = managedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? managedRoot
            : managedRoot + Path.DirectorySeparatorChar;
        return resolved.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) ? resolved : null;
    }

    private bool HasReparsePoint(RelativePath relativePath)
    {
        var rootInfo = new DirectoryInfo(managedRoot);
        if (rootInfo.Exists && rootInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return true;
        }

        string current = managedRoot;
        string[] segments = relativePath.Value.Split('/');
        for (int index = 0; index < segments.Length; index++)
        {
            current = Path.Combine(current, segments[index]);
            FileSystemInfo info = index == segments.Length - 1 ? new FileInfo(current) : new DirectoryInfo(current);
            if (info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> HasZipMagicAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] header = new byte[4];
        int read = await stream.ReadAsync(header, cancellationToken).ConfigureAwait(false);
        stream.Position = 0;
        return read == 4 && header.AsSpan().SequenceEqual("PK\x03\x04"u8);
    }

    private static async Task<bool> LooksLikeTextAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] sample = new byte[4096];
        int read = await stream.ReadAsync(sample, cancellationToken).ConfigureAwait(false);
        stream.Position = 0;
        if (read == 0)
        {
            return true;
        }

        ReadOnlySpan<byte> bytes = sample.AsSpan(0, read);
        try
        {
            if (read >= 2 && sample[0] == 0xff && sample[1] == 0xfe)
            {
                return (read - 2) % 2 == 0
                    && new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true)
                        .GetCharCount(bytes[2..]) >= 0;
            }

            if (read >= 2 && sample[0] == 0xfe && sample[1] == 0xff)
            {
                return (read - 2) % 2 == 0
                    && new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true)
                        .GetCharCount(bytes[2..]) >= 0;
            }

            ReadOnlySpan<byte> utf8 = read >= 3 && bytes[..3].SequenceEqual("\xef\xbb\xbf"u8)
                ? bytes[3..]
                : bytes;
            return !utf8.Contains((byte)0)
                && new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                    .GetCharCount(utf8) >= 0;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private IImporter? SelectImporter(PreparedSourceIdentity source, bool hasZipMagic, bool looksLikeText)
    {
        string displayExtension = Path.GetExtension(source.DisplayName).ToLowerInvariant();
        string resourceExtension = Path.GetExtension(source.ResourcePath.FileName).ToLowerInvariant();
        if (!string.Equals(displayExtension, resourceExtension, StringComparison.Ordinal))
        {
            return null;
        }

        if (hasZipMagic)
        {
            return displayExtension == ".xlsx" ? xlsxImporter : null;
        }

        bool textExtension = displayExtension is ".csv" or ".tsv" or ".txt";
        return looksLikeText && textExtension ? delimitedImporter : null;
    }

    private static AppError ResourceNotFound() => new(
        ErrorCodes.ImportManagedResourceNotFound, ErrorSeverity.Blocking,
        "The managed import resource is not available.", "resourceMissing");

    private static AppError SizeMismatch(long declared, long actual) => new(
        ErrorCodes.ImportManagedResourceSizeMismatch, ErrorSeverity.Blocking,
        "The managed import resource size changed after selection.",
        $"declaredBytes={declared};actualBytes={actual}");

    private static AppError UnsafeResource() => new(
        ErrorCodes.ImportManagedResourceUnsafe, ErrorSeverity.Blocking,
        "The managed import resource could not be opened safely.", "unsafeManagedResource");

    private static AppError FormatMismatch() => new(
        ImportErrorCodes.ImportFormatMismatch, ErrorSeverity.Blocking,
        "The file name and content signature do not identify the same supported format.",
        "formatEvidenceMismatch");
}
