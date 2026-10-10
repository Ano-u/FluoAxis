namespace FluoAxis.Reporting;

using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

/// <summary>Fail-closed bounds and path-free text boundary shared by every report format.</summary>
internal static partial class ReportSafety
{
    internal const int MaxTextLength = 16_384;
    internal const int MaxAggregateTextBytes = 1024 * 1024;
    internal const int MaxCanonicalBytes = 4 * 1024 * 1024;
    internal const int MaxOutputBytes = 16 * 1024 * 1024;

    internal static void ValidateTextBudget(IEnumerable<string?> values)
    {
        int remaining = MaxAggregateTextBytes;
        foreach (string? value in values)
        {
            if (value is null) continue;
            if (value.Length > MaxTextLength) throw new ArgumentException("REPORT_TEXT_LIMIT_EXCEEDED");
            int bytes = Encoding.UTF8.GetByteCount(value);
            if (bytes > remaining) throw new ArgumentException("REPORT_TEXT_BUDGET_EXCEEDED");
            remaining -= bytes;
        }
    }

    internal static void ValidateTextTree(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    // The typed ReportPlotFigureDto validates and bounds this
                    // encoded binary field; it is not a user text field.
                    if (property.Name == "pngBase64") continue;
                    ValidateTextTree(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray()) ValidateTextTree(item);
                break;
            case JsonValueKind.String:
                string value = element.GetString()!;
                if (value.Length > MaxTextLength) throw new ArgumentException("REPORT_TEXT_LIMIT_EXCEEDED");
                try { _ = XmlConvert.VerifyXmlChars(value); }
                catch (XmlException) { throw new ArgumentException("REPORT_TEXT_INVALID"); }
                // HTTPS is an allowed URL, never converted into a live report link.
                string withoutHttps = HttpsUrl().Replace(value, string.Empty);
                if (MachinePath().IsMatch(withoutHttps)) throw new ArgumentException("REPORT_MACHINE_PATH_REJECTED");
                break;
        }
    }

    [GeneratedRegex(@"https://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HttpsUrl();

    [GeneratedRegex(@"(?:[A-Za-z]:[\\/]|\\\\|file:/|(?<![A-Za-z0-9])/(?:[A-Za-z0-9._-]+/)+[A-Za-z0-9._-]+|(?<![A-Za-z0-9])/(?:home|Users|root|tmp|etc|var|opt|srv|usr|mnt|media|run)(?:\b|/))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MachinePath();
}
