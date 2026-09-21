namespace FluoAxis.Infrastructure;

using System.Text.RegularExpressions;
using FluoAxis.Domain;

/// <summary>Bounded categories that may cross the diagnostic boundary.</summary>
public enum SafeFailureCategory
{
    None,
    Cancelled,
    AccessDenied,
    InvalidData,
    InvalidOperation,
    IoFailure,
    Timeout,
    Unknown,
}

/// <summary>
/// Allowlisted diagnostic event. It intentionally has no free-form message,
/// exception, stack trace, raw-data, or arbitrary property-bag field.
/// </summary>
public sealed record SafeDiagnosticEvent
{
    private static readonly Regex EventCodePattern = new(
        "^[A-Z][A-Z0-9_]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly Regex OperationPattern = new(
        "^[a-z][a-z0-9]*(?:[.][a-z0-9]+)*$", RegexOptions.CultureInvariant);
    private static readonly Regex LogicalResourceIdPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant);

    public DateTimeOffset OccurredUtc { get; }
    public string EventCode { get; }
    public string Operation { get; }
    public Guid CorrelationId { get; }
    public Guid? RunId { get; }
    public string? LogicalResourceId { get; }
    public RelativePath? ProjectRelativePath { get; }
    public string? SafeDisplayName { get; }
    public SafeFailureCategory FailureCategory { get; }

    public SafeDiagnosticEvent(
        DateTimeOffset occurredUtc,
        string eventCode,
        string operation,
        Guid correlationId,
        Guid? runId = null,
        string? logicalResourceId = null,
        RelativePath? projectRelativePath = null,
        string? safeDisplayName = null,
        SafeFailureCategory failureCategory = SafeFailureCategory.None)
    {
        if (occurredUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Diagnostic timestamps must use UTC.", nameof(occurredUtc));
        }

        if (eventCode is null || !EventCodePattern.IsMatch(eventCode))
        {
            throw new ArgumentException("EventCode must be a bounded SCREAMING_SNAKE_CASE token.", nameof(eventCode));
        }

        if (operation is null || operation.Length > 96 || !OperationPattern.IsMatch(operation))
        {
            throw new ArgumentException("Operation must be a bounded dotted lowercase token.", nameof(operation));
        }

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("CorrelationId must not be empty.", nameof(correlationId));
        }

        ValidateLogicalResourceId(logicalResourceId);
        ValidateDisplayName(safeDisplayName);
        if (!Enum.IsDefined(failureCategory))
        {
            throw new ArgumentOutOfRangeException(nameof(failureCategory));
        }

        OccurredUtc = occurredUtc;
        EventCode = eventCode;
        Operation = operation;
        CorrelationId = correlationId;
        RunId = runId;
        LogicalResourceId = logicalResourceId;
        ProjectRelativePath = projectRelativePath;
        SafeDisplayName = safeDisplayName;
        FailureCategory = failureCategory;
    }

    private static void ValidateLogicalResourceId(string? value)
    {
        if (value is null)
        {
            return;
        }

        if (!LogicalResourceIdPattern.IsMatch(value))
        {
            throw new ArgumentException(
                "LogicalResourceId must be a bounded identifier token.",
                nameof(value));
        }
    }

    private static void ValidateDisplayName(string? safeDisplayName)
    {
        if (safeDisplayName is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(safeDisplayName) || safeDisplayName.Length > 255
            || safeDisplayName.IndexOfAny(['/', '\\']) >= 0
            || safeDisplayName.Any(char.IsControl))
        {
            throw new ArgumentException(
                "SafeDisplayName must be a bounded bare name.",
                nameof(safeDisplayName));
        }
    }
}

/// <summary>Central defense-in-depth normalization for diagnostics.</summary>
public static partial class SafeDiagnostics
{
    private const string RedactionMarker = "[REDACTED_PATH]";

    /// <summary>
    /// Removes recognizable machine-local paths from unavoidable third-party
    /// text. This does not make arbitrary text safe to log; structured
    /// <see cref="SafeDiagnosticEvent"/> fields remain the primary API.
    /// </summary>
    public static string RedactMachinePaths(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        string redacted = QuotedMachinePathPattern().Replace(text, RedactionMarker);
        redacted = FileUriPattern().Replace(redacted, RedactionMarker);
        redacted = UncPathPattern().Replace(redacted, RedactionMarker);
        redacted = DrivePathPattern().Replace(redacted, RedactionMarker);
        return PosixMachinePathPattern().Replace(redacted, RedactionMarker);
    }

    /// <summary>Maps an exception to a bounded category without retaining it.</summary>
    public static SafeFailureCategory ClassifyException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            OperationCanceledException => SafeFailureCategory.Cancelled,
            UnauthorizedAccessException or System.Security.SecurityException => SafeFailureCategory.AccessDenied,
            InvalidDataException => SafeFailureCategory.InvalidData,
            TimeoutException => SafeFailureCategory.Timeout,
            IOException => SafeFailureCategory.IoFailure,
            InvalidOperationException or ArgumentException => SafeFailureCategory.InvalidOperation,
            _ => SafeFailureCategory.Unknown,
        };
    }

    [GeneratedRegex("(?i)(?:\"(?:file:(?:/{2,}|\\\\{2,})|[A-Z]:[\\\\/]|\\\\{2}|/(?:home|Users|root|tmp|var/tmp)(?:/|(?=\")))[^\"\\r\\n]*\"|'(?:file:(?:/{2,}|\\\\{2,})|[A-Z]:[\\\\/]|\\\\{2}|/(?:home|Users|root|tmp|var/tmp)(?:/|(?=')))[^'\\r\\n]*')", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedMachinePathPattern();

    [GeneratedRegex("(?i)(?<![A-Za-z0-9])file:(?:/{2,}|\\\\{2,})[^\\s\\\"'<>|]+", RegexOptions.CultureInvariant)]
    private static partial Regex FileUriPattern();

    [GeneratedRegex("(?<!\\\\)\\\\\\\\[^\\s\\\"'<>|]+(?:\\\\[^\\s\\\"'<>|]+)+", RegexOptions.CultureInvariant)]
    private static partial Regex UncPathPattern();

    [GeneratedRegex("(?i)(?<![A-Za-z0-9])[A-Z]:[\\\\/][^\\s\\\"'<>|]+", RegexOptions.CultureInvariant)]
    private static partial Regex DrivePathPattern();

    [GeneratedRegex("(?<![A-Za-z0-9])/(?:home|Users|root|tmp|var/tmp)(?:/[^\\s\\\"'<>|]*)?", RegexOptions.CultureInvariant)]
    private static partial Regex PosixMachinePathPattern();
}
