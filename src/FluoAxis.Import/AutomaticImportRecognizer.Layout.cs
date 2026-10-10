namespace FluoAxis.Import;

using System.Globalization;
using System.Text.RegularExpressions;
using FluoAxis.Domain;

public sealed partial class AutomaticImportRecognizer
{
    private sealed record HeaderLayout(int RowIndex, int TimeColumn, string? TimeUnit,
        Dictionary<int, WellId> Fam, Dictionary<int, WellId> Rox);
    private sealed record ChannelCandidate(ChannelRegionMapping Mapping, string? TimeUnit);

    private static AutomaticRecognitionResult Analyze(PrefixBook book, Findings findings,
        RecognitionParsingOptions? configured, CancellationToken token)
    {
        List<ChannelCandidate> fam = [];
        List<ChannelCandidate> rox = [];
        Dictionary<WellId, RecognizedWellRole> roles = [];
        HashSet<WellId> conflictingRoles = [];
        foreach (PrefixSheet sheet in book.Sheets)
        {
            token.ThrowIfCancellationRequested();
            ReadExplicitRoleTables(sheet, roles, conflictingRoles, findings);
            FluorescenceChannel? sheetChannel = ChannelLabel(sheet.Name);
            string? metadataUnit = ReadUnitMetadata(sheet, findings);
            List<HeaderLayout> headers = [];
            foreach (RawRow row in sheet.Rows)
            {
                token.ThrowIfCancellationRequested();
                RawCell[] timeCells = [.. row.Cells.Where(cell => cell.Text is not null && TryTimeHeader(cell.Text, out _))];
                if (timeCells.Length == 0) continue;
                FluorescenceChannel? contextChannel = sheetChannel ?? ReadChannelMetadata(sheet.Rows, row.RowIndex);
                Dictionary<int, WellId> famColumns = [];
                Dictionary<int, WellId> roxColumns = [];
                foreach (RawCell cell in row.Cells)
                {
                    if (cell.Text is null || !TrySignalHeader(cell.Text, contextChannel, out WellId well,
                        out FluorescenceChannel? channel, out string[] annotations)) continue;
                    if (channel is null)
                    {
                        findings.Issue("IMPORT_AUTO_MAPPING_AMBIGUOUS", "发现孔号但没有明确的 FAM/ROX 通道标记，请选择通道区域。");
                        continue;
                    }
                    Dictionary<int, WellId> columns = channel == FluorescenceChannel.Fam ? famColumns : roxColumns;
                    if (columns.ContainsValue(well))
                        findings.Issue("IMPORT_AUTO_MAPPING_AMBIGUOUS", "同一通道重复出现相同孔号，不能自动选择列。");
                    else columns.Add(cell.ColumnIndex, well);
                    if (annotations.Length > 0)
                    {
                        RecognizedWellRole? assigned = ParseRoleAnnotations(well, annotations, findings);
                        if (assigned is not null) MergeRole(assigned, roles, conflictingRoles, findings);
                    }
                }
                if (famColumns.Count == 0 && roxColumns.Count == 0) continue;
                if (timeCells.Length != 1)
                {
                    findings.Issue("IMPORT_AUTO_MAPPING_AMBIGUOUS", "同一数据表头包含多个 Time/Elapsed 列，需要人工选择。");
                    continue;
                }
                _ = TryTimeHeader(timeCells[0].Text!, out string? headerUnit);
                string timeSuffix = TimeHeaderPattern().Match(timeCells[0].Text!.Trim()).Groups[1].Value.Trim(' ', '_', '-', '/', '(', ')', '[', ']', ':', '（', '）');
                if (headerUnit is null && timeSuffix.Length != 0)
                    findings.Issue("IMPORT_AUTO_TIME_UNIT_REQUIRED", "Time/Elapsed 后的单位标记不受支持；用户方案不能覆盖明确但未知的文件单位。");
                if (headerUnit is not null && metadataUnit is not null && headerUnit != metadataUnit)
                    findings.Issue("IMPORT_AUTO_TIME_UNIT_CONFLICT", "表头时间单位与文件显式单位元数据冲突。");
                headers.Add(new HeaderLayout(row.RowIndex, timeCells[0].ColumnIndex, headerUnit ?? metadataUnit, famColumns, roxColumns));
            }

            if (sheet.Truncated && headers.Count > 0)
                findings.Issue("IMPORT_AUTO_PREFIX_LIMIT", "数据超出自动识别前缀范围，后续可能存在额外区域；请确认区域边界后读取。");
            for (int index = 0; index < headers.Count; index++)
            {
                HeaderLayout header = headers[index];
                int? end = index + 1 < headers.Count ? headers[index + 1].RowIndex - 1 : null;
                RawRow? plateBoundary = sheet.Rows.FirstOrDefault(row => row.RowIndex > header.RowIndex && IsRoleTableHeader(row, out _));
                if (plateBoundary is not null && (end is null || plateBoundary.RowIndex - 1 < end)) end = plateBoundary.RowIndex - 1;
                ValidateNumericPrefix(sheet, header, end, configured, findings);
                if (header.TimeUnit is not null && configured is not null && header.TimeUnit != configured.TimeUnit)
                    findings.Issue("IMPORT_AUTO_TIME_UNIT_CONFLICT", "文件显式时间单位与已选孔位方案中的单位冲突，请复核后选择。");
                string? unit = header.TimeUnit ?? configured?.TimeUnit;
                if (unit is null) findings.Issue("IMPORT_AUTO_TIME_UNIT_REQUIRED", "Time/Elapsed 表头没有明确时间单位；请在导入或孔位方案中确认分钟、秒、小时或天。");
                else findings.Reason(header.TimeUnit is not null ? "时间列由语义 Time/Elapsed 表头定位，单位来自文件显式标记。" : "文件未写时间单位，使用用户已保存并明确选择的单位。");
                if (header.Fam.Count > 0)
                    fam.Add(new ChannelCandidate(new ChannelRegionMapping(FluorescenceChannel.Fam, sheet.Name, header.RowIndex, header.TimeColumn, header.Fam, end), unit));
                if (header.Rox.Count > 0)
                    rox.Add(new ChannelCandidate(new ChannelRegionMapping(FluorescenceChannel.Rox, sheet.Name, header.RowIndex, header.TimeColumn, header.Rox, end), unit));
            }
        }

        ChannelRegionMapping? selectedFam = fam.Count == 1 ? fam[0].Mapping : null;
        ChannelRegionMapping? selectedRox = rox.Count == 1 ? rox[0].Mapping : null;
        if (fam.Count != 1 || rox.Count != 1)
            findings.Issue("IMPORT_AUTO_MAPPING_AMBIGUOUS", "没有找到唯一的一对 FAM/ROX 区域；缺少通道或存在多个候选布局。");
        if (selectedFam is not null && selectedRox is not null)
        {
            if (!selectedFam.WellColumns.Values.ToHashSet().SetEquals(selectedRox.WellColumns.Values))
                findings.Issue("IMPORT_AUTO_WELL_SET_MISMATCH", "FAM 与 ROX 的孔集合不同，需要修正通道映射。");
            try { _ = new DataMapping(Guid.NewGuid(), selectedFam, selectedRox); }
            catch (ArgumentException) { findings.Issue("IMPORT_AUTO_MAPPING_AMBIGUOUS", "FAM 与 ROX 数据单元格重叠，不能将同一信号作为两个通道。"); }
            findings.Reason("通道只依据工作表/明确通道元数据或孔列的 FAM/ROX 前后缀识别；单孔表也可产生建议。");
        }
        string? selectedUnit = fam.Count == 1 && rox.Count == 1 && fam[0].TimeUnit == rox[0].TimeUnit ? fam[0].TimeUnit : null;
        if (fam.Count == 1 && rox.Count == 1 && fam[0].TimeUnit != rox[0].TimeUnit)
            findings.Issue("IMPORT_AUTO_TIME_UNIT_CONFLICT", "两个通道使用不同的时间单位，当前导入合同需要统一单位。");
        RecognitionParsingOptions? options = selectedUnit is null || book.EncodingName is null || book.Delimiter is null ? null
            : new RecognitionParsingOptions(book.EncodingName, book.Delimiter, configured?.DecimalCulture ?? "invariant", selectedUnit);
        HashSet<WellId> mapped = selectedFam?.WellColumns.Values.ToHashSet() ?? [];
        RecognizedWellRole[] assignments = [.. roles.Values.Where(role => mapped.Contains(role.Well) && !conflictingRoles.Contains(role.Well))
            .OrderBy(role => role.Well.Row).ThenBy(role => role.Well.Column)];
        if (mapped.Count == 0 || assignments.Length != mapped.Count)
            findings.Issue("IMPORT_AUTO_ROLES_REQUIRED", "部分孔位没有明确角色信息；请选择已保存的孔位方案或核对孔位分配。", parsing: false);
        foreach (WellRole required in new[] { WellRole.Ntc, WellRole.Positive100, WellRole.WildType, WellRole.Lqc01Percent })
            if (assignments.All(assignment => assignment.Role != required))
                findings.Issue("IMPORT_AUTO_ROLES_REQUIRED", "文件没有明确给出所有必要控制角色；请选择已保存的孔位方案或核对孔位分配。", parsing: false);
        if (assignments.Where(assignment => assignment.Role == WellRole.Sample).GroupBy(assignment => assignment.SampleId, StringComparer.Ordinal)
            .Any(group => group.Select(assignment => assignment.ReplicateIndex).Distinct().Count() != group.Count()))
            findings.Issue("IMPORT_AUTO_ROLE_CONFLICT", "同一样本有重复的 Replicate 序号，请核对显式孔位元数据。", parsing: false);
        bool mappingReady = !findings.ParsingBlocked && options is not null && selectedFam is not null && selectedRox is not null;
        bool requiresReview = !mappingReady || findings.Issues.Count > 0;
        if (assignments.Length > 0) findings.Reason("孔位角色仅来自表头标签、明确的 Well/Role 元数据或 PlateMap 表，不依据 RFU 大小推断。");
        return new AutomaticRecognitionResult(findings.Format, options, selectedFam, selectedRox,
            Array.AsReadOnly(assignments), findings.Issues.AsReadOnly(), findings.Reasons.AsReadOnly(), mappingReady, requiresReview);
    }

    private static void ValidateNumericPrefix(PrefixSheet sheet, HeaderLayout header, int? end,
        RecognitionParsingOptions? configured, Findings findings)
    {
        int[] signalColumns = [.. header.Fam.Keys.Concat(header.Rox.Keys).Distinct()];
        int rows = 0; double? previous = null;
        CultureInfo culture = configured?.DecimalCulture is { } selected && selected != "invariant" ? CultureInfo.GetCultureInfo(selected) : CultureInfo.InvariantCulture;
        foreach (RawRow row in sheet.Rows.Where(row => row.RowIndex > header.RowIndex && (end is null || row.RowIndex <= end)))
        {
            if (row.Cells.All(cell => cell.Number is null && string.IsNullOrWhiteSpace(cell.Text))) continue;
            if (!TryPrefixNumber(row, header.TimeColumn, culture, out double time)
                || signalColumns.Any(column => !TryPrefixNumber(row, column, culture, out _)))
            {
                findings.Issue("IMPORT_AUTO_NUMERIC_REVIEW", "识别区域含空值、非有限值或不符合已选小数文化的数字；请核对区域和解析规则。");
                return;
            }
            if (previous is not null && time <= previous)
                findings.Issue("IMPORT_AUTO_TIME_ORDER_REVIEW", "时间前缀有重复或倒序值，不能自动提交该映射。");
            previous = time; rows++;
        }
        if (rows == 0) findings.Issue("IMPORT_AUTO_NO_SIGNAL_ROWS", "语义表头后未发现有效的双通道数据行，请核对区域。");
    }

    private static bool TryPrefixNumber(RawRow row, int column, CultureInfo culture, out double number)
    {
        RawCell cell = row.Cells.FirstOrDefault(cell => cell.ColumnIndex == column);
        if (cell.Number is { } value) { number = value; return double.IsFinite(value); }
        return double.TryParse(cell.Text, NumberStyles.Float, culture, out number) && double.IsFinite(number);
    }

    private static bool TryTimeHeader(string text, out string? unit)
    {
        unit = null;
        string normalized = text.Trim().ToLowerInvariant();
        Match match = TimeHeaderPattern().Match(normalized);
        if (!match.Success) return false;
        string suffix = match.Groups[1].Value.Trim(' ', '_', '-', '/', '(', ')', '[', ']', ':', '（', '）');
        unit = ParseUnit(suffix);
        return true;
    }

    private static string? ParseUnit(string text) => text.Trim().ToLowerInvariant() switch
    {
        "min" or "mins" or "minute" or "minutes" or "分钟" => "minutes",
        "s" or "sec" or "secs" or "second" or "seconds" or "秒" => "seconds",
        "h" or "hr" or "hour" or "hours" or "小时" => "hours",
        "d" or "day" or "days" or "天" => "days",
        _ => null,
    };

    private static bool TrySignalHeader(string text, FluorescenceChannel? fallback,
        out WellId well, out FluorescenceChannel? channel, out string[] annotations)
    {
        string[] parts = text.Split('|', StringSplitOptions.TrimEntries);
        annotations = parts.Length > 1 ? parts[1..] : [];
        string core = parts[0].Trim();
        channel = fallback;
        Match explicitMatch = ChannelWellPattern().Match(core);
        string wellText;
        if (explicitMatch.Success)
        {
            string token = explicitMatch.Groups["channel"].Value;
            channel = string.Equals(token, "fam", StringComparison.OrdinalIgnoreCase) ? FluorescenceChannel.Fam : FluorescenceChannel.Rox;
            wellText = explicitMatch.Groups["well"].Value;
        }
        else wellText = core;
        try { well = WellId.Parse(wellText); return true; }
        catch (Exception error) when (error is ArgumentException or FormatException) { well = default; return false; }
    }

    private static FluorescenceChannel? ChannelLabel(string text)
    {
        MatchCollection matches = ChannelTokenPattern().Matches(text);
        string[] tokens = [.. matches.Select(match => match.Value.ToUpperInvariant()).Distinct(StringComparer.Ordinal)];
        return tokens.Length == 1 ? tokens[0] == "FAM" ? FluorescenceChannel.Fam : FluorescenceChannel.Rox : null;
    }

    private static FluorescenceChannel? ReadChannelMetadata(IReadOnlyList<RawRow> rows, int before)
    {
        foreach (RawRow row in rows.Where(row => row.RowIndex < before).Reverse())
        {
            string[] texts = [.. row.Cells.Where(cell => !string.IsNullOrWhiteSpace(cell.Text)).Select(cell => cell.Text!.Trim())];
            if (texts.Length == 1 && texts[0].ToUpperInvariant() is "FAM" or "ROX") return ChannelLabel(texts[0]);
            if (texts.Length == 2 && texts[0].ToLowerInvariant() is "channel" or "通道") return ChannelLabel(texts[1]);
        }
        return null;
    }

    private static string? ReadUnitMetadata(PrefixSheet sheet, Findings findings)
    {
        HashSet<string> units = [];
        foreach (RawRow row in sheet.Rows)
        {
            string[] texts = [.. row.Cells.Where(cell => !string.IsNullOrWhiteSpace(cell.Text)).Select(cell => cell.Text!.Trim())];
            if (texts.Length == 2 && texts[0].Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant() is "timeunit" or "elapsedunit" or "时间单位")
            {
                string? unit = ParseUnit(texts[1]);
                if (unit is null) findings.Issue("IMPORT_AUTO_TIME_UNIT_REQUIRED", "文件单位元数据不是支持的明确时间单位。");
                else units.Add(unit);
            }
        }
        if (units.Count > 1) findings.Issue("IMPORT_AUTO_TIME_UNIT_CONFLICT", "文件声明了多个不同时间单位，请手工核对。");
        return units.Count == 1 ? units.Single() : null;
    }

    [GeneratedRegex(@"^(?:elapsed\s*[_-]?\s*time|elapsed|time|经过时间|时间)(?:\s|[_/(:\[（-]|$)(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TimeHeaderPattern();
    [GeneratedRegex(@"^(?:(?<well>[A-H](?:[1-9]|1[0-2]))[ _-]+(?<channel>FAM|ROX)|(?<channel>FAM|ROX)[ _-]+(?<well>[A-H](?:[1-9]|1[0-2])))$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChannelWellPattern();
    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:FAM|ROX)(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChannelTokenPattern();
}
