namespace FluoAxis.Analysis.Tests;

using FluoAxis.Analysis;
using FluoAxis.Domain;
using Xunit;

public class AnalysisMathTests
{
    private static readonly BrafMethodSnapshot Method = BrafMethodSnapshot.Default;

    // ---------------------------------------------------------------
    // EvaluatePlateau: RR boundary semantics (<=, fraction form)
    // ---------------------------------------------------------------

    [Fact]
    public void Plateau_passes_when_computed_binary64_range_equals_the_configured_limit()
    {
        // Integer subtraction and averaging are exact here. The final 2/200
        // division rounds to the same binary64 value as the configured 0.01.
        PlateauEvaluation e = AnalysisMath.EvaluatePlateau([199.0, 201.0], 0.01, 2);

        Assert.Equal(QcStatus.Pass, e.QcStatus);
        Assert.Equal(200.0, e.Mean);
        Assert.Equal(0.01, e.RelativeRange);
    }

    [Fact]
    public void Plateau_passes_when_relative_range_is_below_the_limit()
    {
        // {99.9, 100.1}: max-min = 0.2, mean = 100, RR = 0.002 < 0.01.
        PlateauEvaluation below = AnalysisMath.EvaluatePlateau([99.9, 100.1], 0.01, 2);

        Assert.Equal(QcStatus.Pass, below.QcStatus);
        Assert.Equal(0.002, below.RelativeRange!.Value, 12);
    }

    [Fact]
    public void Plateau_fails_when_relative_range_exceeds_the_limit()
    {
        PlateauEvaluation e = AnalysisMath.EvaluatePlateau([99.0, 101.0], 0.01, 2);

        // RR = 2/100 = 0.02 > 0.01 -> FAIL (numerical, not Invalid)
        Assert.Equal(QcStatus.Fail, e.QcStatus);
        Assert.Equal(0.02, e.RelativeRange!.Value, 12);
        Assert.Null(e.ErrorCode);
    }

    [Fact]
    public void Plateau_empty_window_is_invalid_with_insufficient_points()
    {
        PlateauEvaluation e = AnalysisMath.EvaluatePlateau([], 0.01, 2);

        Assert.Equal(QcStatus.Invalid, e.QcStatus);
        Assert.Equal(ErrorCodes.CalibrationInsufficientWindowPoints, e.ErrorCode);
    }

    [Fact]
    public void Plateau_window_with_one_point_is_invalid_when_minimum_is_two()
    {
        PlateauEvaluation e = AnalysisMath.EvaluatePlateau([100.0], 0.01, 2);

        Assert.Equal(QcStatus.Invalid, e.QcStatus);
        Assert.Equal(ErrorCodes.CalibrationInsufficientWindowPoints, e.ErrorCode);
    }

    [Fact]
    public void Plateau_two_points_pass_when_minimum_is_two()
    {
        PlateauEvaluation e = AnalysisMath.EvaluatePlateau([100.0, 100.0], 0.01, 2);

        Assert.Equal(QcStatus.Pass, e.QcStatus);
        Assert.Equal(0.0, e.RelativeRange!.Value);
        Assert.Equal(100.0, e.Mean!.Value);
    }

    [Fact]
    public void Plateau_non_positive_mean_is_invalid()
    {
        PlateauEvaluation e = AnalysisMath.EvaluatePlateau([-10.0, -10.0], 0.01, 2);

        Assert.Equal(QcStatus.Invalid, e.QcStatus);
        Assert.Equal(ErrorCodes.CalibrationNonPositiveMean, e.ErrorCode);
    }

    [Fact]
    public void Plateau_zero_mean_is_invalid()
    {
        PlateauEvaluation e = AnalysisMath.EvaluatePlateau([0.0, 0.0], 0.01, 2);

        Assert.Equal(QcStatus.Invalid, e.QcStatus);
        Assert.Equal(ErrorCodes.CalibrationNonPositiveMean, e.ErrorCode);
    }

    // ---------------------------------------------------------------
    // Normalize: denominator fail-closed semantics
    // ---------------------------------------------------------------

    [Fact]
    public void Normalize_computes_the_normalized_value()
    {
        Assert.Equal(1.0, AnalysisMath.Normalize(1000.0, 100.0, 1000.0));
        Assert.Equal(0.0, AnalysisMath.Normalize(100.0, 100.0, 1000.0));
        Assert.Equal(0.5, AnalysisMath.Normalize(550.0, 100.0, 1000.0));
    }

    [Fact]
    public void Normalize_returns_null_for_zero_denominator()
    {
        Assert.Null(AnalysisMath.Normalize(500.0, 100.0, 100.0));
    }

    [Fact]
    public void Normalize_returns_null_for_negative_denominator()
    {
        Assert.Null(AnalysisMath.Normalize(50.0, 100.0, 40.0));
    }

    [Fact]
    public void Normalize_returns_null_for_non_finite_denominator()
    {
        Assert.Null(AnalysisMath.Normalize(500.0, 100.0, double.NaN));
        Assert.Null(AnalysisMath.Normalize(500.0, double.NaN, 1000.0));
        Assert.Null(AnalysisMath.Normalize(double.PositiveInfinity, 100.0, 1000.0));
    }

    [Fact]
    public void Normalize_returns_null_for_non_finite_input_or_result()
    {
        Assert.Null(AnalysisMath.Normalize(double.NaN, 100.0, 1000.0));
        // Overflowing numerator -> infinite result -> null (fail closed).
        Assert.Null(AnalysisMath.Normalize(double.MaxValue, 0.0, 1.0 / double.MaxValue * 2.0));
    }

    // ---------------------------------------------------------------
    // Distance2D
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(0.0, 0.0, 0.4, 0.0, 0.4)]
    [InlineData(0.0, 0.0, 0.0, 0.4, 0.4)]
    [InlineData(0.0, 0.0, 0.3, 0.4, 0.5)]
    [InlineData(0.3, 0.4, 0.0, 0.0, 0.5)]
    public void Distance2D_computes_the_euclidean_distance(double x1, double y1, double x2, double y2, double expected)
    {
        Assert.Equal(expected, AnalysisMath.Distance2D(x1, y1, x2, y2), 12);
    }

    [Fact]
    public void Distance2D_of_the_threshold_fixture_equals_the_configured_binary64_value()
    {
        // Decimal 0.4 is not exactly representable. This fixture verifies that
        // the computed value equals the same rounded binary64 threshold value.
        double d = AnalysisMath.Distance2D(0.0, 0.0, 0.4, 0.0);
        Assert.Equal(0.4, d);
    }

    // ---------------------------------------------------------------
    // Endpoint selection: closed 15-second tolerance around 120 minutes
    // ---------------------------------------------------------------

    [Fact]
    public void SelectEndpoint_accepts_a_point_exactly_at_the_tolerance_boundary()
    {
        // GD-018: 119 min 45 s is exactly 15 seconds before the endpoint.
        TimeSpan boundary = TimeSpan.FromMinutes(119) + TimeSpan.FromSeconds(45);
        IReadOnlyList<SignalPoint> points = [TestWells.P(0, 100), new(boundary, 200)];

        EndpointSelection s = AnalysisMath.SelectEndpoint(points, Method);

        Assert.True(s.IsFound);
        Assert.Equal(200.0, s.Point!.Value.Rfu);
        Assert.Equal(1, s.PointIndex);
    }

    [Fact]
    public void SelectEndpoint_rejects_a_point_just_outside_the_tolerance_boundary()
    {
        // GD-019: 119 min 44.9 s is just outside the 15-second window.
        TimeSpan justOutside = TimeSpan.FromMinutes(119) + TimeSpan.FromSeconds(44.9);
        IReadOnlyList<SignalPoint> points = [TestWells.P(0, 100), new(justOutside, 200)];

        EndpointSelection s = AnalysisMath.SelectEndpoint(points, Method);

        Assert.False(s.IsFound);
        Assert.Equal(ErrorCodes.CalibrationMissingEndpoint, s.Error!.Code);
    }

    [Fact]
    public void SelectEndpoint_selects_the_point_nearest_the_nominal_endpoint()
    {
        IReadOnlyList<SignalPoint> points =
        [
            TestWells.P(0, 1),
            new(TimeSpan.FromMinutes(119) + TimeSpan.FromSeconds(46), 2),
            new(TimeSpan.FromMinutes(119) + TimeSpan.FromSeconds(59), 3),
            new(TimeSpan.FromMinutes(120) + TimeSpan.FromSeconds(14), 4),
        ];

        EndpointSelection s = AnalysisMath.SelectEndpoint(points, Method);

        Assert.Equal(2, s.PointIndex);
        Assert.Equal(3.0, s.Point!.Value.Rfu);
    }

    [Fact]
    public void SelectEndpoint_uses_the_later_point_as_an_equal_distance_tie_break()
    {
        IReadOnlyList<SignalPoint> points =
        [
            new(TimeSpan.FromMinutes(119) + TimeSpan.FromSeconds(50), 2),
            new(TimeSpan.FromMinutes(120) + TimeSpan.FromSeconds(10), 9),
        ];

        EndpointSelection s = AnalysisMath.SelectEndpoint(points, Method);

        Assert.True(s.IsFound);
        Assert.Equal(9.0, s.Point!.Value.Rfu);
    }

    [Theory]
    [InlineData(119, 44.9)]
    [InlineData(120, 15.1)]
    [InlineData(121, 0)]
    public void SelectEndpoint_rejects_points_outside_either_side_of_the_tolerance_window(int minutes, double seconds)
    {
        IReadOnlyList<SignalPoint> points = [new(TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds), 9)];

        EndpointSelection s = AnalysisMath.SelectEndpoint(points, Method);

        Assert.False(s.IsFound);
        Assert.Equal(ErrorCodes.CalibrationMissingEndpoint, s.Error!.Code);
    }

    [Fact]
    public void SelectEndpoint_honors_a_custom_tolerance_in_seconds()
    {
        BrafMethodSnapshot method = new(endpointToleranceSeconds: 30);
        IReadOnlyList<SignalPoint> points = [new(TimeSpan.FromMinutes(119) + TimeSpan.FromSeconds(30), 7)];

        EndpointSelection s = AnalysisMath.SelectEndpoint(points, method);

        Assert.True(s.IsFound);
        Assert.Equal(7.0, s.Point!.Value.Rfu);
    }

    [Fact]
    public void SelectEndpoint_of_an_empty_series_is_missing()
    {
        EndpointSelection s = AnalysisMath.SelectEndpoint([], Method);

        Assert.False(s.IsFound);
        Assert.Equal(ErrorCodes.CalibrationMissingEndpoint, s.Error!.Code);
    }

    // ---------------------------------------------------------------
    // Window selection: closed interval [90, 120]
    // ---------------------------------------------------------------

    [Fact]
    public void SelectWindow_includes_both_interval_ends()
    {
        IReadOnlyList<SignalPoint> points =
        [
            TestWells.P(0, 1),
            TestWells.P(89.9, 2),
            TestWells.P(90.0, 3),
            TestWells.P(100.0, 4),
            TestWells.P(120.0, 5),
            TestWells.P(120.1, 6),
        ];

        IReadOnlyList<SignalPoint> window = AnalysisMath.SelectWindow(points, Method);

        Assert.Equal(3, window.Count);
        Assert.Equal(90.0, window[0].Elapsed.TotalMinutes);
        Assert.Equal(100.0, window[1].Elapsed.TotalMinutes);
        Assert.Equal(120.0, window[2].Elapsed.TotalMinutes);
    }

    [Fact]
    public void SelectWindow_of_points_outside_the_window_is_empty()
    {
        IReadOnlyList<SignalPoint> points = [TestWells.P(0, 1), TestWells.P(60, 2), TestWells.P(121, 3)];

        Assert.Empty(AnalysisMath.SelectWindow(points, Method));
    }

    // ---------------------------------------------------------------
    // Mean
    // ---------------------------------------------------------------

    [Fact]
    public void Mean_of_an_empty_span_is_null()
    {
        Assert.Null(AnalysisMath.Mean([]));
    }

    [Fact]
    public void Mean_of_non_finite_values_is_null()
    {
        Assert.Null(AnalysisMath.Mean([1.0, double.NaN]));
    }

    // ---------------------------------------------------------------
    // Input order defense: SignalSeries.Create rejects unordered input
    // (the import layer contract the engine relies on)
    // ---------------------------------------------------------------

    [Fact]
    public void SignalSeries_rejects_out_of_order_points()
    {
        Assert.Throws<ArgumentException>(() =>
            SignalSeries.Create(TestWells.RunId, new WellId('A', 1), FluorescenceChannel.Fam, [TestWells.P(5, 1), TestWells.P(0, 2)]));
    }

    [Fact]
    public void SignalSeries_rejects_duplicate_elapsed_times()
    {
        Assert.Throws<ArgumentException>(() =>
            SignalSeries.Create(TestWells.RunId, new WellId('A', 1), FluorescenceChannel.Fam, [TestWells.P(2, 1), TestWells.P(2, 2)]));
    }

    [Fact]
    public void SignalSeries_rejects_non_finite_rfu()
    {
        Assert.Throws<ArgumentException>(() =>
            SignalSeries.Create(TestWells.RunId, new WellId('A', 1), FluorescenceChannel.Fam, [TestWells.P(0, double.NaN)]));
    }

    [Fact]
    public void SignalSeries_accepts_negative_rfu()
    {
        // Raw negative values are legal (Method-Spec: no negative-RFU ban;
        // GD-017); they flow into plateau/normalization as-is.
        SignalSeries s = SignalSeries.Create(TestWells.RunId, new WellId('A', 1), FluorescenceChannel.Fam, [TestWells.P(0, -5.0), TestWells.P(2, -4.0)]);

        Assert.Equal(-5.0, s.Points[0].Rfu);
    }

    // ---------------------------------------------------------------
    // Affine invariance (GD-020): x8 bit-exact, x10 within 1e-12
    // ---------------------------------------------------------------

    [Fact]
    public void Normalize_is_bit_exact_under_power_of_two_scaling()
    {
        // f, fb, fm all scaled by 8: (8f-8fb)/(8fm-8fb) = (f-fb)/(fm-fb)
        // exactly in binary64 (multiplication by a power of two is exact,
        // subtraction/division of exactly-scaled operands preserves the
        // quotient bit-for-bit).
        double y = AnalysisMath.Normalize(550.0, 100.0, 1000.0)!.Value;
        double y8 = AnalysisMath.Normalize(550.0 * 8, 100.0 * 8, 1000.0 * 8)!.Value;

        Assert.Equal(y, y8); // bit-exact equality (Assert.Equal on doubles)
    }

    [Fact]
    public void Normalize_is_invariant_within_1e_12_under_decimal_scaling()
    {
        double y = AnalysisMath.Normalize(551.0, 100.0, 1001.0)!.Value;
        double y10 = AnalysisMath.Normalize(5510.0, 1000.0, 10010.0)!.Value;

        Assert.Equal(y, y10, 12);
    }

    [Fact]
    public void Distance2D_is_invariant_under_uniform_scaling_up_to_relative_tolerance()
    {
        // Scaling both coordinates by 10 scales the distance by 10; the
        // classification-relevant invariant (compare against a likewise
        // scaled threshold) is preserved exactly: d/10 vs d' / 10 differ only
        // by the (exact) power-of-two-free rounding of the decimal scale.
        double d = AnalysisMath.Distance2D(0.5, 0.25, 1.5, 2.25);
        double d10 = AnalysisMath.Distance2D(5.0, 2.5, 15.0, 22.5);

        Assert.Equal(d, d10 / 10.0, 12);
    }
}
