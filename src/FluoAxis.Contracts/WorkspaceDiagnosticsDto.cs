namespace FluoAxis.Contracts;

/// <summary>Allowlisted technical data only; no project names, sources, patient fields, or exception messages.</summary>
public sealed record DiagnosticEventDto(DateTimeOffset OccurredUtc, string Code, string Category, Guid CorrelationId);

public sealed record WorkspaceDiagnosticsDto(string SoftwareVersion, string FrameworkVersion, string OperatingSystemVersion,
    string Architecture, int LogicalProcessors, long ManagedMemoryBytes, int CatalogPageCount, bool MoreProjectsAvailable,
    bool RecoveredUncleanSession, IReadOnlyList<DiagnosticEventDto> RecentEvents);

public sealed record SupportBundleDto(string RelativeEntry, string Sha256, long FileSizeBytes);
