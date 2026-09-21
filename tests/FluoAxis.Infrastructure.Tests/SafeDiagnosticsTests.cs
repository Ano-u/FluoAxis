namespace FluoAxis.Infrastructure.Tests;

using FluoAxis.Domain;
using Xunit;

public sealed class SafeDiagnosticsTests
{
    [Fact]
    public void Redactor_removes_machine_local_path_forms_and_preserves_safe_references()
    {
        string separator = new((char)92, 1);
        string input = string.Join(" | ",
            string.Concat("C", ":", separator, "private", separator, "source.xlsx"),
            string.Concat(separator, separator, "host", separator, "share", separator, "source.xlsx"),
            string.Concat("file", ":///private/source.xlsx"),
            string.Concat("/", "home/research/source.xlsx"),
            "https://example.invalid/reference",
            "data/raw/source.xlsx",
            "source.xlsx");

        string result = SafeDiagnostics.RedactMachinePaths(input);

        Assert.Equal(4, result.Split("[REDACTED_PATH]", StringSplitOptions.None).Length - 1);
        Assert.Contains("https://example.invalid/reference", result, StringComparison.Ordinal);
        Assert.Contains("data/raw/source.xlsx", result, StringComparison.Ordinal);
        Assert.Contains("source.xlsx", result, StringComparison.Ordinal);
        Assert.DoesNotContain("private", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("research", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("operation completed", "operation completed")]
    public void Redactor_handles_empty_and_safe_text(string? input, string expected)
    {
        Assert.Equal(expected, SafeDiagnostics.RedactMachinePaths(input));
    }

    [Fact]
    public void Redactor_removes_complete_quoted_machine_paths_that_contain_spaces()
    {
        string separator = new((char)92, 1);
        string drivePath = string.Concat("C", ":", separator, "private folder", separator, "source file.xlsx");
        string uncPath = string.Concat(separator, separator, "host", separator, "shared folder", separator, "source file.xlsx");
        string fileUri = string.Concat("file", ":///private folder/source file.xlsx");
        string posixPath = string.Concat("/", "home/research folder/source file.xlsx");
        string drivePathWithApostrophe = string.Concat("D", ":", separator, "researcher's folder", separator, "source.xlsx");
        string posixPathWithQuote = string.Concat("/", "tmp/research \"draft\"/source.xlsx");
        string input = string.Join(" | ",
            string.Concat((char)34, drivePath, (char)34),
            string.Concat((char)39, uncPath, (char)39),
            string.Concat((char)34, fileUri, (char)34),
            string.Concat((char)39, posixPath, (char)39),
            string.Concat((char)34, drivePathWithApostrophe, (char)34),
            string.Concat((char)39, posixPathWithQuote, (char)39));

        string result = SafeDiagnostics.RedactMachinePaths(input);

        Assert.Equal(string.Join(" | ", Enumerable.Repeat("[REDACTED_PATH]", 6)), result);
        Assert.DoesNotContain("private folder", result, StringComparison.Ordinal);
        Assert.DoesNotContain("shared folder", result, StringComparison.Ordinal);
        Assert.DoesNotContain("research folder", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Structured_event_accepts_only_allowlisted_bounded_fields()
    {
        Guid correlationId = Guid.NewGuid();
        Guid runId = Guid.NewGuid();
        var value = new SafeDiagnosticEvent(
            DateTimeOffset.UnixEpoch,
            "IMPORT_INSPECTION_FAILED",
            "import.inspect",
            correlationId,
            runId,
            "managed-source-17",
            RelativePath.Parse("data/raw/source.xlsx"),
            "source.xlsx",
            SafeFailureCategory.InvalidData);

        Assert.Equal(correlationId, value.CorrelationId);
        Assert.Equal(runId, value.RunId);
        Assert.Equal("data/raw/source.xlsx", value.ProjectRelativePath!.Value);
        Assert.Equal(SafeFailureCategory.InvalidData, value.FailureCategory);
    }

    [Fact]
    public void Structured_event_rejects_free_form_or_path_carrying_fields()
    {
        Guid correlationId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new SafeDiagnosticEvent(
            DateTimeOffset.Now, "EVENT", "import.inspect", correlationId));
        Assert.Throws<ArgumentException>(() => new SafeDiagnosticEvent(
            DateTimeOffset.UnixEpoch, "event with spaces", "import.inspect", correlationId));
        Assert.Throws<ArgumentException>(() => new SafeDiagnosticEvent(
            DateTimeOffset.UnixEpoch, "EVENT", "Import Inspect", correlationId));
        Assert.Throws<ArgumentException>(() => new SafeDiagnosticEvent(
            DateTimeOffset.UnixEpoch, "EVENT", "import.inspect", Guid.Empty));
        Assert.Throws<ArgumentException>(() => new SafeDiagnosticEvent(
            DateTimeOffset.UnixEpoch, "EVENT", "import.inspect", correlationId,
            logicalResourceId: "data/raw/source.xlsx"));
        Assert.Throws<ArgumentException>(() => new SafeDiagnosticEvent(
            DateTimeOffset.UnixEpoch, "EVENT", "import.inspect", correlationId,
            logicalResourceId: "C:source.xlsx"));
        Assert.Throws<ArgumentException>(() => new SafeDiagnosticEvent(
            DateTimeOffset.UnixEpoch, "EVENT", "import.inspect", correlationId,
            logicalResourceId: "source name"));
        Assert.Throws<ArgumentException>(() => new SafeDiagnosticEvent(
            DateTimeOffset.UnixEpoch, "EVENT", "import.inspect", correlationId,
            safeDisplayName: "raw/source.xlsx"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SafeDiagnosticEvent(
            DateTimeOffset.UnixEpoch, "EVENT", "import.inspect", correlationId,
            failureCategory: (SafeFailureCategory)999));
    }

    [Theory]
    [MemberData(nameof(ExceptionCategories))]
    public void Exception_classification_retains_only_a_bounded_category(Exception exception, SafeFailureCategory expected)
    {
        Assert.Equal(expected, SafeDiagnostics.ClassifyException(exception));
    }

    public static TheoryData<Exception, SafeFailureCategory> ExceptionCategories => new()
    {
        { new OperationCanceledException("secret"), SafeFailureCategory.Cancelled },
        { new UnauthorizedAccessException("secret"), SafeFailureCategory.AccessDenied },
        { new InvalidDataException("secret"), SafeFailureCategory.InvalidData },
        { new TimeoutException("secret"), SafeFailureCategory.Timeout },
        { new IOException("secret"), SafeFailureCategory.IoFailure },
        { new ArgumentException("secret"), SafeFailureCategory.InvalidOperation },
        { new NotSupportedException("secret"), SafeFailureCategory.Unknown },
    };
}
