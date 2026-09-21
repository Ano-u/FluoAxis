namespace FluoAxis.Contracts;

using System.Collections.Frozen;
using System.Text.RegularExpressions;
using FluoAxis.Domain;

/// <summary>
/// Shared construction-time validation for contract records. The wire
/// contract is fail-closed: DTOs that would carry malformed identifiers,
/// machine paths, or out-of-vocabulary strings are rejected at construction
/// rather than deep in the backend.
/// </summary>
internal static partial class ContractGuard
{
    /// <summary>plan.md §5.4 error-code namespaces; mirrors Domain's AppError pattern.</summary>
    public const string ErrorCodePattern =
        "^(IMPORT|MAPPING|PLATE|METHOD|CALIBRATION|CONTROL|SAMPLE|PROJECT|REPORT|PLOT|SYSTEM)_[A-Z0-9_]+$";

    public static void RequireNonEmptyGuid(Guid value, string paramName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{paramName} must not be an empty GUID.", paramName);
        }
    }

    public static void RequireNonBlank(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{paramName} must not be null or blank.", paramName);
        }
    }

    public static void RequireNonNegative(int value, string paramName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, $"{paramName} must be non-negative.");
        }
    }

    public static void RequireUtc(DateTimeOffset value, string paramName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException($"{paramName} must be a UTC instant (offset zero).", paramName);
        }
    }

    public static void RequirePositive(long value, string paramName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, $"{paramName} must be positive.");
        }
    }

    public static void RequireErrorCode(string code)
    {
        RequireNonBlank(code, nameof(code));

        if (!ErrorCodeRegex().IsMatch(code))
        {
            throw new ArgumentException(
                $"Error code '{code}' does not match the required pattern '{ErrorCodePattern}'.",
                nameof(code));
        }
    }

    public static void RequireInVocabulary(string value, FrozenSet<string> vocabulary, string paramName, string vocabularyName)
    {
        if (!vocabulary.Contains(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a member of the {vocabularyName} contract vocabulary.",
                paramName);
        }
    }

    public static void RequireWellId(string? wellId)
    {
        if (wellId is null)
        {
            return;
        }

        WellId.Parse(wellId); // throws FormatException for anything outside A1..H12
    }

    public static void RequireChannel(string? channel)
    {
        if (channel is null)
        {
            return;
        }

        if (channel != ChannelNames.Fam && channel != ChannelNames.Rox)
        {
            throw new ArgumentException(
                $"'{channel}' is not a valid channel; expected 'fam' or 'rox'.",
                nameof(channel));
        }
    }

    public static void RequireSha256(string sha256, string paramName)
    {
        RequireNonBlank(sha256, paramName);

        if (sha256.Length != HexHash.HexLength || !sha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                $"{paramName} must be exactly {HexHash.HexLength} hexadecimal characters.",
                paramName);
        }
    }

    [GeneratedRegex(ErrorCodePattern)]
    private static partial Regex ErrorCodeRegex();
}
