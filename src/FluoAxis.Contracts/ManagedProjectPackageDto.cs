namespace FluoAxis.Contracts;

using FluoAxis.Domain;

/// <summary>Safe identity of a validated portable package inside the managed workspace.</summary>
public sealed record ManagedProjectPackageDto
{
    public string DisplayName { get; }
    public string RelativeResourcePath { get; }
    public string Sha256 { get; }
    public long FileSizeBytes { get; }

    public ManagedProjectPackageDto(string displayName, string relativeResourcePath, string sha256, long fileSizeBytes)
    {
        ContractGuard.RequireNonBlank(displayName, nameof(displayName));
        ContractGuard.RequireSha256(sha256, nameof(sha256));
        ContractGuard.RequirePositive(fileSizeBytes, nameof(fileSizeBytes));
        if (displayName.Length > 255 || displayName.IndexOfAny(['/', '\\', ':']) >= 0 || displayName.Any(char.IsControl)
            || !displayName.EndsWith(".fluoaxisproj", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A package display name must be a bare .fluoaxisproj file name.", nameof(displayName));
        RelativePath entry = RelativePath.Parse(relativeResourcePath);
        if (!entry.Value.EndsWith(".fluoaxisproj", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A managed package entry must have the .fluoaxisproj extension.", nameof(relativeResourcePath));
        DisplayName = displayName;
        RelativeResourcePath = entry.Value;
        Sha256 = sha256.ToLowerInvariant();
        FileSizeBytes = fileSizeBytes;
    }
}
