namespace FluoAxis.ProjectFormat;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Wire (persisted) form of <see cref="ManifestEntry"/>. Plain strings only:
/// the path is re-validated through <see cref="Domain.RelativePath.Parse"/>
/// on conversion back to the domain model, so a rooted or escaping value
/// fails closed at deserialization time (CLAUDE.md §4.3). Public because
/// <see cref="ProjectFormatJsonContext"/> is a source-generated public
/// serializer context.
/// </summary>
public sealed record PersistedManifestEntry
{
    public required string Path { get; init; }

    public required string EntryType { get; init; }

    public required string Sha256 { get; init; }

    public required long SizeBytes { get; init; }
}

/// <summary>
/// Wire (persisted) form of <see cref="ProjectManifest"/>. Property names
/// serialize camelCase; every field is required so a truncated manifest
/// fails as <see cref="ProjectErrorCodes.ManifestInvalid"/> instead of
/// silently defaulting. Public for the same source-generation accessibility
/// reason as <see cref="PersistedManifestEntry"/>.
/// </summary>
public sealed record PersistedProjectManifest
{
    public required string Format { get; init; }

    public required int FormatVersion { get; init; }

    public required Guid ProjectId { get; init; }

    public required string DisplayName { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset ModifiedAtUtc { get; init; }

    public required string AppVersion { get; init; }

    public required IReadOnlyList<string> MethodVersions { get; init; }

    public required IReadOnlyList<string> MethodSnapshotHashes { get; init; }

    public required string HashAlgorithm { get; init; }

    public required IReadOnlyList<PersistedManifestEntry> Entries { get; init; }
}

/// <summary>
/// System.Text.Json source-generation context for the .fluoaxisproj manifest.
/// Settings are part of the persisted package contract (plan.md §7.4):
/// camelCase property names, compact output, reflection-free.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PersistedProjectManifest))]
public sealed partial class ProjectFormatJsonContext : JsonSerializerContext
{
}
