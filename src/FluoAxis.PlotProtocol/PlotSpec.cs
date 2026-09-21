namespace FluoAxis.PlotProtocol;

/// <summary>Renderer-neutral graph kinds enabled by the V1 protocol.</summary>
public enum PlotKind
{
    Scatter2D,
    Line2D,
    ScatterLine2D,
    Scatter3D,
    Line3D,
}

public enum AxisScale
{
    Linear,
    Log,
}

public enum CameraProjection
{
    Perspective,
    Orthographic,
}

/// <summary>One plot axis. Null bounds request renderer-managed range.</summary>
public sealed record AxisSpec
{
    public string Label { get; }
    public AxisScale Scale { get; }
    public double? Minimum { get; }
    public double? Maximum { get; }

    public AxisSpec(string label, AxisScale scale = AxisScale.Linear, double? minimum = null, double? maximum = null)
    {
        Guard.NonBlank(label, nameof(label));
        Guard.Defined(scale, nameof(scale));
        Guard.Finite(minimum, nameof(minimum));
        Guard.Finite(maximum, nameof(maximum));
        if (minimum is not null && maximum is not null && minimum >= maximum)
        {
            throw new ArgumentException("Axis minimum must be less than maximum.", nameof(minimum));
        }

        if (scale == AxisScale.Log && (minimum is <= 0 || maximum is <= 0))
        {
            throw new ArgumentException("Explicit logarithmic axis bounds must be positive.", nameof(minimum));
        }

        Label = label;
        Scale = scale;
        Minimum = minimum;
        Maximum = maximum;
    }
}

/// <summary>Renderer-neutral series styling; point data crosses the runtime bridge separately.</summary>
public sealed record SeriesSpec
{
    public string SeriesId { get; }
    public string Label { get; }
    public bool Visible { get; }
    public double MarkerSize { get; }

    public SeriesSpec(string seriesId, string label, bool visible = true, double markerSize = 6)
    {
        Guard.Identifier(seriesId, nameof(seriesId));
        Guard.NonBlank(label, nameof(label));
        if (!double.IsFinite(markerSize) || markerSize <= 0 || markerSize > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(markerSize), markerSize, "Marker size must be finite and in (0, 100].");
        }

        SeriesId = seriesId;
        Label = label;
        Visible = visible;
        MarkerSize = markerSize;
    }
}

/// <summary>Three-dimensional camera position and projection.</summary>
public sealed record CameraSpec
{
    public double EyeX { get; }
    public double EyeY { get; }
    public double EyeZ { get; }
    public CameraProjection Projection { get; }

    public CameraSpec(double eyeX, double eyeY, double eyeZ, CameraProjection projection = CameraProjection.Perspective)
    {
        Guard.Finite(eyeX, nameof(eyeX));
        Guard.Finite(eyeY, nameof(eyeY));
        Guard.Finite(eyeZ, nameof(eyeZ));
        Guard.Defined(projection, nameof(projection));
        if (eyeX == 0 && eyeY == 0 && eyeZ == 0)
        {
            throw new ArgumentException("Camera eye must not coincide with the origin.", nameof(eyeX));
        }

        EyeX = eyeX;
        EyeY = eyeY;
        EyeZ = eyeZ;
        Projection = projection;
    }
}

/// <summary>Immutable renderer-neutral specification shared by 2D and 3D graphs.</summary>
public sealed record PlotSpec
{
    public int SchemaVersion { get; }
    public Guid PlotId { get; }
    public PlotKind Kind { get; }
    public AxisSpec X { get; }
    public AxisSpec Y { get; }
    public AxisSpec? Z { get; }
    public IReadOnlyList<SeriesSpec> Series { get; }
    public CameraSpec? Camera { get; }
    public bool ShowLegend { get; }
    public string Title { get; }

    public PlotSpec(Guid plotId, PlotKind kind, AxisSpec x, AxisSpec y, AxisSpec? z,
        IReadOnlyList<SeriesSpec> series, CameraSpec? camera, bool showLegend, string title,
        int schemaVersion = PlotProtocolVersions.Current)
    {
        PlotProtocolVersions.RequireCurrent(schemaVersion, nameof(schemaVersion));
        Guard.NonEmpty(plotId, nameof(plotId));
        Guard.Defined(kind, nameof(kind));
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        ArgumentNullException.ThrowIfNull(series);
        Guard.NonBlank(title, nameof(title));

        SeriesSpec[] seriesSnapshot = [.. series];
        if (seriesSnapshot.Length == 0 || seriesSnapshot.Any(item => item is null))
        {
            throw new ArgumentException("A plot must contain at least one non-null series.", nameof(series));
        }

        if (seriesSnapshot.Select(item => item.SeriesId).Distinct(StringComparer.Ordinal).Count() != seriesSnapshot.Length)
        {
            throw new ArgumentException("Series identifiers must be unique.", nameof(series));
        }

        bool is3D = kind is PlotKind.Scatter3D or PlotKind.Line3D;
        if (is3D != (z is not null) || is3D != (camera is not null))
        {
            throw new ArgumentException("Three-dimensional plots require Z and Camera; two-dimensional plots forbid both.", nameof(z));
        }

        SchemaVersion = schemaVersion;
        PlotId = plotId;
        Kind = kind;
        X = x;
        Y = y;
        Z = z;
        Series = Array.AsReadOnly(seriesSnapshot);
        Camera = camera;
        ShowLegend = showLegend;
        Title = title;
    }
}

public static class PlotProtocolVersions
{
    public const int Current = 1;

    internal static void RequireCurrent(int value, string name)
    {
        if (value != Current)
        {
            throw new ArgumentOutOfRangeException(name, value, $"{name} must equal the current protocol version {Current}.");
        }
    }
}

internal static class Guard
{
    public static void NonEmpty(Guid value, string name)
    {
        if (value == Guid.Empty) throw new ArgumentException($"{name} must not be empty.", name);
    }

    public static void NonBlank(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} must not be blank.", name);
    }

    public static void Identifier(string value, string name)
    {
        NonBlank(value, name);
        if (value.Length > 128 || value.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.')))
        {
            throw new ArgumentException($"{name} is not a safe protocol identifier.", name);
        }
    }

    public static void BoundedText(string value, int maximumLength, string name)
    {
        if (value.Length > maximumLength || value.Any(char.IsControl))
        {
            throw new ArgumentException($"{name} exceeds its limit or contains a control character.", name);
        }
    }

    public static void Finite(double value, string name)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(name, value, $"{name} must be finite.");
    }

    public static void Finite(double? value, string name)
    {
        if (value is not null) Finite(value.Value, name);
    }

    public static void Defined<TEnum>(TEnum value, string name) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(name, value, $"{name} must be a defined protocol value.");
        }
    }
}
