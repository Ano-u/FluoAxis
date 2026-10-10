namespace FluoAxis.Contracts;

using System.Buffers.Binary;

/// <summary>One frozen research display image, separate from formal results and bound to the selected Run revision.</summary>
public sealed record ReportPlotFigureDto
{
    public Guid RunId { get; }
    public int RunRevision { get; }
    public Guid PlotId { get; }
    public string Caption { get; }
    public string PngBase64 { get; }

    public ReportPlotFigureDto(Guid runId, int runRevision, Guid plotId, string caption, string pngBase64)
    {
        ContractGuard.RequireNonEmptyGuid(runId, nameof(runId)); ContractGuard.RequireNonEmptyGuid(plotId, nameof(plotId));
        ContractGuard.RequireNonNegative(runRevision, nameof(runRevision)); ContractGuard.RequireNonBlank(caption, nameof(caption));
        ArgumentNullException.ThrowIfNull(pngBase64);
        if (caption.Length > 512 || caption.Any(char.IsControl) || pngBase64.Length > 1_398_104)
            throw new ArgumentException("REPORT_FIGURE_LIMIT_EXCEEDED");
        byte[] png;
        try { png = Convert.FromBase64String(pngBase64); }
        catch (FormatException) { throw new ArgumentException("REPORT_FIGURE_PNG_INVALID", nameof(pngBase64)); }
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (png.Length is < 33 or > 1024 * 1024 || !png.AsSpan(0, 8).SequenceEqual(signature)
            || !png.AsSpan(12, 4).SequenceEqual("IHDR"u8)) throw new ArgumentException("REPORT_FIGURE_PNG_INVALID");
        uint width = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4)), height = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4));
        if (width is 0 or > 8192 || height is 0 or > 8192 || (long)width * height > 16_000_000)
            throw new ArgumentException("REPORT_FIGURE_PIXEL_LIMIT_EXCEEDED");
        RunId = runId; RunRevision = runRevision; PlotId = plotId; Caption = caption; PngBase64 = Convert.ToBase64String(png);
    }
}
