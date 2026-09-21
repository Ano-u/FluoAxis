namespace FluoAxis.ProjectFormat;

using System.Text.Json;
using FluoAxis.Domain;

/// <summary>
/// Conversion and strict read/write between the domain manifest model and
/// its persisted JSON form. Any malformed value (bad enum name, bad hash,
/// rooted path, unknown JSON) surfaces as a structured
/// <see cref="ProjectErrorCodes.ManifestInvalid"/> error, never as a silent
/// default.
/// </summary>
internal static class ManifestJson
{
    /// <summary>Serializes a manifest to compact UTF-8 JSON bytes.</summary>
    public static byte[] Write(ProjectManifest manifest)
    {
        return JsonSerializer.SerializeToUtf8Bytes(ToPersisted(manifest), ProjectFormatJsonContext.Default.PersistedProjectManifest);
    }

    /// <summary>
    /// Parses manifest bytes. JSON syntax errors, missing required
    /// properties, unknown entry types, invalid hashes, and rooted or
    /// escaping paths all fail closed with <see cref="AppError"/>.
    /// </summary>
    public static Result<ProjectManifest> Read(ReadOnlySpan<byte> utf8Json)
    {
        PersistedProjectManifest? persisted;
        try
        {
            persisted = JsonSerializer.Deserialize(
                utf8Json,
                ProjectFormatJsonContext.Default.PersistedProjectManifest);
        }
        catch (JsonException ex)
        {
            return Result<ProjectManifest>.Fail(new AppError(
                ProjectErrorCodes.ManifestInvalid,
                ErrorSeverity.Blocking,
                "The package manifest is not valid JSON.",
                detailOf(ex)));
        }

        if (persisted is null)
        {
            return Result<ProjectManifest>.Fail(new AppError(
                ProjectErrorCodes.ManifestInvalid,
                ErrorSeverity.Blocking,
                "The package manifest is empty or null.",
                "null"));
        }

        return FromPersisted(persisted);
    }

    internal static PersistedProjectManifest ToPersisted(ProjectManifest manifest)
    {
        PersistedManifestEntry[] entries = new PersistedManifestEntry[manifest.Entries.Count];

        for (int i = 0; i < manifest.Entries.Count; i++)
        {
            ManifestEntry entry = manifest.Entries[i];
            entries[i] = new PersistedManifestEntry
            {
                Path = entry.RelativePath.Value,
                EntryType = entry.EntryType.ToString(),
                Sha256 = entry.Sha256.Value,
                SizeBytes = entry.SizeBytes,
            };
        }

        return new PersistedProjectManifest
        {
            Format = manifest.Format,
            FormatVersion = manifest.FormatVersion,
            ProjectId = manifest.ProjectId,
            DisplayName = manifest.DisplayName,
            CreatedAtUtc = manifest.CreatedAtUtc,
            ModifiedAtUtc = manifest.ModifiedAtUtc,
            AppVersion = manifest.AppVersion,
            MethodVersions = manifest.MethodVersions,
            MethodSnapshotHashes = manifest.MethodSnapshotHashes,
            HashAlgorithm = manifest.HashAlgorithm,
            Entries = entries,
        };
    }

    private static Result<ProjectManifest> FromPersisted(PersistedProjectManifest persisted)
    {
        try
        {
            ManifestEntry[] entries = new ManifestEntry[persisted.Entries.Count];

            for (int i = 0; i < persisted.Entries.Count; i++)
            {
                PersistedManifestEntry source = persisted.Entries[i];

                if (!Enum.TryParse<ProjectEntryType>(source.EntryType, ignoreCase: false, out ProjectEntryType entryType))
                {
                    return Result<ProjectManifest>.Fail(new AppError(
                        ProjectErrorCodes.ManifestInvalid,
                        ErrorSeverity.Blocking,
                        $"A manifest entry declares unknown entry type '{source.EntryType}'.",
                        "entryType"));
                }

                entries[i] = ManifestEntry.FromPersisted(
                    source.Path,
                    entryType,
                    source.Sha256,
                    source.SizeBytes);
            }

            ProjectManifest manifest = new()
            {
                Format = persisted.Format,
                FormatVersion = persisted.FormatVersion,
                ProjectId = persisted.ProjectId,
                DisplayName = persisted.DisplayName,
                CreatedAtUtc = persisted.CreatedAtUtc,
                ModifiedAtUtc = persisted.ModifiedAtUtc,
                AppVersion = persisted.AppVersion,
                MethodVersions = persisted.MethodVersions,
                MethodSnapshotHashes = persisted.MethodSnapshotHashes,
                HashAlgorithm = persisted.HashAlgorithm,
                Entries = entries,
            };

            List<AppError> validationErrors = manifest.Validate();
            return validationErrors.Count == 0
                ? Result<ProjectManifest>.Ok(manifest)
                : Result<ProjectManifest>.Fail([.. validationErrors]);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or ArgumentOutOfRangeException)
        {
            return Result<ProjectManifest>.Fail(new AppError(
                ProjectErrorCodes.ManifestInvalid,
                ErrorSeverity.Blocking,
                "The package manifest contains an invalid entry value.",
                detailOf(ex)));
        }
    }

    private static string detailOf(Exception ex) => ex.GetType().Name;
}
