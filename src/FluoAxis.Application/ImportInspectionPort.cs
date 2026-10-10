namespace FluoAxis.Application;

using FluoAxis.Contracts;
using FluoAxis.Domain;

/// <summary>Application-owned identity for a resource already staged beneath a managed root.</summary>
public sealed record ManagedImportRequest
{
    public Guid RunId { get; }
    public string DisplayName { get; }
    public long DeclaredSizeBytes { get; }
    public RelativePath ResourcePath { get; }
    public string? FormatHint { get; }
    public ImportOptionsDto? Options { get; }

    public ManagedImportRequest(Guid runId, string displayName, long declaredSizeBytes, RelativePath resourcePath, string? formatHint, ImportOptionsDto? options = null)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("RunId must not be empty.", nameof(runId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (displayName.IndexOfAny(['/', '\\']) >= 0)
        {
            throw new ArgumentException("DisplayName must be a bare file name, not a path.", nameof(displayName));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(declaredSizeBytes);
        ArgumentNullException.ThrowIfNull(resourcePath);

        RunId = runId;
        DisplayName = displayName;
        DeclaredSizeBytes = declaredSizeBytes;
        ResourcePath = resourcePath;
        FormatHint = formatHint;
        Options = options;
    }
}

/// <summary>Port for bounded, read-only inspection of a managed import resource.</summary>
public interface IImportInspectionPort
{
    Task<Result<ImportPreviewDto>> InspectAsync(
        ManagedImportRequest request,
        IProgress<OperationProgressDto>? progress,
        CancellationToken cancellationToken);
}
