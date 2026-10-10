namespace FluoAxis.Infrastructure;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluoAxis.Contracts;
using FluoAxis.Domain;

public sealed partial class PersistentDesktopStore : IPlateProfileGateway
{
    private const int MaximumPlateProfiles = 100;
    private const int MaximumPlateProfileBytes = 64 * 1024;
    private static readonly RelativePath ProfileFolder = RelativePath.Parse("profiles");

    public Task<GatewayResult<PlateProfileDto>> SavePlateProfileAsync(string name, IReadOnlyList<WellAssignmentDto> assignments,
        bool remainingWellsAreSamples, bool makeDefault, CancellationToken cancellationToken, ImportOptionsDto? options = null) =>
        ExecuteAsync(async () =>
        {
            PlateProfileDefinitionDto definition;
            try
            {
                ArgumentNullException.ThrowIfNull(assignments);
                if (assignments.Count > 96 || assignments.Any(item => item is null)) throw new ArgumentException("Invalid profile assignment budget.");
                definition = new(name, assignments.Select(item => new PlateProfileWellDto(item.Well, item.Role)).ToArray(), remainingWellsAreSamples, options);
                if (SafeDiagnostics.RedactMachinePaths(definition.Name) != definition.Name) throw new ArgumentException("Invalid profile label.");
            }
            catch (Exception error) when (error is ArgumentException or FormatException)
            { return ProfileFailure<PlateProfileDto>("PLATE_PROFILE_INVALID", "孔位方案必须包含四类明确对照、唯一孔位和不超过 64 字的名称。"); }
            Directory.CreateDirectory(Resolve(ProfileFolder));
            if (ProfileEntries().Length >= MaximumPlateProfiles)
                return ProfileFailure<PlateProfileDto>("PLATE_PROFILE_LIMIT", "孔位方案已达到 100 个的保存上限。");
            var profile = new PlateProfileDto(Guid.NewGuid(), definition, ProfileHash(definition), DateTimeOffset.UtcNow);
            await WriteProfileBytesAsync(ProfileEntry(profile.ProfileId), JsonSerializer.SerializeToUtf8Bytes(profile,
                PlateProfileJsonContext.Default.PlateProfileDto), overwrite: false, cancellationToken).ConfigureAwait(false);
            if (makeDefault) await WriteDefaultProfileAsync(profile, cancellationToken).ConfigureAwait(false);
            return GatewayResult.Ok(WithDefault(profile, makeDefault));
        }, cancellationToken);

    public Task<GatewayResult<IReadOnlyList<PlateProfileDto>>> ListPlateProfilesAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            string[] entries = ProfileEntries();
            if (entries.Length > MaximumPlateProfiles)
                return ProfileFailure<IReadOnlyList<PlateProfileDto>>("PLATE_PROFILE_LIMIT", "保存的孔位方案数量超出预算。");
            List<PlateProfileDto> profiles = new(entries.Length);
            try
            {
                (Guid Id, string Hash)? selected = await ReadDefaultProfileAsync(cancellationToken).ConfigureAwait(false);
                foreach (string entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string name = Path.GetFileNameWithoutExtension(entry);
                    if (!name.StartsWith("profile-", StringComparison.Ordinal) || !Guid.TryParseExact(name[8..], "N", out Guid id))
                        throw new InvalidDataException("Invalid profile identity.");
                    PlateProfileDto profile = await ReadProfileAsync(id, cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidDataException("Profile is missing.");
                    bool isDefault = selected is { } choice && choice.Id == id;
                    if (isDefault && selected!.Value.Hash != profile.Sha256) throw new InvalidDataException("Invalid default profile identity.");
                    profiles.Add(WithDefault(profile, isDefault));
                }
                if (selected is not null && !profiles.Any(profile => profile.IsDefault)) throw new InvalidDataException("Default profile is missing.");
                return GatewayResult.Ok<IReadOnlyList<PlateProfileDto>>(profiles.OrderByDescending(profile => profile.IsDefault)
                    .ThenBy(profile => profile.Definition.Name, StringComparer.Ordinal).ThenBy(profile => profile.ProfileId).ToArray());
            }
            catch (Exception error) when (error is JsonException or ArgumentException or FormatException or InvalidDataException)
            { return ProfileFailure<IReadOnlyList<PlateProfileDto>>("PLATE_PROFILE_INVALID", "保存的孔位方案或默认引用损坏，请人工复核后重新保存方案。"); }
        }, cancellationToken);

    public Task<GatewayResult<PlateProfileDto?>> GetPlateProfileAsync(Guid profileId, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            if (profileId == Guid.Empty) return ProfileFailure<PlateProfileDto?>("PLATE_PROFILE_INVALID", "孔位方案标识无效。");
            try
            {
                PlateProfileDto? profile = await ReadProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
                if (profile is null) return GatewayResult.Ok<PlateProfileDto?>(null);
                (Guid Id, string Hash)? selected = await ReadDefaultProfileAsync(cancellationToken).ConfigureAwait(false);
                bool isDefault = selected is { } choice && choice.Id == profileId;
                if (isDefault && selected!.Value.Hash != profile.Sha256) throw new InvalidDataException("Invalid default identity.");
                return GatewayResult.Ok<PlateProfileDto?>(WithDefault(profile, isDefault));
            }
            catch (Exception error) when (error is JsonException or ArgumentException or FormatException or InvalidDataException)
            { return ProfileFailure<PlateProfileDto?>("PLATE_PROFILE_INVALID", "保存的孔位方案损坏，不能用于自动分析。"); }
        }, cancellationToken);

    public Task<GatewayResult<PlateProfileDto>> SetDefaultPlateProfileAsync(Guid profileId, CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            try
            {
                if (profileId == Guid.Empty) throw new ArgumentException("Missing profile identity.");
                PlateProfileDto? profile = await ReadProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
                if (profile is null) return ProfileFailure<PlateProfileDto>("PLATE_PROFILE_NOT_FOUND", "孔位方案不存在。");
                await WriteDefaultProfileAsync(profile, cancellationToken).ConfigureAwait(false);
                return GatewayResult.Ok(WithDefault(profile, true));
            }
            catch (Exception error) when (error is JsonException or ArgumentException or FormatException or InvalidDataException)
            { return ProfileFailure<PlateProfileDto>("PLATE_PROFILE_INVALID", "孔位方案无效，默认方案没有改变。"); }
        }, cancellationToken);

    private string[] ProfileEntries()
    {
        string folder = Resolve(ProfileFolder);
        return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "profile-*.json", SearchOption.TopDirectoryOnly)
            .Take(MaximumPlateProfiles + 1).Select(path => Path.GetFileName(path)).ToArray() : [];
    }

    private async Task<PlateProfileDto?> ReadProfileAsync(Guid id, CancellationToken token)
    {
        string path = Resolve(ProfileEntry(id));
        if (!File.Exists(path)) return null;
        byte[] bytes = await ReadProfileBytesAsync(ProfileEntry(id), MaximumPlateProfileBytes, token).ConfigureAwait(false);
        PlateProfileDto profile = JsonSerializer.Deserialize(bytes, PlateProfileJsonContext.Default.PlateProfileDto)
            ?? throw new InvalidDataException("Profile is absent.");
        if (profile.ProfileId != id || profile.IsDefault || ProfileHash(profile.Definition) != profile.Sha256
            || SafeDiagnostics.RedactMachinePaths(profile.Definition.Name) != profile.Definition.Name)
            throw new InvalidDataException("Profile integrity is invalid.");
        return profile;
    }

    private async Task<byte[]> ReadProfileBytesAsync(RelativePath entry, int maximum, CancellationToken token)
    {
        await using var stream = new FileStream(Resolve(entry), FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (stream.Length is <= 0 || stream.Length > maximum) throw new InvalidDataException("Profile budget exceeded.");
        byte[] bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return bytes;
    }

    private async Task WriteProfileBytesAsync(RelativePath entry, byte[] bytes, bool overwrite, CancellationToken token)
    {
        if (bytes.Length > MaximumPlateProfileBytes) throw new InvalidDataException("Profile budget exceeded.");
        string temporary = Resolve(ProfileFolder.Combine($"write-{Guid.NewGuid():N}.tmp"));
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, token).ConfigureAwait(false);
            byte[] reread = await File.ReadAllBytesAsync(temporary, token).ConfigureAwait(false);
            if (!bytes.AsSpan().SequenceEqual(reread)) throw new InvalidDataException("Profile write verification failed.");
            token.ThrowIfCancellationRequested();
            File.Move(temporary, Resolve(entry), overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private Task WriteDefaultProfileAsync(PlateProfileDto profile, CancellationToken token) =>
        WriteProfileBytesAsync(ProfileFolder.Combine("default.txt"), Encoding.ASCII.GetBytes($"{profile.ProfileId:N}\n{profile.Sha256}"), overwrite: true, token);

    private async Task<(Guid Id, string Hash)?> ReadDefaultProfileAsync(CancellationToken token)
    {
        RelativePath entry = ProfileFolder.Combine("default.txt");
        if (!File.Exists(Resolve(entry))) return null;
        string[] values = Encoding.ASCII.GetString(await ReadProfileBytesAsync(entry, 128, token).ConfigureAwait(false)).Split('\n');
        if (values.Length != 2 || !Guid.TryParseExact(values[0], "N", out Guid id) || values[1].Length != 64 || !values[1].All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid default profile reference.");
        return (id, values[1]);
    }

    private static RelativePath ProfileEntry(Guid id) => ProfileFolder.Combine($"profile-{id:N}.json");
    private static string ProfileHash(PlateProfileDefinitionDto definition) => Convert.ToHexStringLower(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(definition, PlateProfileJsonContext.Default.PlateProfileDefinitionDto)));
    private static PlateProfileDto WithDefault(PlateProfileDto profile, bool isDefault) =>
        new(profile.ProfileId, profile.Definition, profile.Sha256, profile.CreatedUtc, isDefault, profile.Version);
    private static GatewayResult<T> ProfileFailure<T>(string code, string message) => GatewayResult.Fail<T>(
        new AppErrorDto(code, SeverityNames.Blocking, message, "请选择有效的已保存方案，或手工确认孔位后重新保存。"));
}
