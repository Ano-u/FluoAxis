namespace FluoAxis.Domain;

/// <summary>
/// Immutable kinetic series of one well and one channel.
/// Points must already be strictly ascending by elapsed time with finite
/// RFU values (the import layer guarantee); this factory validates that
/// contract fail-closed and never silently repairs unordered input.
/// </summary>
public sealed record SignalSeries
{
    public Guid RunId { get; }
    public WellId Well { get; }
    public FluorescenceChannel Channel { get; }
    public IReadOnlyList<SignalPoint> Points { get; }

    private SignalSeries(Guid runId, WellId well, FluorescenceChannel channel, IReadOnlyList<SignalPoint> points)
    {
        RunId = runId;
        Well = well;
        Channel = channel;
        Points = points;
    }

    /// <summary>
    /// Validates and stores a defensive copy of <paramref name="points"/>.
    /// The input must be strictly ascending by <see cref="SignalPoint.Elapsed"/>;
    /// duplicate timestamps, out-of-order points, and non-finite RFU values
    /// all throw <see cref="ArgumentException"/>. An empty point set is
    /// allowed; detecting an empty well is the analysis engine's structured
    /// error, not a domain construction failure.
    /// </summary>
    public static SignalSeries Create(Guid runId, WellId well, FluorescenceChannel channel, IEnumerable<SignalPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        SignalPoint[] copy = points.ToArray();

        for (int i = 0; i < copy.Length; i++)
        {
            if (!double.IsFinite(copy[i].Rfu))
            {
                throw new ArgumentException($"Point {i} has a non-finite RFU value; NaN and Infinity are rejected.", nameof(points));
            }

            if (i > 0 && copy[i].Elapsed <= copy[i - 1].Elapsed)
            {
                throw new ArgumentException(
                    copy[i].Elapsed == copy[i - 1].Elapsed
                        ? $"Points {i - 1} and {i} share the elapsed time {copy[i].Elapsed}; duplicate timestamps are rejected."
                        : $"Point {i} is not after point {i - 1} by elapsed time; points must be strictly ascending.",
                    nameof(points));
            }
        }

        return new SignalSeries(runId, well, channel, copy);
    }
}
