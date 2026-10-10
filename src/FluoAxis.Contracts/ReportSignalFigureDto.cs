namespace FluoAxis.Contracts;

public sealed record ReportSignalPointDto(long ElapsedTicks, double RawRfu);

/// <summary>A bounded, explicitly partial Raw RFU view frozen into a report; never an Analysis input.</summary>
public sealed record ReportSignalFigureDto
{
    public string Well { get; }
    public string Channel { get; }
    public int TotalPoints { get; }
    public IReadOnlyList<ReportSignalPointDto> Points { get; }

    public ReportSignalFigureDto(string well, string channel, int totalPoints, IReadOnlyList<ReportSignalPointDto> points)
    {
        ContractGuard.RequireNonBlank(well, nameof(well)); ContractGuard.RequireNonBlank(channel, nameof(channel));
        ContractGuard.RequireWellId(well); ContractGuard.RequireChannel(channel);
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count is < 1 or > 500 || totalPoints < points.Count || points.Any(point => point is null || !double.IsFinite(point.RawRfu))
            || points.Zip(points.Skip(1)).Any(pair => pair.First.ElapsedTicks >= pair.Second.ElapsedTicks))
            throw new ArgumentException("Invalid bounded report signal view.", nameof(points));
        Well = well; Channel = channel; TotalPoints = totalPoints; Points = Array.AsReadOnly(points.ToArray());
    }
}
