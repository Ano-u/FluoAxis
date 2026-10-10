namespace FluoAxis.Import;

using System.Globalization;
using FluoAxis.Domain;

public sealed partial class AutomaticImportRecognizer
{
    private sealed record RoleColumns(int Well, int Role, int? Sample, int? Replicate);

    private static void ReadExplicitRoleTables(PrefixSheet sheet, Dictionary<WellId, RecognizedWellRole> roles,
        HashSet<WellId> conflicts, Findings findings)
    {
        RoleColumns? columns = null;
        foreach (RawRow row in sheet.Rows)
        {
            if (IsRoleTableHeader(row, out RoleColumns? detected)) { columns = detected; continue; }
            if (columns is not null)
            {
                string? wellText = TextAt(row, columns.Well);
                if (string.IsNullOrWhiteSpace(wellText)) { columns = null; continue; }
                if (!TryWell(wellText, out WellId well)) { columns = null; continue; }
                List<string> annotations = [TextAt(row, columns.Role) ?? string.Empty];
                if (columns.Sample is { } sample && TextAt(row, sample) is { Length: > 0 } sampleId) annotations.Add("SampleId=" + sampleId);
                if (columns.Replicate is { } replicate && TextAt(row, replicate) is { Length: > 0 } replicateId) annotations.Add("Replicate=" + replicateId);
                RecognizedWellRole? role = ParseRoleAnnotations(well, [.. annotations], findings);
                if (role is not null) MergeRole(role, roles, conflicts, findings);
                continue;
            }
            // Explicit key/value metadata is also accepted without a PlateMap
            // sheet: Well=A1, Role=NTC, SampleId=S1, Replicate=1.
            string[] texts = [.. row.Cells.Where(cell => cell.Text is not null).Select(cell => cell.Text!.Trim())];
            string[] wellTokens = [.. texts.Where(text => text.StartsWith("Well=", StringComparison.OrdinalIgnoreCase))];
            if (wellTokens.Length == 1 && TryWell(wellTokens[0][5..], out WellId explicitWell))
            {
                RecognizedWellRole? role = ParseRoleAnnotations(explicitWell,
                    [.. texts.Where(text => !text.StartsWith("Well=", StringComparison.OrdinalIgnoreCase))], findings);
                if (role is not null) MergeRole(role, roles, conflicts, findings);
            }
        }
    }

    private static bool IsRoleTableHeader(RawRow row, out RoleColumns? columns)
    {
        int? well = null, role = null, sample = null, replicate = null;
        foreach (RawCell cell in row.Cells)
        {
            string normalized = (cell.Text ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            switch (normalized)
            {
                case "well": case "wellid": case "孔位": well = cell.ColumnIndex; break;
                case "role": case "角色": role = cell.ColumnIndex; break;
                case "sampleid": case "样本编号": sample = cell.ColumnIndex; break;
                case "replicate": case "replicateindex": case "重复孔": replicate = cell.ColumnIndex; break;
            }
        }
        columns = well is { } wellColumn && role is { } roleColumn ? new RoleColumns(wellColumn, roleColumn, sample, replicate) : null;
        return columns is not null;
    }

    private static RecognizedWellRole? ParseRoleAnnotations(WellId well, string[] annotations, Findings findings)
    {
        WellRole? role = null;
        string? sampleId = null;
        int? replicate = null;
        bool invalid = false;
        foreach (string annotation in annotations)
        {
            string token = annotation.Trim();
            int separator = token.IndexOf('=');
            string key = separator >= 0 ? token[..separator].Trim().ToLowerInvariant() : string.Empty;
            string value = separator >= 0 ? token[(separator + 1)..].Trim() : token;
            if (key is "sample" or "sampleid")
            {
                if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || sampleId is not null && sampleId != value) invalid = true;
                else sampleId = value;
                if (key == "sample")
                {
                    if (role is not null && role != WellRole.Sample) invalid = true;
                    role = WellRole.Sample;
                }
                continue;
            }
            if (key is "replicate" or "replicateindex" or "rep")
            {
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                    || index < 1 || replicate is not null && replicate != index) invalid = true;
                else replicate = index;
                continue;
            }
            WellRole? candidate = key is "" or "role" ? ParseRole(value) : null;
            if (candidate is null || role is not null && role != candidate) invalid = true;
            else role = candidate;
        }
        if (invalid || role is null)
        {
            findings.Issue("IMPORT_AUTO_ROLE_CONFLICT", "显式角色标签存在未知值或互相冲突，请核对孔位标签。", parsing: false);
            return null;
        }
        if (role == WellRole.Sample && (sampleId is null || replicate is null))
        {
            findings.Issue("IMPORT_AUTO_SAMPLE_METADATA_REQUIRED", "Sample 孔必须明确提供 SampleId 和 Replicate；不会自动编造样本编号或重复序号。", parsing: false);
            return null;
        }
        if (role != WellRole.Sample && (sampleId is not null || replicate is not null))
        {
            findings.Issue("IMPORT_AUTO_ROLE_CONFLICT", "非 Sample 角色带有样本元数据，请核对孔位角色。", parsing: false);
            return null;
        }
        return new RecognizedWellRole(well, role.Value, sampleId, replicate);
    }

    private static WellRole? ParseRole(string role) => role.Trim().Replace(" ", string.Empty, StringComparison.Ordinal)
        .Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant() switch
    {
        "ntc" or "notemplatecontrol" or "空白对照" => WellRole.Ntc,
        "positive100" or "100%positive" or "positive100%" or "100%pos" or "100%阳性" => WellRole.Positive100,
        "wt" or "wildtype" or "野生型" => WellRole.WildType,
        "lqc" or "lqc0.1%" or "lqc01percent" or "0.1%lqc" => WellRole.Lqc01Percent,
        "sample" or "样本" => WellRole.Sample,
        "ignore" or "忽略" => WellRole.Ignore,
        _ => null,
    };

    private static void MergeRole(RecognizedWellRole role, Dictionary<WellId, RecognizedWellRole> roles,
        HashSet<WellId> conflicts, Findings findings)
    {
        if (conflicts.Contains(role.Well)) return;
        if (roles.TryGetValue(role.Well, out RecognizedWellRole? existing) && existing != role)
        {
            conflicts.Add(role.Well); roles.Remove(role.Well);
            findings.Issue("IMPORT_AUTO_ROLE_CONFLICT", "同一孔在文件中被赋予不同角色或样本信息，请人工复核。", parsing: false);
            return;
        }
        roles[role.Well] = role;
    }

    private static string? TextAt(RawRow row, int column)
    {
        RawCell cell = row.Cells.FirstOrDefault(cell => cell.ColumnIndex == column);
        return cell.Text ?? cell.Number?.ToString("G17", CultureInfo.InvariantCulture);
    }

    private static bool TryWell(string value, out WellId well)
    {
        try { well = WellId.Parse(value.Trim()); return true; }
        catch (Exception error) when (error is ArgumentException or FormatException) { well = default; return false; }
    }
}
