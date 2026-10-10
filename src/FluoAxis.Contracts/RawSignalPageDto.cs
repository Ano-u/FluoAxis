namespace FluoAxis.Contracts;

using FluoAxis.Domain;

/// <summary>One immutable stored point. PointIndex identifies the original sequence, independently of paging.</summary>
public sealed record RawSignalPointDto
{
    public int PointIndex { get; }
    public long ElapsedTicks { get; }
    public double Rfu { get; }

    public RawSignalPointDto(int pointIndex, long elapsedTicks, double rfu)
    {
        ContractGuard.RequireNonNegative(pointIndex, nameof(pointIndex));
        if (!double.IsFinite(rfu)) throw new ArgumentOutOfRangeException(nameof(rfu), "Raw RFU must be finite.");
        PointIndex = pointIndex;
        ElapsedTicks = elapsedTicks;
        Rfu = rfu;
    }
}

/// <summary>A revision-consistent immutable page; no smoothing, normalization, or display rounding is applied.</summary>
public sealed record RawSignalPageDto
{
    public const int MaximumPageSize = 25_000;

    public Guid RunId { get; }
    public int Revision { get; }
    public string Well { get; }
    public string Channel { get; }
    public int TotalPoints { get; }
    public IReadOnlyList<RawSignalPointDto> Points { get; }

    public RawSignalPageDto(Guid runId, int revision, string well, string channel,
        int totalPoints, IReadOnlyList<RawSignalPointDto> points)
    {
        ContractGuard.RequireNonEmptyGuid(runId, nameof(runId));
        ContractGuard.RequireNonNegative(revision, nameof(revision));
        ContractGuard.RequireNonNegative(totalPoints, nameof(totalPoints));
        ContractGuard.RequireNonBlank(channel, nameof(channel));
        ContractGuard.RequireChannel(channel);
        ArgumentNullException.ThrowIfNull(points);
        RawSignalPointDto[] copy = [.. points];
        if (copy.Length > MaximumPageSize || copy.Length > totalPoints || copy.Any(point => point is null))
            throw new ArgumentException("The raw signal page exceeds its declared bounds.", nameof(points));
        for (int index = 1; index < copy.Length; index++)
            if (copy[index].PointIndex <= copy[index - 1].PointIndex || copy[index].ElapsedTicks <= copy[index - 1].ElapsedTicks)
                throw new ArgumentException("Raw points must retain strictly increasing sequence and elapsed time.", nameof(points));
        RunId = runId;
        Revision = revision;
        Well = WellId.Parse(well).ToString();
        Channel = channel;
        TotalPoints = totalPoints;
        Points = Array.AsReadOnly(copy);
    }
}
