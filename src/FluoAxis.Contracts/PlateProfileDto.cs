namespace FluoAxis.Contracts;

using System.Text.Json.Serialization;
using FluoAxis.Domain;

/// <summary>Reusable role geometry only. Sample identities and experiment metadata are never copied into a new Run.</summary>
public sealed record PlateProfileWellDto
{
    public string Well { get; }
    public string Role { get; }
    public PlateProfileWellDto(string well, string role)
    {
        ContractGuard.RequireNonBlank(well, nameof(well));
        if (role is not (WellRoleNames.Ntc or WellRoleNames.Positive100 or WellRoleNames.WildType
            or WellRoleNames.Lqc01Percent or WellRoleNames.Sample or WellRoleNames.Ignore))
            throw new ArgumentException("Unsupported profile role.", nameof(role));
        Well = WellId.Parse(well).ToString(); Role = role;
    }
}

public sealed record PlateProfileDefinitionDto
{
    public string Name { get; }
    public IReadOnlyList<PlateProfileWellDto> Wells { get; }
    public bool RemainingWellsAreSamples { get; }
    public ImportOptionsDto? Options { get; }
    public PlateProfileDefinitionDto(string name, IReadOnlyList<PlateProfileWellDto> wells,
        bool remainingWellsAreSamples, ImportOptionsDto? options = null)
    {
        ContractGuard.RequireNonBlank(name, nameof(name));
        if (name.Length > 64 || name.Any(char.IsControl) || name.IndexOfAny(['/', '\\']) >= 0)
            throw new ArgumentException("Profile names must be short labels without paths.", nameof(name));
        ArgumentNullException.ThrowIfNull(wells);
        if (wells.Count is < 4 or > 96 || wells.Any(well => well is null)
            || wells.Select(well => well.Well).Distinct(StringComparer.Ordinal).Count() != wells.Count)
            throw new ArgumentException("A profile must contain unique wells within the plate budget.", nameof(wells));
        foreach (string role in new[] { WellRoleNames.Ntc, WellRoleNames.Positive100, WellRoleNames.WildType, WellRoleNames.Lqc01Percent })
            if (!wells.Any(well => well.Role == role)) throw new ArgumentException("Every required control role must be explicit.", nameof(wells));
        Name = name.Trim(); Wells = Array.AsReadOnly(wells.OrderBy(well => well.Well, StringComparer.Ordinal).ToArray());
        RemainingWellsAreSamples = remainingWellsAreSamples; Options = options;
    }
}

public sealed record PlateProfileDto
{
    public Guid ProfileId { get; }
    public PlateProfileDefinitionDto Definition { get; }
    public string Sha256 { get; }
    public DateTimeOffset CreatedUtc { get; }
    public bool IsDefault { get; }
    public int Version { get; }
    public PlateProfileDto(Guid profileId, PlateProfileDefinitionDto definition, string sha256,
        DateTimeOffset createdUtc, bool isDefault = false, int version = 1)
    {
        ContractGuard.RequireNonEmptyGuid(profileId, nameof(profileId)); ArgumentNullException.ThrowIfNull(definition);
        ContractGuard.RequireSha256(sha256, nameof(sha256)); ContractGuard.RequireUtc(createdUtc, nameof(createdUtc));
        if (version != 1) throw new ArgumentException("Unsupported plate profile version.", nameof(version));
        ProfileId = profileId; Definition = definition; Sha256 = sha256.ToLowerInvariant();
        CreatedUtc = createdUtc; IsDefault = isDefault; Version = version;
    }
}

/// <summary>Additive capability independent of the version 3 analysis gateway.</summary>
public interface IPlateProfileGateway
{
    Task<GatewayResult<PlateProfileDto>> SavePlateProfileAsync(string name, IReadOnlyList<WellAssignmentDto> assignments,
        bool remainingWellsAreSamples, bool makeDefault, CancellationToken cancellationToken, ImportOptionsDto? options = null);
    Task<GatewayResult<IReadOnlyList<PlateProfileDto>>> ListPlateProfilesAsync(CancellationToken cancellationToken);
    Task<GatewayResult<PlateProfileDto?>> GetPlateProfileAsync(Guid profileId, CancellationToken cancellationToken);
    Task<GatewayResult<PlateProfileDto>> SetDefaultPlateProfileAsync(Guid profileId, CancellationToken cancellationToken);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PlateProfileDto))]
[JsonSerializable(typeof(PlateProfileDefinitionDto))]
public sealed partial class PlateProfileJsonContext : JsonSerializerContext;
