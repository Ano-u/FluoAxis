namespace FluoAxis.Analysis;

using FluoAxis.Domain;

/// <summary>
/// Stateless primitive computations shared by every analysis stage.
/// Every method is pure and deterministic: no IO, no global state, no
/// clock. Numeric failures are reported as structured diagnostics, never
/// as thrown exceptions.
/// </summary>
public static class AnalysisMath
{
    /// <summary>
    /// Evaluates the plateau flatness of a 100% positive well window.
    /// The relative range is compared in fraction form directly against
    /// <paramref name="relativeRangeMax"/> with the inclusive operator
    /// &lt;= (Method-Spec §3.5: RR == 1% passes).
    /// </summary>
    /// <param name="values">RFU values inside the 90–120 min window.</param>
    /// <param name="relativeRangeMax">Maximum tolerated relative range, as a fraction (0.01 = 1%).</param>
    /// <param name="minWindowPoints">Fail-closed minimum point count (Method-Spec §4.5).</param>
    public static PlateauEvaluation EvaluatePlateau(ReadOnlySpan<double> values, double relativeRangeMax, int minWindowPoints)
    {
        if (values.Length == 0)
        {
            return PlateauEvaluation.Invalid(ErrorCodes.CalibrationInsufficientWindowPoints, "The plateau window contains no points.");
        }

        if (values.Length < minWindowPoints)
        {
            return PlateauEvaluation.Invalid(ErrorCodes.CalibrationInsufficientWindowPoints, $"The plateau window contains {values.Length} point(s); at least {minWindowPoints} are required.");
        }

        double min = double.MaxValue;
        double max = double.MinValue;
        double sum = 0;

        for (int i = 0; i < values.Length; i++)
        {
            if (!double.IsFinite(values[i]))
            {
                return PlateauEvaluation.Invalid(ErrorCodes.CalibrationNonFiniteValue, $"Window point {i} has a non-finite RFU value.");
            }

            if (values[i] < min)
            {
                min = values[i];
            }

            if (values[i] > max)
            {
                max = values[i];
            }

            sum += values[i];
        }

        double mean = sum / values.Length;

        if (!double.IsFinite(mean))
        {
            return PlateauEvaluation.Invalid(ErrorCodes.CalibrationNonFiniteValue, "The plateau window mean is not finite.");
        }

        if (mean <= 0)
        {
            return PlateauEvaluation.Invalid(ErrorCodes.CalibrationNonPositiveMean, "The plateau window mean must be positive.");
        }

        double relativeRange = (max - min) / mean;

        if (!double.IsFinite(relativeRange))
        {
            return PlateauEvaluation.Invalid(ErrorCodes.CalibrationNonFiniteValue, "The computed relative range is not finite.");
        }

        return new PlateauEvaluation(
            qcStatus: relativeRange <= relativeRangeMax ? QcStatus.Pass : QcStatus.Fail,
            relativeRange: relativeRange,
            mean: mean,
            min: min,
            max: max,
            pointCount: values.Length,
            errorCode: null,
            errorMessage: null);
    }

    /// <summary>
    /// Normalizes a raw endpoint fluorescence value:
    /// Y = (f − fb) / (fm − fb). Returns null when the denominator is not
    /// strictly positive and finite, or when the result is not finite;
    /// the caller converts that into the structured
    /// <see cref="ErrorCodes.CalibrationInvalidDenominator"/> /
    /// <see cref="ErrorCodes.CalibrationNonFiniteValue"/> failure.
    /// </summary>
    public static double? Normalize(double f, double fb, double fm)
    {
        double denominator = fm - fb;

        if (!double.IsFinite(denominator) || denominator <= 0)
        {
            return null;
        }

        double y = (f - fb) / denominator;

        return double.IsFinite(y) ? y : null;
    }

    /// <summary>Euclidean distance between two points (Method-Spec §9.2).</summary>
    public static double Distance2D(double x1, double y1, double x2, double y2)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>
    /// Sequential (deterministic) arithmetic mean; returns null when the
    /// input is empty or the accumulated mean is not finite.
    /// </summary>
    public static double? Mean(ReadOnlySpan<double> values)
    {
        if (values.Length == 0)
        {
            return null;
        }

        double sum = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (!double.IsFinite(values[i]))
            {
                return null;
            }

            sum += values[i];
        }

        double mean = sum / values.Length;
        return double.IsFinite(mean) ? mean : null;
    }

    /// <summary>
    /// Selects the valid point nearest the nominal endpoint inside the closed
    /// endpoint +/- tolerance window (Method-Spec §4.3). No interpolation is
    /// performed. Equal-distance candidates are resolved to the later point
    /// so selection remains deterministic.
    /// </summary>
    /// <param name="points">Strictly ascending points of one well and channel.</param>
    /// <param name="method">Method snapshot supplying endpoint and tolerance.</param>
    public static EndpointSelection SelectEndpoint(IReadOnlyList<SignalPoint> points, BrafMethodSnapshot method)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(method);

        TimeSpan endpoint = TimeSpan.FromMinutes(method.EndpointMinutes);
        TimeSpan tolerance = TimeSpan.FromSeconds(method.EndpointToleranceSeconds);

        int bestIndex = -1;
        SignalPoint best = default;
        long bestDistanceTicks = long.MaxValue;

        for (int i = 0; i < points.Count; i++)
        {
            long distanceTicks = Math.Abs(points[i].Elapsed.Ticks - endpoint.Ticks);
            if (distanceTicks <= tolerance.Ticks
                && (distanceTicks < bestDistanceTicks
                    || (distanceTicks == bestDistanceTicks && points[i].Elapsed > best.Elapsed)))
            {
                bestIndex = i;
                best = points[i];
                bestDistanceTicks = distanceTicks;
            }
        }

        if (bestIndex < 0)
        {
            return new EndpointSelection(
                isFound: false,
                point: null,
                pointIndex: null,
                error: new AppError(
                    ErrorCodes.CalibrationMissingEndpoint,
                    ErrorSeverity.Blocking,
                    $"No acquisition point exists within {method.EndpointToleranceSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} second(s) of the {method.EndpointMinutes.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} minute endpoint; the endpoint value cannot be interpolated."));
        }

        return new EndpointSelection(isFound: true, point: best, pointIndex: bestIndex, error: null);
    }

    /// <summary>
    /// Selects every point inside the closed plateau window
    /// start ≤ t ≤ end (Method-Spec §4.4).
    /// </summary>
    public static IReadOnlyList<SignalPoint> SelectWindow(IReadOnlyList<SignalPoint> points, BrafMethodSnapshot method)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(method);

        TimeSpan start = TimeSpan.FromMinutes(method.Plateau.StartMinutes);
        TimeSpan end = TimeSpan.FromMinutes(method.Plateau.EndMinutes);

        List<SignalPoint> window = [];
        foreach (SignalPoint p in points)
        {
            if (p.Elapsed >= start && p.Elapsed <= end)
            {
                window.Add(p);
            }
        }

        return window;
    }

    /// <summary>
    /// Selects every point in the closed interval 0 ≤ t ≤ endpoint
    /// (the NTC full-course QC domain, Method-Spec §7.2).
    /// </summary>
    public static IReadOnlyList<SignalPoint> SelectFullCourse(IReadOnlyList<SignalPoint> points, BrafMethodSnapshot method)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(method);

        TimeSpan end = TimeSpan.FromMinutes(method.EndpointMinutes);

        List<SignalPoint> course = [];
        foreach (SignalPoint p in points)
        {
            if (p.Elapsed >= TimeSpan.Zero && p.Elapsed <= end)
            {
                course.Add(p);
            }
        }

        return course;
    }
}

/// <summary>Outcome of one plateau (100% positive) flatness evaluation.</summary>
public sealed record PlateauEvaluation
{
    public QcStatus QcStatus { get; }
    public double? RelativeRange { get; }
    public double? Mean { get; }
    public double? Min { get; }
    public double? Max { get; }
    public int PointCount { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    internal PlateauEvaluation(
        QcStatus qcStatus,
        double? relativeRange,
        double? mean,
        double? min,
        double? max,
        int pointCount,
        string? errorCode,
        string? errorMessage)
    {
        QcStatus = qcStatus;
        RelativeRange = relativeRange;
        Mean = mean;
        Min = min;
        Max = max;
        PointCount = pointCount;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    internal static PlateauEvaluation Invalid(string errorCode, string message) =>
        new(QcStatus.Invalid, null, null, null, null, 0, errorCode, message);

    /// <summary>Success constructor used when the window evaluation completed numerically.</summary>
    public static PlateauEvaluation FromComputed(QcStatus status, double relativeRange, double mean, double min, double max, int pointCount) =>
        new(status, relativeRange, mean, min, max, pointCount, null, null);
}

/// <summary>Result of the endpoint point selection for one well and channel.</summary>
public sealed record EndpointSelection
{
    public bool IsFound { get; }
    public SignalPoint? Point { get; }
    public int? PointIndex { get; }
    public AppError? Error { get; }

    internal EndpointSelection(bool isFound, SignalPoint? point, int? pointIndex, AppError? error)
    {
        IsFound = isFound;
        Point = point;
        PointIndex = pointIndex;
        Error = error;
    }

    internal static EndpointSelection Missing(AppError error) => new(false, null, null, error);
}
