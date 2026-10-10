namespace FluoAxis.Contracts;

/// <summary>Explicit parsing choices frozen with a mapping. Null options on older mappings preserve legacy behavior.</summary>
public sealed record ImportOptionsDto
{
    public string EncodingName { get; }
    public string Delimiter { get; }
    public string DecimalCulture { get; }
    public string TimeUnit { get; }

    public ImportOptionsDto(string encodingName = "utf-8", string delimiter = "comma",
        string decimalCulture = "invariant", string timeUnit = "minutes")
    {
        if (encodingName is not ("utf-8" or "utf-16" or "utf-16BE" or "gb18030" or "windows-1252"))
            throw new ArgumentException("Unsupported explicit text encoding.", nameof(encodingName));
        if (delimiter is not ("comma" or "tab" or "semicolon"))
            throw new ArgumentException("Unsupported explicit delimiter.", nameof(delimiter));
        if (decimalCulture is not ("invariant" or "zh-CN" or "de-DE" or "fr-FR"))
            throw new ArgumentException("Unsupported explicit decimal culture.", nameof(decimalCulture));
        if (timeUnit is not ("minutes" or "seconds" or "hours" or "days"))
            throw new ArgumentException("Unsupported explicit time unit.", nameof(timeUnit));
        EncodingName = encodingName; Delimiter = delimiter; DecimalCulture = decimalCulture; TimeUnit = timeUnit;
    }
}
