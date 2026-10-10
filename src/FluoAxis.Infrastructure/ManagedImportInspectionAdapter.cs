namespace FluoAxis.Infrastructure;

using System.Text;
using FluoAxis.Application;
using FluoAxis.Contracts;
using FluoAxis.Domain;
using FluoAxis.Import;

/// <summary>Resolves and inspects one project-relative resource beneath an injected managed root.</summary>
public sealed class ManagedImportInspectionAdapter : IImportInspectionPort
{
    private readonly string managedRoot;
    private readonly ImportLimits limits;
    private readonly IImporter xlsxImporter;
    private readonly IImporter delimitedImporter;

    public ManagedImportInspectionAdapter(
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

    public async Task<Result<ImportPreviewDto>> InspectAsync(
        ManagedImportRequest request,
        IProgress<OperationProgressDto>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgressDto(ImportStageNames.DetectingFormat, 0));

        string? resource = ResolveSafeResource(request.ResourcePath);
        if (resource is null)
        {
            return Result<ImportPreviewDto>.Fail(UnsafeResource());
        }

        FileInfo file;
        try
        {
            file = new FileInfo(resource);
            if (!file.Exists)
            {
                return Result<ImportPreviewDto>.Fail(ResourceNotFound());
            }

            if (HasReparsePoint(request.ResourcePath))
            {
                return Result<ImportPreviewDto>.Fail(UnsafeResource());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<ImportPreviewDto>.Fail(UnsafeResource());
        }

        if (file.Length != request.DeclaredSizeBytes)
        {
            return Result<ImportPreviewDto>.Fail(SizeMismatch(request.DeclaredSizeBytes, file.Length));
        }

        if (file.Length > limits.MaxBytes)
        {
            return Result<ImportPreviewDto>.Fail(new AppError(
                ImportErrorCodes.ImportFileTooLarge,
                ErrorSeverity.Blocking,
                $"The file exceeds the import size limit of {limits.MaxBytes} bytes.",
                $"maxBytes={limits.MaxBytes}"));
        }

        try
        {
            await using var stream = new FileStream(
                resource, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length != request.DeclaredSizeBytes)
            {
                return Result<ImportPreviewDto>.Fail(SizeMismatch(request.DeclaredSizeBytes, stream.Length));
            }

            if (stream.Length > limits.MaxBytes)
            {
                return Result<ImportPreviewDto>.Fail(new AppError(
                    ImportErrorCodes.ImportFileTooLarge,
                    ErrorSeverity.Blocking,
                    $"The file exceeds the import size limit of {limits.MaxBytes} bytes.",
                    $"maxBytes={limits.MaxBytes}"));
            }

            bool hasZipMagic = await HasZipMagicAsync(stream, cancellationToken).ConfigureAwait(false);
            bool looksLikeText = !hasZipMagic && await LooksLikeTextAsync(stream, cancellationToken).ConfigureAwait(false);
            IImporter? importer = SelectImporter(request, hasZipMagic, looksLikeText);
            if (Path.GetExtension(request.DisplayName).Equals(".xls", StringComparison.OrdinalIgnoreCase)
                && Path.GetExtension(request.ResourcePath.FileName).Equals(".xls", StringComparison.OrdinalIgnoreCase))
                importer = XlsImporter.HasSignature(stream) && request.FormatHint?.ToLowerInvariant() is null or "xls" or "application/vnd.ms-excel" ? new XlsImporter() : null;
            else if (!hasZipMagic && request.Options is { } options
                && Path.GetExtension(request.DisplayName).Equals(Path.GetExtension(request.ResourcePath.FileName), StringComparison.OrdinalIgnoreCase)
                && (request.FormatHint?.ToLowerInvariant() is null or "csv" or "tsv" or "txt" or "delimited" or "delimitedtext" or "text/csv" or "text/tab-separated-values" or "text/plain")
                && (Path.GetExtension(request.DisplayName).ToLowerInvariant() is ".csv" or ".tsv" or ".txt"))
                importer = new DelimitedTextImporter(options: ToReadOptions(options));
            if (importer is null)
            {
                return Result<ImportPreviewDto>.Fail(FormatMismatch());
            }

            progress?.Report(new OperationProgressDto(ImportStageNames.DiscoveringRegion, 20));
            Result<ImportPreview> inspected = await importer.InspectAsync(stream, limits, cancellationToken).ConfigureAwait(false);
            if (!inspected.IsSuccess)
            {
                return Result<ImportPreviewDto>.Fail([.. inspected.Errors]);
            }

            progress?.Report(new OperationProgressDto(ImportStageNames.Validating, 90));
            ImportPreviewDto preview = ToDto(inspected.Value!);
            progress?.Report(new OperationProgressDto(ImportStageNames.Validating, 100));
            return Result<ImportPreviewDto>.Ok(preview);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<ImportPreviewDto>.Fail(UnsafeResource());
        }
    }

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

    private IImporter? SelectImporter(ManagedImportRequest request, bool hasZipMagic, bool looksLikeText)
    {
        string displayExtension = Path.GetExtension(request.DisplayName).ToLowerInvariant();
        string resourceExtension = Path.GetExtension(request.ResourcePath.FileName).ToLowerInvariant();
        if (!string.Equals(displayExtension, resourceExtension, StringComparison.Ordinal))
        {
            return null;
        }

        string? hint = request.FormatHint?.Trim().ToLowerInvariant();
        bool hintXlsx = hint is "xlsx" or "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        bool hintText = hint is "csv" or "tsv" or "txt" or "delimited" or "delimitedtext"
            or "text/csv" or "text/tab-separated-values" or "text/plain";
        bool knownHint = hint is null || hintXlsx || hintText;

        if (!knownHint)
        {
            return null;
        }

        if (hasZipMagic)
        {
            return displayExtension == ".xlsx" && !hintText ? xlsxImporter : null;
        }

        bool textExtension = displayExtension is ".csv" or ".tsv" or ".txt";
        return looksLikeText && textExtension && !hintXlsx ? delimitedImporter : null;
    }

    private static ImportPreviewDto ToDto(ImportPreview preview)
    {
        RegionCandidateDto[] candidates =
        [
            .. preview.Regions.Select(region => new RegionCandidateDto(
                region.SheetName,
                region.HeaderRowIndex,
                region.TimeColumnIndex,
                [.. region.WellColumnMap.OrderBy(pair => pair.Key).Select(pair => new WellColumnMappingDto(pair.Key, pair.Value.ToString()))],
                region.ChannelSuggestion switch
                {
                    FluorescenceChannel.Fam => ChannelNames.Fam,
                    FluorescenceChannel.Rox => ChannelNames.Rox,
                    null => null,
                    _ => throw new ArgumentOutOfRangeException(nameof(preview)),
                },
                region.EstimatedDataRows)),
        ];

        return new ImportPreviewDto(
            preview.Format == ImportFileFormat.Xlsx ? "xlsx" : preview.Format == ImportFileFormat.Xls ? "xls" : "delimitedText",
            [.. preview.Regions.Select(region => region.SheetName)],
            candidates,
            validationIssues: []);
    }

    private static AppError ResourceNotFound() => new(
        ErrorCodes.ImportManagedResourceNotFound, ErrorSeverity.Blocking,
        "The managed import resource is not available.", "resourceMissing");

    private static ImportReadOptions ToReadOptions(ImportOptionsDto options) =>
        new(options.EncodingName, options.Delimiter, options.DecimalCulture, options.TimeUnit);

    private static AppError SizeMismatch(long declared, long actual) => new(
        ErrorCodes.ImportManagedResourceSizeMismatch, ErrorSeverity.Blocking,
        "The managed import resource size changed after selection.",
        $"declaredBytes={declared};actualBytes={actual}");

    private static AppError UnsafeResource() => new(
        ErrorCodes.ImportManagedResourceUnsafe, ErrorSeverity.Blocking,
        "The managed import resource could not be opened safely.", "unsafeManagedResource");

    private static AppError FormatMismatch() => new(
        ImportErrorCodes.ImportFormatMismatch, ErrorSeverity.Blocking,
        "The file name, format hint, and content signature do not identify the same supported format.",
        "formatEvidenceMismatch");
}
