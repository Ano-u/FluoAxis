namespace FluoAxis.Import;

using System.Globalization;
using System.Text;

/// <summary>Explicit adapter parsing choices. A null instance preserves the previous import contract.</summary>
public sealed record ImportReadOptions
{
    public string EncodingName { get; }
    public char Delimiter { get; }
    public CultureInfo DecimalCulture { get; }
    public double MinutesPerUnit { get; }

    public ImportReadOptions(string encodingName = "utf-8", string delimiter = "comma", string decimalCulture = "invariant", string timeUnit = "minutes")
    {
        if (encodingName is not ("utf-8" or "utf-16" or "utf-16BE" or "gb18030" or "windows-1252")) throw new ArgumentException("Unsupported encoding.", nameof(encodingName));
        EncodingName = encodingName;
        Delimiter = delimiter switch { "comma" => ',', "tab" => '\t', "semicolon" => ';', _ => throw new ArgumentException("Unsupported delimiter.", nameof(delimiter)) };
        DecimalCulture = decimalCulture switch
        {
            "invariant" => CultureInfo.InvariantCulture,
            "zh-CN" or "de-DE" or "fr-FR" => CultureInfo.GetCultureInfo(decimalCulture),
            _ => throw new ArgumentException("Unsupported decimal culture.", nameof(decimalCulture)),
        };
        MinutesPerUnit = timeUnit switch { "minutes" => 1, "seconds" => 1d / 60, "hours" => 60, "days" => 1440, _ => throw new ArgumentException("Unsupported time unit.", nameof(timeUnit)) };
    }

    public Encoding GetEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(EncodingName, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    internal bool TryParseFinite(string text, out double number) =>
        double.TryParse(text, NumberStyles.Float, DecimalCulture, out number) && double.IsFinite(number);
}
