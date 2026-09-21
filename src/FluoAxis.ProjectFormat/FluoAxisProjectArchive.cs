namespace FluoAxis.ProjectFormat;

using System.IO.Compression;
using System.Security.Cryptography;
using FluoAxis.Domain;

/// <summary>
/// Reader/writer of the .fluoaxisproj ZIP project package (CLAUDE.md §21,
/// plan.md §7.4). Opening is a full defensive pass before any content is
/// accepted: ZIP structure, per-entry path legality, exact and
/// case-insensitive collisions, bomb limits, manifest presence and
/// validity, hash verification of every entry, and manifest/zip
/// cross-consistency. Only a package that passes every check becomes a
/// <see cref="ProjectPackage"/>. Saved packages contain relative paths
/// only (P0 invariant).
/// </summary>
public static class FluoAxisProjectArchive
{
    private const int CopyBufferSize = 81920;

    /// <summary>
    /// Opens and fully validates a package from a readable stream. The
    /// returned package owns copies of all validated entry bytes, so the
    /// stream may be closed afterward.
    /// </summary>
    public static async Task<Result<ProjectPackage>> OpenAsync(
        Stream packageStream,
        PackageLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packageStream);

        PackageLimits checkedLimits = limits ?? PackageLimits.Default;

        if (!packageStream.CanRead)
        {
            return Result<ProjectPackage>.Fail(new AppError(
                ProjectErrorCodes.NotAPackage,
                ErrorSeverity.Blocking,
                "The package stream is not readable.",
                "stream"));
        }

        try
        {
            using ZipArchive archive = new(packageStream, ZipArchiveMode.Read, leaveOpen: true);
            return await OpenCoreAsync(archive, checkedLimits, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            return CorruptZipError();
        }
        catch (IOException)
        {
            return CorruptZipError();
        }
        catch (NotSupportedException)
        {
            return CorruptZipError();
        }
    }

    /// <summary>
    /// Saves a package: hashes every entry, assembles the complete manifest,
    /// then writes the ZIP with manifest.json last. The destination stream
    /// receives a complete, self-consistent package. Callers following the
    /// atomic save flow write to a temporary sibling and swap via
    /// <see cref="AtomicFile.Replace"/>; on failure or cancellation the
    /// destination may contain a partial package and must be discarded.
    /// </summary>
    public static async Task<Result<ProjectManifest>> SaveAsync(
        Stream destination,
        ProjectPackageContents contents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(contents);

        if (string.IsNullOrWhiteSpace(contents.DisplayName))
        {
            return Result<ProjectManifest>.Fail(new AppError(
                ProjectErrorCodes.SaveInvalidContents,
                ErrorSeverity.Blocking,
                "The package contents have no display name.",
                "displayName"));
        }

        // Self-validate before writing anything: reserved manifest name,
        // duplicate paths, and case-insensitive collisions all fail closed
        // (the remaining structural checks run via manifest.Validate()).
        HashSet<string> ordinal = new(StringComparer.Ordinal);
        HashSet<string> ignoreCase = new(StringComparer.OrdinalIgnoreCase);
        foreach (ProjectPackageContentFile file in contents.Files)
        {
            if (file.Path.Value.Equals(ManifestEntry.ManifestPath, StringComparison.OrdinalIgnoreCase))
            {
                return Result<ProjectManifest>.Fail(new AppError(
                    ProjectErrorCodes.SaveInvalidContents,
                    ErrorSeverity.Blocking,
                    "An entry may not use the reserved manifest name.",
                    file.Path.Value));
            }

            if (!ordinal.Add(file.Path.Value))
            {
                return Result<ProjectManifest>.Fail(new AppError(
                    ProjectErrorCodes.DuplicateEntry,
                    ErrorSeverity.Blocking,
                    "The save contents declare the same path twice.",
                    file.Path.Value));
            }

            if (!ignoreCase.Add(file.Path.Value))
            {
                return Result<ProjectManifest>.Fail(new AppError(
                    ProjectErrorCodes.CaseCollision,
                    ErrorSeverity.Blocking,
                    "The save contents declare paths that collide case-insensitively.",
                    file.Path.Value));
            }
        }

        ManifestEntry[] entries = new ManifestEntry[contents.Files.Count];
        for (int i = 0; i < contents.Files.Count; i++)
        {
            ProjectPackageContentFile file = contents.Files[i];
            entries[i] = new ManifestEntry
            {
                RelativePath = file.Path,
                EntryType = file.EntryType,
                Sha256 = HexHash.FromBytes(SHA256.HashData(file.Content)),
                SizeBytes = file.Content.LongLength,
            };
        }

        ProjectManifest manifest;
        try
        {
            manifest = ProjectManifest.Create(
                contents.ProjectId,
                contents.DisplayName,
                contents.CreatedAtUtc,
                contents.ModifiedAtUtc,
                contents.MethodVersions,
                contents.MethodSnapshotHashes,
                entries);
        }
        catch (ArgumentException)
        {
            return Result<ProjectManifest>.Fail(new AppError(
                ProjectErrorCodes.SaveInvalidContents,
                ErrorSeverity.Blocking,
                "The package contents carry invalid manifest metadata.",
                "metadata"));
        }

        List<AppError> manifestErrors = manifest.Validate();
        if (manifestErrors.Count > 0)
        {
            return Result<ProjectManifest>.Fail([.. manifestErrors]);
        }

        try
        {
            using ZipArchive archive = new(destination, ZipArchiveMode.Create, leaveOpen: true);

            foreach (ProjectPackageContentFile file in contents.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ZipArchiveEntry zipEntry = archive.CreateEntry(file.Path.Value, CompressionLevel.Optimal);
                using Stream entryStream = zipEntry.Open();
                await entryStream.WriteAsync(file.Content, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            byte[] manifestBytes = ManifestJson.Write(manifest);
            ZipArchiveEntry manifestZipEntry = archive.CreateEntry(ManifestEntry.ManifestPath, CompressionLevel.Optimal);
            using Stream manifestStream = manifestZipEntry.Open();
            await manifestStream.WriteAsync(manifestBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            return SaveFailedError();
        }
        catch (IOException)
        {
            return SaveFailedError();
        }
        catch (NotSupportedException)
        {
            return SaveFailedError();
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        return Result<ProjectManifest>.Ok(manifest);
    }

    private static async Task<Result<ProjectPackage>> OpenCoreAsync(
        ZipArchive archive,
        PackageLimits limits,
        CancellationToken cancellationToken)
    {
        // ---- Pass 1: structural scan of every entry name (no content read) ----
        Result<Dictionary<string, ScannedEntry>> scan = ScanEntries(archive, limits);
        if (!scan.IsSuccess)
        {
            return Result<ProjectPackage>.Fail([.. scan.Errors]);
        }

        Dictionary<string, ScannedEntry> fileEntries = scan.Value!;

        // ---- Pass 2: manifest presence, size bound, and parse ----
        if (!fileEntries.TryGetValue(ManifestEntry.ManifestPath, out ScannedEntry? manifestEntry))
        {
            return Result<ProjectPackage>.Fail(new AppError(
                ProjectErrorCodes.ManifestMissing,
                ErrorSeverity.Blocking,
                "The package does not contain a manifest.json.",
                ManifestEntry.ManifestPath));
        }

        if (manifestEntry.ZipEntry.Length > limits.MaxManifestUncompressedBytes)
        {
            return Result<ProjectPackage>.Fail(new AppError(
                ProjectErrorCodes.ManifestInvalid,
                ErrorSeverity.Blocking,
                "The package manifest is larger than the permitted limit.",
                ManifestEntry.ManifestPath));
        }

        byte[]? manifestBytes = await TryReadAllBoundedAsync(
            manifestEntry.ZipEntry, limits.MaxManifestUncompressedBytes, cancellationToken).ConfigureAwait(false);
        if (manifestBytes is null)
        {
            return Result<ProjectPackage>.Fail(new AppError(
                ProjectErrorCodes.ManifestInvalid,
                ErrorSeverity.Blocking,
                "The package manifest is larger than the permitted limit.",
                ManifestEntry.ManifestPath));
        }

        Result<ProjectManifest> manifestResult = ManifestJson.Read(manifestBytes);
        if (!manifestResult.IsSuccess)
        {
            return Result<ProjectPackage>.Fail([.. manifestResult.Errors]);
        }

        ProjectManifest manifest = manifestResult.Value!;

        // ---- Pass 3: manifest/zip cross-consistency ----
        foreach (string path in fileEntries.Keys.Order(StringComparer.Ordinal))
        {
            if (path.Equals(ManifestEntry.ManifestPath, StringComparison.Ordinal))
            {
                continue;
            }

            if (!manifest.Entries.Any(e => e.RelativePath.Value.Equals(path, StringComparison.Ordinal)))
            {
                return Result<ProjectPackage>.Fail(new AppError(
                    ProjectErrorCodes.EntryNotDeclared,
                    ErrorSeverity.Blocking,
                    "The package contains an entry that the manifest does not declare.",
                    path));
            }
        }

        foreach (ManifestEntry entry in manifest.Entries)
        {
            if (!fileEntries.TryGetValue(entry.RelativePath.Value, out _))
            {
                return Result<ProjectPackage>.Fail(new AppError(
                    ProjectErrorCodes.EntryNotDeclared,
                    ErrorSeverity.Blocking,
                    "The manifest declares an entry that is missing from the package.",
                    entry.RelativePath.Value));
            }
        }

        // ---- Pass 4: bomb limits and hash verification of every declared entry ----
        long totalUncompressed = 0;
        Dictionary<string, byte[]> verified = new(StringComparer.Ordinal);

        foreach (ManifestEntry entry in manifest.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ZipArchiveEntry zipEntry = fileEntries[entry.RelativePath.Value].ZipEntry;

            if (entry.SizeBytes != zipEntry.Length)
            {
                return Result<ProjectPackage>.Fail(new AppError(
                    ProjectErrorCodes.EntryHashMismatch,
                    ErrorSeverity.Blocking,
                    "An entry's declared size does not match the package.",
                    entry.RelativePath.Value));
            }

            if (zipEntry.Length > limits.MaxEntryUncompressedBytes)
            {
                return Result<ProjectPackage>.Fail(new AppError(
                    ProjectErrorCodes.EntrySizeExceeded,
                    ErrorSeverity.Blocking,
                    "The package contains an entry larger than the permitted limit.",
                    entry.RelativePath.Value));
            }

            if (zipEntry.CompressedLength > 0
                && zipEntry.Length > (double)zipEntry.CompressedLength * limits.MaxCompressionRatio)
            {
                return Result<ProjectPackage>.Fail(new AppError(
                    ProjectErrorCodes.CompressionRatio,
                    ErrorSeverity.Blocking,
                    "The package contains an entry with an implausible compression ratio.",
                    entry.RelativePath.Value));
            }

            totalUncompressed += zipEntry.Length;
            if (totalUncompressed > limits.MaxTotalUncompressedBytes)
            {
                return Result<ProjectPackage>.Fail(new AppError(
                    ProjectErrorCodes.TotalSizeExceeded,
                    ErrorSeverity.Blocking,
                    "The package's total uncompressed size exceeds the permitted limit.",
                    "total"));
            }

            byte[]? content = await TryReadAllBoundedAsync(
                zipEntry, limits.MaxEntryUncompressedBytes, cancellationToken).ConfigureAwait(false);
            if (content is null)
            {
                return Result<ProjectPackage>.Fail(new AppError(
                    ProjectErrorCodes.EntrySizeExceeded,
                    ErrorSeverity.Blocking,
                    "An entry produced more data than its declared size.",
                    entry.RelativePath.Value));
            }

            string actualHash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            if (!string.Equals(actualHash, entry.Sha256.Value, StringComparison.Ordinal))
            {
                return Result<ProjectPackage>.Fail(new AppError(
                    ProjectErrorCodes.EntryHashMismatch,
                    ErrorSeverity.Blocking,
                    "An entry's content does not match its manifest hash.",
                    entry.RelativePath.Value));
            }

            verified.Add(entry.RelativePath.Value, content);
        }

        return Result<ProjectPackage>.Ok(new ProjectPackage(manifest, verified));
    }

    /// <summary>
    /// Validates every entry name, skips directory markers, and detects
    /// exact and case-insensitive path collisions. Content is never read
    /// here.
    /// </summary>
    private static Result<Dictionary<string, ScannedEntry>> ScanEntries(ZipArchive archive, PackageLimits limits)
    {
        if (archive.Entries.Count > limits.MaxEntryCount)
        {
            return Result<Dictionary<string, ScannedEntry>>.Fail(new AppError(
                ProjectErrorCodes.EntryCountExceeded,
                ErrorSeverity.Blocking,
                "The package contains too many entries.",
                "count"));
        }

        Dictionary<string, ScannedEntry> fileEntries = new(StringComparer.Ordinal);
        Dictionary<string, string> caseInsensitive = new(StringComparer.OrdinalIgnoreCase);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            AppError? nameError = ValidateEntryName(entry.FullName);
            if (nameError is not null)
            {
                return Result<Dictionary<string, ScannedEntry>>.Fail(nameError);
            }

            if (IsDirectoryEntryName(entry.FullName))
            {
                continue;
            }

            RelativePath path = RelativePath.Parse(entry.FullName);

            if (!fileEntries.TryAdd(path.Value, new ScannedEntry(entry, path)))
            {
                return Result<Dictionary<string, ScannedEntry>>.Fail(new AppError(
                    ProjectErrorCodes.DuplicateEntry,
                    ErrorSeverity.Blocking,
                    "The package contains two entries with the same path.",
                    path.Value));
            }

            if (!caseInsensitive.TryAdd(path.Value, path.Value))
            {
                return Result<Dictionary<string, ScannedEntry>>.Fail(new AppError(
                    ProjectErrorCodes.CaseCollision,
                    ErrorSeverity.Blocking,
                    "The package contains entries that collide case-insensitively.",
                    path.Value));
            }
        }

        return Result<Dictionary<string, ScannedEntry>>.Ok(fileEntries);
    }

    /// <summary>
    /// Classifies an entry name before <see cref="RelativePath.Parse"/> is
    /// trusted with it: rooted/URI names and any ".." segment are rejected
    /// outright; everything else must survive parsing.
    /// </summary>
    private static AppError? ValidateEntryName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)
            || fullName.StartsWith('/')
            || fullName.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || Path.IsPathRooted(fullName))
        {
            return EntryNameError(ProjectErrorCodes.EntryAbsolutePath, fullName);
        }

        foreach (string segment in fullName.Split('/', '\\'))
        {
            if (segment == "..")
            {
                return EntryNameError(ProjectErrorCodes.EntryTraversal, fullName);
            }
        }

        try
        {
            RelativePath.Parse(fullName);
            return null;
        }
        catch (FormatException)
        {
            return EntryNameError(ProjectErrorCodes.EntryNameInvalid, fullName);
        }
    }

    private static bool IsDirectoryEntryName(string fullName)
        => fullName.EndsWith('/') || fullName.EndsWith('\\');

    private static AppError EntryNameError(string code, string fullName)
        => new(
            code,
            ErrorSeverity.Blocking,
            "The package contains an entry whose name escapes or violates the package root.",
            fullName);

    /// <summary>
    /// Reads an entry fully, bounded by <paramref name="maxBytes"/>; returns
    /// null when the stream yields more data than allowed (lying header).
    /// </summary>
    private static async Task<byte[]?> TryReadAllBoundedAsync(
        ZipArchiveEntry entry,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        using Stream stream = entry.Open();
        using var buffer = new MemoryStream((int)Math.Min(maxBytes, CopyBufferSize * 4L));

        byte[] chunk = new byte[CopyBufferSize];
        while (true)
        {
            int read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return buffer.ToArray();
    }

    private static Result<ProjectPackage> CorruptZipError()
        => Result<ProjectPackage>.Fail(new AppError(
            ProjectErrorCodes.NotAPackage,
            ErrorSeverity.Blocking,
            "The file is not a readable ZIP package or its entry data is corrupt.",
            "zip"));

    private static Result<ProjectManifest> SaveFailedError()
        => Result<ProjectManifest>.Fail(new AppError(
            ProjectErrorCodes.SaveFailed,
            ErrorSeverity.Blocking,
            "Writing the package failed.",
            "zip"));

    private sealed record ScannedEntry(ZipArchiveEntry ZipEntry, RelativePath Path);
}
