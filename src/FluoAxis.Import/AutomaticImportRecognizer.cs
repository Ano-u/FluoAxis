namespace FluoAxis.Import;

using FluoAxis.Domain;

/// <summary>
/// Bounded, advisory recognition of explicit source labels. It never assigns
/// control roles from RFU, changes a Run, or calls the Analysis engine.
/// </summary>
public sealed partial class AutomaticImportRecognizer
{
    public const int MaximumSheets = 32;
    public const int MaximumRowsPerSheet = 256;
    public const int MaximumPrefixCells = 65_536;
    public const int MaximumTextCharacters = 1_048_576;
    private const int MaximumCellCharacters = 4096;
    private const int MaximumLineCharacters = 65_536;

    public static Task<Result<AutomaticRecognitionResult>> RecognizeAsync(Stream source, ImportFileFormat format,
        ImportLimits limits, CancellationToken cancellationToken, RecognitionParsingOptions? configuredOptions = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using SeekableStreamScope scope = SeekableStreamScope.Create(source, limits);
            var findings = new Findings(format, configuredOptions);
            PrefixBook book = ReadPrefix(scope.Stream, format, limits, configuredOptions, findings, cancellationToken);
            AutomaticRecognitionResult result = Analyze(book, findings, configuredOptions, cancellationToken);
            return Task.FromResult(Result<AutomaticRecognitionResult>.Ok(result));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return Task.FromResult(Result<AutomaticRecognitionResult>.Fail(new AppError(
                "IMPORT_AUTO_SOURCE_UNREADABLE", ErrorSeverity.Blocking,
                "无法在受限读取范围内安全识别源文件，请使用手工导入并核对格式。", "recognitionFailed")));
        }
    }

    private sealed record PrefixSheet(string Name, IReadOnlyList<RawRow> Rows, bool Truncated);
    private sealed record PrefixBook(IReadOnlyList<PrefixSheet> Sheets, string? EncodingName, string? Delimiter);
    private sealed class Findings(ImportFileFormat format, RecognitionParsingOptions? configured)
    {
        public ImportFileFormat Format { get; } = format;
        public List<RecognitionIssue> Issues { get; } = [];
        public List<string> Reasons { get; } = [];
        public bool ParsingBlocked { get; private set; }
        public RecognitionParsingOptions? Configured { get; } = configured;

        public void Issue(string code, string message, bool parsing = true)
        {
            if (parsing) ParsingBlocked = true;
            if (Issues.Count < 128 && Issues.All(item => item.Code != code)) Issues.Add(new RecognitionIssue(code, message));
        }

        public void Reason(string reason)
        {
            if (Reasons.Count < 128 && !Reasons.Contains(reason, StringComparer.Ordinal)) Reasons.Add(reason);
        }
    }
}
