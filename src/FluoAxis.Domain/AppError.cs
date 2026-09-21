namespace FluoAxis.Domain;

using System.Text.RegularExpressions;

/// <summary>
/// Structured domain error. Codes are a compatibility contract
/// (plan.md §5.4): the code namespace may not be renamed without
/// migration, so construction validates the shape up front.
/// </summary>
public sealed partial record AppError
{
    public const string CodePattern = "^(IMPORT|MAPPING|PLATE|METHOD|CALIBRATION|CONTROL|SAMPLE|PROJECT|REPORT|PLOT|SYSTEM)_[A-Z0-9_]+$";

    public string Code { get; }
    public ErrorSeverity Severity { get; }
    public string UserMessage { get; }
    public string? SafeDetailCode { get; }
    public Guid? RunId { get; }

    public AppError(string code, ErrorSeverity severity, string userMessage, string? safeDetailCode = null, Guid? runId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);

        if (!CodeRegex().IsMatch(code))
        {
            throw new ArgumentException($"Error code '{code}' does not match the required pattern '{CodePattern}'.", nameof(code));
        }

        Code = code;
        Severity = severity;
        UserMessage = userMessage;
        SafeDetailCode = safeDetailCode;
        RunId = runId;
    }

    [GeneratedRegex(CodePattern)]
    private static partial Regex CodeRegex();
}
