namespace FluoAxis.PlotProtocol;

/// <summary>Coordinate dimensionality of one immutable plot-data snapshot.</summary>
public enum PlotDimensions
{
    Two = 2,
    Three = 3,
}

/// <summary>One exact renderer-independent point. Display formatting is deliberately absent.</summary>
public sealed record PlotDataPoint
{
    public string PointId { get; }
    public double X { get; }
    public double Y { get; }
    public double? Z { get; }
    public string SeriesId { get; }
    public string? Label { get; }

    public PlotDataPoint(string pointId, double x, double y, double? z, string seriesId, string? label = null)
    {
        Guard.Identifier(pointId, nameof(pointId));
        Guard.Identifier(seriesId, nameof(seriesId));
        Guard.Finite(x, nameof(x));
        Guard.Finite(y, nameof(y));
        Guard.Finite(z, nameof(z));
        if (label is not null)
        {
            Guard.BoundedText(label, 512, nameof(label));
        }

        PointId = pointId;
        X = x;
        Y = y;
        Z = z;
        SeriesId = seriesId;
        Label = label;
    }
}

/// <summary>Bounded immutable exact coordinates indexed by stable PointId.</summary>
public sealed record PlotDataSnapshot
{
    public const int MaximumPointLimit = 1_000_000;

    private readonly Dictionary<string, PlotDataPoint> pointsById;

    public int SchemaVersion { get; }
    public Guid PlotId { get; }
    public PlotDimensions Dimensions { get; }
    public int PointLimit { get; }
    public IReadOnlyList<PlotDataPoint> Points { get; }

    public PlotDataSnapshot(Guid plotId, PlotDimensions dimensions, int pointLimit,
        IReadOnlyList<PlotDataPoint> points, int schemaVersion = PlotProtocolVersions.Current)
    {
        PlotProtocolVersions.RequireCurrent(schemaVersion, nameof(schemaVersion));
        Guard.NonEmpty(plotId, nameof(plotId));
        Guard.Defined(dimensions, nameof(dimensions));
        if (pointLimit <= 0 || pointLimit > MaximumPointLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(pointLimit), pointLimit,
                $"Point limit must be in [1, {MaximumPointLimit}].");
        }

        ArgumentNullException.ThrowIfNull(points);
        PlotDataPoint[] snapshot = [.. points];
        if (snapshot.Length > pointLimit)
        {
            throw new ArgumentException("Point count exceeds the explicit snapshot limit.", nameof(points));
        }

        var index = new Dictionary<string, PlotDataPoint>(snapshot.Length, StringComparer.Ordinal);
        bool requiresZ = dimensions == PlotDimensions.Three;
        foreach (PlotDataPoint point in snapshot)
        {
            if (point is null)
            {
                throw new ArgumentException("Points must not contain null entries.", nameof(points));
            }

            if (requiresZ != point.Z.HasValue)
            {
                throw new ArgumentException("Every point must match the snapshot dimensionality.", nameof(points));
            }

            if (!index.TryAdd(point.PointId, point))
            {
                throw new ArgumentException("Point identifiers must be unique within a snapshot.", nameof(points));
            }
        }

        SchemaVersion = schemaVersion;
        PlotId = plotId;
        Dimensions = dimensions;
        PointLimit = pointLimit;
        Points = Array.AsReadOnly(snapshot);
        pointsById = index;
    }

    public bool TryGetPoint(string pointId, out PlotDataPoint? point)
    {
        Guard.Identifier(pointId, nameof(pointId));
        return pointsById.TryGetValue(pointId, out point);
    }

    public PlotDistanceMeasurement Measure(string firstPointId, string secondPointId)
    {
        if (!TryGetPoint(firstPointId, out PlotDataPoint? first))
        {
            throw new KeyNotFoundException("The first PointId is not present in the snapshot.");
        }

        if (!TryGetPoint(secondPointId, out PlotDataPoint? second))
        {
            throw new KeyNotFoundException("The second PointId is not present in the snapshot.");
        }

        double deltaX = second!.X - first!.X;
        double deltaY = second.Y - first.Y;
        double? deltaZ = Dimensions == PlotDimensions.Three ? second.Z!.Value - first.Z!.Value : null;
        double distance = deltaZ is null
            ? StableMagnitude(deltaX, deltaY)
            : StableMagnitude(deltaX, deltaY, deltaZ.Value);

        return new PlotDistanceMeasurement(first.PointId, second.PointId, Dimensions,
            deltaX, deltaY, deltaZ, distance);
    }

    private static double StableMagnitude(double x, double y, double z = 0)
    {
        double scale = Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z)));
        if (scale == 0)
        {
            return 0;
        }

        double scaledX = x / scale;
        double scaledY = y / scale;
        double scaledZ = z / scale;
        return scale * Math.Sqrt((scaledX * scaledX) + (scaledY * scaledY) + (scaledZ * scaledZ));
    }
}

/// <summary>Full-precision C# distance result; presentation owns rounding and labels.</summary>
public sealed record PlotDistanceMeasurement
{
    public string FirstPointId { get; }
    public string SecondPointId { get; }
    public PlotDimensions Dimensions { get; }
    public double DeltaX { get; }
    public double DeltaY { get; }
    public double? DeltaZ { get; }
    public double Distance { get; }

    public PlotDistanceMeasurement(string firstPointId, string secondPointId, PlotDimensions dimensions,
        double deltaX, double deltaY, double? deltaZ, double distance)
    {
        Guard.Identifier(firstPointId, nameof(firstPointId));
        Guard.Identifier(secondPointId, nameof(secondPointId));
        Guard.Defined(dimensions, nameof(dimensions));
        Guard.Finite(deltaX, nameof(deltaX));
        Guard.Finite(deltaY, nameof(deltaY));
        Guard.Finite(deltaZ, nameof(deltaZ));
        Guard.Finite(distance, nameof(distance));
        if (distance < 0 || (dimensions == PlotDimensions.Three) != deltaZ.HasValue)
        {
            throw new ArgumentException("Distance components do not match the declared dimensionality.", nameof(deltaZ));
        }

        FirstPointId = firstPointId;
        SecondPointId = secondPointId;
        Dimensions = dimensions;
        DeltaX = deltaX;
        DeltaY = deltaY;
        DeltaZ = deltaZ;
        Distance = distance;
    }
}
