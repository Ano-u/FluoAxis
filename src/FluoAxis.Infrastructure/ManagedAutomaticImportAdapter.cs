namespace FluoAxis.Infrastructure;

using System.Security.Cryptography;
using FluoAxis.Contracts;
using FluoAxis.Domain;
using FluoAxis.Import;

/// <summary>Read-only recognition over a hash-verified managed source. External paths never enter this API.</summary>
public sealed class ManagedAutomaticImportAdapter
{
    private readonly string managedRoot;
    private readonly ImportLimits limits;

    public ManagedAutomaticImportAdapter(string managedRoot, ImportLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        if (!Path.IsPathFullyQualified(managedRoot)) throw new ArgumentException("A managed root is required.", nameof(managedRoot));
        this.managedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(managedRoot));
        this.limits = limits ?? ImportLimits.Default;
    }

    public async Task<GatewayResult<AutomaticImportProposalDto>> RecognizeAsync(SourceFileDto source,
        CancellationToken cancellationToken, ImportOptionsDto? configuredOptions = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            RelativePath entry = RelativePath.Parse(source.RelativeResourcePath);
            string path = ResolveSafe(entry);
            await using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (input.Length != source.FileSizeBytes || input.Length > limits.MaxBytes)
                return Failure("IMPORT_AUTO_SOURCE_CHANGED", "所选托管文件的大小已改变或超过导入限制，请重新选择文件。");
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[64 * 1024]; long readBytes = 0;
            while (true)
            {
                int read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                readBytes += read;
                if (readBytes > source.FileSizeBytes || readBytes > limits.MaxBytes)
                    return Failure("IMPORT_AUTO_SOURCE_CHANGED", "托管源文件在识别前校验时发生变化，请重新选择文件。");
                hash.AppendData(buffer, 0, read);
            }
            if (readBytes != source.FileSizeBytes || Convert.ToHexStringLower(hash.GetHashAndReset()) != source.Sha256)
                return Failure("IMPORT_AUTO_SOURCE_CHANGED", "源文件 SHA-256 已改变，不能用旧身份继续自动识别。");
            input.Position = 0;
            ImportFileFormat format = Path.GetExtension(source.OriginalFileName).ToLowerInvariant() switch
            {
                ".xlsx" when XlsxImporter.HasZipMagic(input) => ImportFileFormat.Xlsx,
                ".xls" when XlsImporter.HasSignature(input) => ImportFileFormat.Xls,
                ".csv" or ".tsv" or ".txt" when !XlsxImporter.HasZipMagic(input) && !XlsImporter.HasSignature(input) => ImportFileFormat.DelimitedText,
                _ => ImportFileFormat.Unknown,
            };
            if (format == ImportFileFormat.Unknown)
                return Failure("IMPORT_AUTO_FORMAT_MISMATCH", "源文件扩展名与内容签名不一致，请核对文件格式。");
            RecognitionParsingOptions? configured = configuredOptions is null ? null : new(configuredOptions.EncodingName,
                configuredOptions.Delimiter, configuredOptions.DecimalCulture, configuredOptions.TimeUnit);
            Result<AutomaticRecognitionResult> recognized = await AutomaticImportRecognizer.RecognizeAsync(
                input, format, limits, cancellationToken, configured).ConfigureAwait(false);
            if (!recognized.IsSuccess)
                return Failure("IMPORT_AUTO_SOURCE_UNREADABLE", "无法在受限前缀内安全识别文件，请查看手工导入选项。");
            AutomaticRecognitionResult result = recognized.Value!;
            foreach (string text in result.Assignments.Select(item => item.SampleId).Where(value => value is not null).Cast<string>()
                .Concat(new[] { result.Fam?.SheetName, result.Rox?.SheetName }.Where(value => value is not null).Cast<string>()))
                if (SafeDiagnostics.RedactMachinePaths(text) != text)
                    return Failure("IMPORT_AUTO_UNSAFE_METADATA", "识别元数据包含不允许的机器路径，请在源文件中移除路径再导入。");
            ChannelRegionMappingDto? fam = ToMapping(result.Fam);
            ChannelRegionMappingDto? rox = ToMapping(result.Rox);
            ImportOptionsDto? options = result.Options is null ? null : new(result.Options.EncodingName,
                result.Options.Delimiter, result.Options.DecimalCulture, result.Options.TimeUnit);
            return GatewayResult.Ok(new AutomaticImportProposalDto(format switch
            {
                ImportFileFormat.Xlsx => "xlsx", ImportFileFormat.Xls => "xls", _ => "delimitedText",
            }, options, fam, rox,
                [.. result.Assignments.Select(role => new WellAssignmentDto(role.Well.ToString(), RoleName(role.Role),
                    sampleId: role.SampleId, replicateIndex: role.ReplicateIndex))],
                [.. result.Issues.Select(issue => new IssueDto(issue.Code, issue.Message, "核对文件标记、解析选项和孔位方案；不明确的项目请手工确认。"))],
                result.Reasons, result.MappingReady, result.RequiresReview));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return Failure("IMPORT_AUTO_SOURCE_UNREADABLE", "无法安全读取托管源文件，请重新选择并检查访问权限。"); }
    }

    private string ResolveSafe(RelativePath entry)
    {
        for (DirectoryInfo? ancestor = new(managedRoot); ancestor is not null; ancestor = ancestor.Parent)
            if (ancestor.Exists && ancestor.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Unsafe managed root.");
        string current = managedRoot;
        foreach (string segment in entry.Value.Split('/'))
        {
            current = Path.Combine(current, segment);
            if (Path.Exists(current) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Unsafe managed source.");
        }
        return current;
    }

    private static ChannelRegionMappingDto? ToMapping(ChannelRegionMapping? mapping) => mapping is null ? null : new(
        mapping.Channel == FluorescenceChannel.Fam ? ChannelNames.Fam : ChannelNames.Rox,
        mapping.SheetName, mapping.HeaderRowIndex, mapping.TimeColumnIndex,
        [.. mapping.WellColumns.OrderBy(item => item.Key).Select(item => new WellColumnMappingDto(item.Key, item.Value.ToString()))], mapping.EndRowIndex);

    private static string RoleName(WellRole role) => role switch
    {
        WellRole.Ntc => WellRoleNames.Ntc, WellRole.Positive100 => WellRoleNames.Positive100,
        WellRole.WildType => WellRoleNames.WildType, WellRole.Lqc01Percent => WellRoleNames.Lqc01Percent,
        WellRole.Sample => WellRoleNames.Sample, WellRole.Ignore => WellRoleNames.Ignore,
        _ => throw new ArgumentException("Unsupported role."),
    };

    private static GatewayResult<AutomaticImportProposalDto> Failure(string code, string message) =>
        GatewayResult.Fail<AutomaticImportProposalDto>(new AppErrorDto(code, SeverityNames.Blocking, message,
            "自动识别没有修改 Run，也没有执行判读。请修正源文件或选择手工导入。"));
}
