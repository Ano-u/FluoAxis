namespace FluoAxis.Analysis.Tests;

using FluoAxis.Analysis;
using FluoAxis.Domain;
using Xunit;

/// <summary>
/// Full-chain integration tests of the reference run: 1 NTC (A1), 1 positive
/// (B1), 2 WT (C1/C2), 1 LQC (D1), 1 sample of 2 wells (E1/E2).
///
/// Hand-computable frame (flat wells, endpoint = 120 min):
///   Fm_FAM = 1000, Fb_FAM = 100, denominator FAM = 900
///   Fm_ROX = 500, Fb_ROX = 50,  denominator ROX = 450
///   WT reference = (0, 0) (WT wells sit exactly at Fb)
///   dr'(Y) = |Y_FAM| when Y_ROX = 0.
/// </summary>
public class BrafAnalysisEngineTests
{
    private static readonly BrafAnalysisEngine Engine = new();

    private static readonly BrafMethodSnapshot Method = BrafMethodSnapshot.Default;

    [Fact]
    public void Control_evaluation_returns_structured_failure_when_finite_wt_values_overflow_the_mean()
    {
        var calibration = new CalibrationResult(
            [], [],
            new Dictionary<FluorescenceChannel, double> { [FluorescenceChannel.Fam] = 1, [FluorescenceChannel.Rox] = 1 },
            new Dictionary<FluorescenceChannel, double> { [FluorescenceChannel.Fam] = 0, [FluorescenceChannel.Rox] = 0 },
            QcStatus.Pass, []);
        var input = new ControlQcInput(
            [TestWells.Flat("C1", double.MaxValue, 0), TestWells.Flat("C2", double.MaxValue, 0)],
            [TestWells.Flat("D1", 1, 0)]);

        Result<ControlQcResult> result = Engine.EvaluateControls(input, calibration, Method);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.CalibrationNonFiniteValue, Assert.Single(result.Errors).Code);
    }

    // ---------------------------------------------------------------
    // Full pass chain
    // ---------------------------------------------------------------

    [Fact]
    public void Reference_run_calibrates_passes_controls_and_classifies_the_sample_positive()
    {
        Result<CalibrationResult> calibration = Engine.Calibrate(TestWells.ReferenceCalibrationInput(), Method);
        Assert.True(calibration.IsSuccess, string.Join("; ", calibration.Errors.Select(e => e.UserMessage)));
        Assert.Equal(QcStatus.Pass, calibration.Value!.QcStatus);

        // Plateau mean = window mean of the flat 1000 line.
        PositiveWellResult positiveFam = calibration.Value.PositiveWells.Single(w => w.Channel == FluorescenceChannel.Fam);
        Assert.Equal(1000.0, positiveFam.Fm!.Value);
        Assert.Equal(0.0, positiveFam.Plateau!.RelativeRange!.Value);

        PositiveWellResult positiveRox = calibration.Value.PositiveWells.Single(w => w.Channel == FluorescenceChannel.Rox);
        Assert.Equal(500.0, positiveRox.Fm!.Value);

        // Run-level Fm per channel; NTC per-well Fb at its own endpoint.
        Assert.Equal(1000.0, calibration.Value.RunFm[FluorescenceChannel.Fam]);
        Assert.Equal(500.0, calibration.Value.RunFm[FluorescenceChannel.Rox]);
        NtcWellResult ntcFam = calibration.Value.NtcWells.Single(w => w.Channel == FluorescenceChannel.Fam);
        Assert.Equal(100.0, ntcFam.Fb);
        Assert.Equal(61, ntcFam.QcPointCount);
        Assert.Empty(ntcFam.OutOfBandPoints);
        Assert.Equal(100.0, calibration.Value.RunFb[FluorescenceChannel.Fam]);
        Assert.Equal(50.0, calibration.Value.RunFb[FluorescenceChannel.Rox]);

        // Controls: WT endpoint Y = (0, 0) both channels, mean (0, 0).
        Result<ControlQcResult> controls = Engine.EvaluateControls(TestWells.ReferenceControlInput(), calibration.Value, Method);
        Assert.True(controls.IsSuccess);
        Assert.Equal(QcStatus.Pass, controls.Value!.QcStatus);
        Assert.Equal(0.0, controls.Value.WtReference!.YFamMean);
        Assert.Equal(0.0, controls.Value.WtReference.YRoxMean);

        // LQC at dr' = 0.6 -> Positive direction -> LQC PASS.
        WellDrPrimeResult lqc = controls.Value.LqcWells.Single();
        Assert.Equal(0.6, lqc.DrPrime!.Value, 12);
        Assert.Equal(DrPrimeCall.Positive, lqc.Call);

        // Sample: two wells at dr' = 0.6 -> Positive + MeanDrPrime 0.6.
        Result<SampleAnalysisResult> samples = Engine.AnalyzeSamples(TestWells.ReferenceSampleInput(), controls.Value.WtReference, calibration.Value, Method);
        Assert.True(samples.IsSuccess);

        SampleResult sample = samples.Value!.Results.Single();
        Assert.Equal(QcStatus.Pass, sample.Status);
        Assert.Equal(Method.Classification.PositiveLabel, sample.Call);
        Assert.Equal(0.6, sample.MeanDrPrime!.Value, 12);
        Assert.Equal(2, sample.Wells.Count);
    }

    [Fact]
    public void Consistent_negative_sample_classifies_negative_with_mean()
    {
        // Two wells at dr' = 0.35 (0.35 < 0.4): Y_FAM = 0.35.
        SampleAnalysisInput input = new([new SampleGroup("S1", [TestWells.AtDrPrime("E1", 0.35), TestWells.AtDrPrime("E2", 0.35)])]);
        var (calibration, controls) = ReferenceStages();

        Result<SampleAnalysisResult> result = Engine.AnalyzeSamples(input, controls.WtReference!, calibration, Method);

        SampleResult sample = result.Value!.Results.Single();
        Assert.Equal(QcStatus.Pass, sample.Status);
        Assert.Equal(Method.Classification.NegativeLabel, sample.Call);
        Assert.Equal(0.35, sample.MeanDrPrime!.Value, 12);
    }

    [Fact]
    public void Three_wells_at_039_are_all_negative_and_the_mean_is_039()
    {
        // Monotonicity lock (Method-Spec §9.8): all single wells Negative
        // => mean < 0.4 on the same side; the mean never becomes the verdict.
        SampleAnalysisInput input = new(
        [
            new SampleGroup("S1", [TestWells.AtDrPrime("E1", 0.39), TestWells.AtDrPrime("E2", 0.39), TestWells.AtDrPrime("E3", 0.39)]),
        ]);
        var (calibration, controls) = ReferenceStages();

        Result<SampleAnalysisResult> result = Engine.AnalyzeSamples(input, controls.WtReference!, calibration, Method);

        SampleResult sample = result.Value!.Results.Single();
        Assert.Equal(QcStatus.Pass, sample.Status);
        Assert.Equal(Method.Classification.NegativeLabel, sample.Call);
        Assert.Equal(0.39, sample.MeanDrPrime!.Value, 12);
        Assert.All(sample.Wells, w => Assert.Equal(DrPrimeCall.Negative, w.Call));
        Assert.True(sample.MeanDrPrime!.Value < 0.4);
    }

    [Fact]
    public void Exact_threshold_04_is_positive()
    {
        // GD-010: the computed distance equals the configured binary64 value
        // for 0.4, exercising the inclusive >= threshold without claiming
        // that decimal 0.4 itself is exactly representable.
        SampleAnalysisInput input = new([new SampleGroup("S1", [TestWells.AtY("E1", 0.4, 0)])]);
        var (calibration, controls) = ReferenceStages();

        Result<SampleAnalysisResult> result = Engine.AnalyzeSamples(input, controls.WtReference!, calibration, Method);

        SampleResult sample = result.Value!.Results.Single();
        Assert.Equal(Method.Classification.PositiveLabel, sample.Call);
        Assert.Equal(0.4, sample.Wells.Single().DrPrime!.Value);
    }

    [Fact]
    public void Just_below_threshold_is_negative_and_just_above_is_positive()
    {
        SampleAnalysisInput below = new([new SampleGroup("S1", [TestWells.AtDrPrime("E1", 0.399999)])]);
        SampleAnalysisInput above = new([new SampleGroup("S2", [TestWells.AtDrPrime("E2", 0.400001)])]);
        var (calibration, controls) = ReferenceStages();

        SampleResult belowResult = Engine.AnalyzeSamples(below, controls.WtReference!, calibration, Method).Value!.Results.Single();
        SampleResult aboveResult = Engine.AnalyzeSamples(above, controls.WtReference!, calibration, Method).Value!.Results.Single();

        Assert.Equal(Method.Classification.NegativeLabel, belowResult.Call);
        Assert.Equal(Method.Classification.PositiveLabel, aboveResult.Call);
    }

    [Fact]
    public void Replicate_conflict_yields_invalid_without_call_and_without_mean()
    {
        // dr' 0.35 (Negative) + 0.45 (Positive) -> mixed -> INVALID.
        SampleAnalysisInput input = new([new SampleGroup("S1", [TestWells.AtDrPrime("E1", 0.35), TestWells.AtDrPrime("E2", 0.45)])]);
        var (calibration, controls) = ReferenceStages();

        Result<SampleAnalysisResult> result = Engine.AnalyzeSamples(input, controls.WtReference!, calibration, Method);

        Assert.True(result.IsSuccess); // scientific INVALID, not an operation failure
        SampleResult sample = result.Value!.Results.Single();
        Assert.Equal(QcStatus.Invalid, sample.Status);
        Assert.Null(sample.Call);
        Assert.Null(sample.MeanDrPrime);
        Assert.Equal(ErrorCodes.SampleReplicateClassificationInconsistent, sample.ErrorCode);
        // Per-well calls still exist so the UI can show the disagreement.
        Assert.Equal(DrPrimeCall.Negative, sample.Wells[0].Call);
        Assert.Equal(DrPrimeCall.Positive, sample.Wells[1].Call);
    }

    [Fact]
    public void Legacy_false_consistency_flag_does_not_disable_mixed_sample_or_lqc_invalidity()
    {
        BrafMethodSnapshot legacyMethod = Method with
        {
            Replicates = new ReplicateSpec(requireClassificationConsistency: false),
        };
        var (calibration, controls) = ReferenceStages();
        SampleAnalysisInput samples = new([new SampleGroup("S1", [TestWells.AtDrPrime("E1", 0.35), TestWells.AtDrPrime("E2", 0.45)])]);
        ControlQcInput controlInput = new([TestWells.Wt("C1")], [TestWells.AtDrPrime("D1", 0.35), TestWells.AtDrPrime("D2", 0.45)]);

        Result<SampleAnalysisResult> result = Engine.AnalyzeSamples(samples, controls.WtReference!, calibration, legacyMethod);
        Result<ControlQcResult> controlResult = Engine.EvaluateControls(controlInput, calibration, legacyMethod);

        Assert.True(result.IsSuccess);
        SampleResult sample = Assert.Single(result.Value!.Results);
        Assert.Equal(QcStatus.Invalid, sample.Status);
        Assert.Null(sample.Call);
        Assert.Null(sample.MeanDrPrime);
        Assert.Equal(ErrorCodes.SampleReplicateClassificationInconsistent, sample.ErrorCode);
        Assert.True(controlResult.IsSuccess);
        Assert.Equal(QcStatus.Invalid, controlResult.Value!.QcStatus);
        Assert.Null(controlResult.Value.LqcDirection);
        Assert.Null(controlResult.Value.LqcMeanDrPrime);
    }

    [Fact]
    public void Legacy_zero_role_minima_still_fail_closed_for_each_missing_required_role()
    {
        BrafMethodSnapshot legacyMethod = Method with { Roles = new RoleCardinalitySpec(0, 0, 0, 0) };
        var (calibration, _) = ReferenceStages();
        Result<CalibrationResult> noNtc = Engine.Calibrate(new CalibrationInput([TestWells.Positive100("B1")], []), legacyMethod);
        Result<CalibrationResult> noPositive = Engine.Calibrate(new CalibrationInput([], [TestWells.Ntc("A1")]), legacyMethod);
        Result<ControlQcResult> noWt = Engine.EvaluateControls(new ControlQcInput([], [TestWells.AtDrPrime("D1", 0.6)]), calibration, legacyMethod);
        Result<ControlQcResult> noLqc = Engine.EvaluateControls(new ControlQcInput([TestWells.Wt("C1")], []), calibration, legacyMethod);

        Assert.False(noNtc.IsSuccess);
        Assert.False(noPositive.IsSuccess);
        Assert.False(noWt.IsSuccess);
        Assert.False(noLqc.IsSuccess);
        Assert.Equal(ErrorCodes.PlateRoleCardinality, Assert.Single(noNtc.Errors).Code);
        Assert.Equal(ErrorCodes.PlateRoleCardinality, Assert.Single(noPositive.Errors).Code);
        Assert.Equal(ErrorCodes.PlateRoleCardinality, Assert.Single(noWt.Errors).Code);
        Assert.Equal(ErrorCodes.PlateRoleCardinality, Assert.Single(noLqc.Errors).Code);
    }

    [Fact]
    public void Configured_role_minima_above_one_are_honored()
    {
        BrafMethodSnapshot method = Method with { Roles = new RoleCardinalitySpec(2, 2, 2, 2) };
        var (calibration, _) = ReferenceStages();
        Result<CalibrationResult> tooFewNtc = Engine.Calibrate(
            new CalibrationInput([TestWells.Positive100("B1"), TestWells.Positive100("B2")], [TestWells.Ntc("A1")]), method);
        Result<CalibrationResult> tooFewPositive = Engine.Calibrate(
            new CalibrationInput([TestWells.Positive100("B1")], [TestWells.Ntc("A1"), TestWells.Ntc("A2")]), method);
        Result<ControlQcResult> tooFewWt = Engine.EvaluateControls(
            new ControlQcInput([TestWells.Wt("C1")], [TestWells.AtDrPrime("D1", 0.6), TestWells.AtDrPrime("D2", 0.6)]), calibration, method);
        Result<ControlQcResult> tooFewLqc = Engine.EvaluateControls(
            new ControlQcInput([TestWells.Wt("C1"), TestWells.Wt("C2")], [TestWells.AtDrPrime("D1", 0.6)]), calibration, method);

        Assert.Equal(ErrorCodes.PlateRoleCardinality, Assert.Single(tooFewNtc.Errors).Code);
        Assert.Equal(ErrorCodes.PlateRoleCardinality, Assert.Single(tooFewPositive.Errors).Code);
        Assert.Equal(ErrorCodes.PlateRoleCardinality, Assert.Single(tooFewWt.Errors).Code);
        Assert.Equal(ErrorCodes.PlateRoleCardinality, Assert.Single(tooFewLqc.Errors).Code);
    }

    // ---------------------------------------------------------------
    // Calibration failure branches
    // ---------------------------------------------------------------

    [Fact]
    public void Ntc_point_outside_the_band_fails_calibration()
    {
        // NTC well with FAM 100 except one point at 200: Y = 100/900 ≈ 0.111
        // > 0.05 -> out of band (hand-computable: 100/900 = 0.111...).
        double[] minutes = [.. Enumerable.Range(0, 61).Select(i => (double)(i * 2))];
        double[] fam = [.. Enumerable.Repeat(100.0, 61)];
        double[] rox = [.. Enumerable.Repeat(50.0, 61)];
        fam[10] = 200.0; // t = 20 min
        WellSignals ntc = TestWells.Well("A1", fam, rox, minutes);

        Result<CalibrationResult> result = Engine.Calibrate(new CalibrationInput([TestWells.Positive100("B1")], [ntc]), Method);

        Assert.True(result.IsSuccess);
        Assert.Equal(QcStatus.Fail, result.Value!.QcStatus);
        Assert.Equal(ErrorCodes.CalibrationNtcRangeExceeded, result.Value.Diagnostics.Single().Code);
        NtcWellResult ntcFam = result.Value.NtcWells.Single(w => w.Channel == FluorescenceChannel.Fam);
        Assert.Single(ntcFam.OutOfBandPoints);
        Assert.Equal(100.0 / 900.0, ntcFam.OutOfBandPoints[0].Y, 12);
        // Run Fb never forms after a failing NTC well (Method-Spec §7.5).
        Assert.Empty(result.Value.RunFb);
    }

    [Fact]
    public void Plateau_relative_range_exceeded_fails_calibration()
    {
        // Positive FAM window: 61-point flat 1000 except two window points at
        // 1000 and 1200 -> RR = 200/1004.3... > 1%. Precisely: window is
        // 90..120 (points 45..60), set point 46 to 1200:
        // 16 points: 15 x 1000 + 1200 -> RR = 200 / (16200/16) = 200/1012.5
        // ≈ 0.1975 > 0.01 -> FAIL.
        double[] minutes = [.. Enumerable.Range(0, 61).Select(i => (double)(i * 2))];
        double[] fam = [.. Enumerable.Repeat(1000.0, 61)];
        double[] rox = [.. Enumerable.Repeat(500.0, 61)];
        fam[46] = 1200.0;
        WellSignals positive = TestWells.Well("B1", fam, rox, minutes);

        Result<CalibrationResult> result = Engine.Calibrate(new CalibrationInput([positive], [TestWells.Ntc("A1")]), Method);

        Assert.True(result.IsSuccess);
        Assert.Equal(QcStatus.Fail, result.Value!.QcStatus);
        Assert.Equal(AnalysisErrorCodes.CalibrationPlateauRangeExceeded, result.Value.Diagnostics.Single().Code);
        // Fm never forms: the failing well is not dropped (Method-Spec §5.5).
        Assert.Empty(result.Value.RunFm);
    }

    [Fact]
    public void Missing_endpoint_on_any_well_fails_the_calibration_operation()
    {
        // A positive well whose last point is 104 min: no point within 15 s
        // of 120 min -> MISSING_ENDPOINT before any computation.
        double[] minutes = [.. Enumerable.Range(0, 53).Select(i => (double)(i * 2))];
        WellSignals shortWell = TestWells.Well("B1", [.. Enumerable.Repeat(1000.0, 53)], [.. Enumerable.Repeat(500.0, 53)], minutes);

        Result<CalibrationResult> result = Engine.Calibrate(new CalibrationInput([shortWell], [TestWells.Ntc("A1")]), Method);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.CalibrationMissingEndpoint, result.Errors.Single().Code);
    }

    [Fact]
    public void Ntc_denominator_fails_closed_when_fm_run_is_not_above_fb()
    {
        // NTC FAM = 1000 (equal to Fm): Fm_run - Fb_i = 0 -> INVALID_DENOMINATOR
        // before the division (Method-Spec §6.1/§6.3).
        WellSignals ntcAtFm = TestWells.Flat("A1", 1000, 500);

        Result<CalibrationResult> result = Engine.Calibrate(new CalibrationInput([TestWells.Positive100("B1")], [ntcAtFm]), Method);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.CalibrationInvalidDenominator, result.Errors.Single().Code);
    }

    [Fact]
    public void Insufficient_window_points_fails_the_calibration_operation()
    {
        // Endpoint at 119 min 45 s exists (the inclusive 15-second boundary),
        // but the closed 90..120 window contains only that single point ->
        // INSUFFICIENT_WINDOW_POINTS
        // (fail closed, P-03).
        double[] minutes = [0, 50, 119.75];
        WellSignals sparseWell = TestWells.Well("B1", [1000, 1000, 1000], [500, 500, 500], minutes);

        Result<CalibrationResult> result = Engine.Calibrate(new CalibrationInput([sparseWell], [TestWells.Ntc("A1")]), Method);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.CalibrationInsufficientWindowPoints, result.Errors.Single().Code);
    }

    [Fact]
    public void Missing_role_wells_fail_with_plate_role_cardinality()
    {
        Result<CalibrationResult> noNtc = Engine.Calibrate(new CalibrationInput([TestWells.Positive100("B1")], []), Method);
        Assert.False(noNtc.IsSuccess);
        Assert.Equal(ErrorCodes.PlateRoleCardinality, noNtc.Errors.Single().Code);

        Result<CalibrationResult> noPositive = Engine.Calibrate(new CalibrationInput([], [TestWells.Ntc("A1")]), Method);
        Assert.False(noPositive.IsSuccess);
        Assert.Equal(ErrorCodes.PlateRoleCardinality, noPositive.Errors.Single().Code);
    }

    // ---------------------------------------------------------------
    // Control failure branches
    // ---------------------------------------------------------------

    [Fact]
    public void Wt_mean_at_or_above_the_exclusive_limit_fails_controls()
    {
        // WT wells at Y_FAM = 0.05: mean = 0.05, not < 0.05 -> WT FAIL.
        ControlQcInput input = new([TestWells.AtY("C1", 0.05, 0), TestWells.AtY("C2", 0.05, 0)], [TestWells.AtDrPrime("D1", 0.6)]);
        var (calibration, _) = ReferenceStages();

        Result<ControlQcResult> result = Engine.EvaluateControls(input, calibration, Method);

        Assert.True(result.IsSuccess);
        Assert.Equal(QcStatus.Fail, result.Value!.QcStatus);
        Assert.Equal(ErrorCodes.ControlWtMeanExceeded, result.Value.Diagnostics.Single().Code);
        // WT reference is not published after a WT failure.
        Assert.Null(result.Value.WtReference);
    }

    [Fact]
    public void Wt_mean_just_below_the_exclusive_limit_passes()
    {
        ControlQcInput input = new([TestWells.AtY("C1", 0.049999, 0), TestWells.AtY("C2", 0.049999, 0)], [TestWells.AtDrPrime("D1", 0.6)]);
        var (calibration, _) = ReferenceStages();

        Result<ControlQcResult> result = Engine.EvaluateControls(input, calibration, Method);

        Assert.Equal(QcStatus.Pass, result.Value!.QcStatus);
    }

    [Fact]
    public void Lqc_direction_negative_fails_controls()
    {
        // LQC at dr' = 0.2 -> Negative direction -> LQC FAIL (P-07).
        ControlQcInput input = new([TestWells.Wt("C1")], [TestWells.AtDrPrime("D1", 0.2)]);
        var (calibration, _) = ReferenceStages();

        Result<ControlQcResult> result = Engine.EvaluateControls(input, calibration, Method);

        Assert.True(result.IsSuccess);
        Assert.Equal(QcStatus.Fail, result.Value!.QcStatus);
        Assert.Equal(ErrorCodes.ControlLqcDirectionNegative, result.Value.Diagnostics.Single().Code);
        Assert.Equal(DrPrimeCall.Negative, result.Value.LqcDirection);
        Assert.Equal(0.2, result.Value.LqcMeanDrPrime!.Value, 12);
    }

    [Theory]
    [InlineData(0.2, 0.3, 0.25, QcStatus.Fail, DrPrimeCall.Negative)]
    [InlineData(0.5, 0.9, 0.7, QcStatus.Pass, DrPrimeCall.Positive)]
    public void Lqc_consistent_replicates_report_the_engine_mean_independent_of_expected_direction(
        double first, double second, double expectedMean, QcStatus expectedStatus, DrPrimeCall expectedDirection)
    {
        ControlQcInput input = new([TestWells.Wt("C1")], [TestWells.AtDrPrime("D1", first), TestWells.AtDrPrime("D2", second)]);
        var (calibration, _) = ReferenceStages();

        Result<ControlQcResult> result = Engine.EvaluateControls(input, calibration, Method);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedStatus, result.Value!.QcStatus);
        Assert.Equal(expectedDirection, result.Value.LqcDirection);
        Assert.Equal(expectedMean, result.Value.LqcMeanDrPrime!.Value, 12);
    }

    [Fact]
    public void Lqc_missing_endpoint_returns_no_group_result_or_mean()
    {
        ControlQcInput input = new([TestWells.Wt("C1")], [TestWells.Well("D1", [100, 640], [50, 50], [0, 90])]);
        var (calibration, _) = ReferenceStages();

        Result<ControlQcResult> result = Engine.EvaluateControls(input, calibration, Method);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(ErrorCodes.CalibrationMissingEndpoint, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Lqc_mixed_replicate_directions_make_the_run_invalid()
    {
        ControlQcInput input = new([TestWells.Wt("C1")], [TestWells.AtDrPrime("D1", 0.2), TestWells.AtDrPrime("D2", 0.6)]);
        var (calibration, _) = ReferenceStages();

        Result<ControlQcResult> result = Engine.EvaluateControls(input, calibration, Method);

        Assert.True(result.IsSuccess);
        Assert.Equal(QcStatus.Invalid, result.Value!.QcStatus);
        Assert.Equal(AnalysisErrorCodes.ControlLqcReplicateInconsistent, result.Value.Diagnostics.Single().Code);
        Assert.Null(result.Value.LqcDirection);
        Assert.Null(result.Value.LqcMeanDrPrime);
    }

    [Fact]
    public void Controls_and_samples_require_a_passed_calibration()
    {
        // A failed calibration (NTC out of band) blocks both downstream stages.
        double[] minutes = [.. Enumerable.Range(0, 61).Select(i => (double)(i * 2))];
        double[] fam = [.. Enumerable.Repeat(100.0, 61)];
        double[] rox = [.. Enumerable.Repeat(50.0, 61)];
        fam[10] = 200.0;
        WellSignals badNtc = TestWells.Well("A1", fam, rox, minutes);
        Result<CalibrationResult> failed = Engine.Calibrate(new CalibrationInput([TestWells.Positive100("B1")], [badNtc]), Method);
        Assert.Equal(QcStatus.Fail, failed.Value!.QcStatus);

        Result<ControlQcResult> controls = Engine.EvaluateControls(TestWells.ReferenceControlInput(), failed.Value, Method);
        Assert.False(controls.IsSuccess);
        Assert.Equal(AnalysisErrorCodes.ControlCalibrationNotPassed, controls.Errors.Single().Code);

        Result<SampleAnalysisResult> samples = Engine.AnalyzeSamples(TestWells.ReferenceSampleInput(), new WtReference(0, 0), failed.Value, Method);
        Assert.False(samples.IsSuccess);
        Assert.Equal(AnalysisErrorCodes.ControlCalibrationNotPassed, samples.Errors.Single().Code);
    }

    [Fact]
    public void Missing_control_role_wells_fail_with_plate_role_cardinality()
    {
        var (calibration, _) = ReferenceStages();

        Result<ControlQcResult> noWt = Engine.EvaluateControls(new ControlQcInput([], [TestWells.AtDrPrime("D1", 0.6)]), calibration, Method);
        Assert.False(noWt.IsSuccess);
        Assert.Equal(ErrorCodes.PlateRoleCardinality, noWt.Errors.Single().Code);

        Result<ControlQcResult> noLqc = Engine.EvaluateControls(new ControlQcInput([TestWells.Wt("C1")], []), calibration, Method);
        Assert.False(noLqc.IsSuccess);
        Assert.Equal(ErrorCodes.PlateRoleCardinality, noLqc.Errors.Single().Code);
    }

    // ---------------------------------------------------------------
    // Trace content (CLAUDE.md §13)
    // ---------------------------------------------------------------

    [Fact]
    public void CalculationTrace_captures_every_intermediate_in_method_order()
    {
        SampleAnalysisInput input = new([new SampleGroup("S1", [TestWells.AtDrPrime("E1", 0.6)])]);
        var (calibration, controls) = ReferenceStages();

        Result<SampleAnalysisResult> result = Engine.AnalyzeSamples(input, controls.WtReference!, calibration, Method);

        CalculationTrace trace = result.Value!.Results.Single().Wells.Single().Trace!;
        Assert.Equal(CalculationTrace.AlgorithmVersion, trace.Algorithm);
        Assert.Equal(Method.ComputeCanonicalHash(), trace.MethodSnapshotHash);
        Assert.Equal(TestWells.Id("E1"), trace.SourceWell);
        Assert.Equal(FluorescenceChannel.Fam, trace.SourceChannel);
        Assert.Equal(60, trace.SourcePointIndex); // 0,2,...,120 -> index of 120

        string[] keys = [.. trace.IntermediateValues.Select(v => v.Key)];
        Assert.Equal(
        [
            "F_FAM_120", "Fb_FAM", "Fm_FAM", "Y_FAM",
            "F_ROX_120", "Fb_ROX", "Fm_ROX", "Y_ROX",
            "WT_Y_FAM_mean", "WT_Y_ROX_mean",
        ], keys);

        // Hand-computed values: F = 100 + 0.6*900 = 640, Y = 0.6 exactly;
        // ROX at Y = 0: F = 50, Y = 0.
        Assert.Equal(640.0, trace.GetIntermediate("F_FAM_120"));
        Assert.Equal(100.0, trace.GetIntermediate("Fb_FAM"));
        Assert.Equal(1000.0, trace.GetIntermediate("Fm_FAM"));
        Assert.Equal(0.6, trace.GetIntermediate("Y_FAM")!.Value, 12);
        Assert.Equal(50.0, trace.GetIntermediate("F_ROX_120"));
        Assert.Equal(0.0, trace.GetIntermediate("Y_ROX"));
        Assert.Equal(0.0, trace.GetIntermediate("WT_Y_FAM_mean"));
        Assert.Equal(0.0, trace.GetIntermediate("WT_Y_ROX_mean"));

        Assert.Equal(">=", trace.ComparisonOperator);
        Assert.Equal(0.4, trace.ThresholdRaw);
        Assert.Equal(0.6, trace.ResultRaw!.Value, 12);
        Assert.Equal(QcStatus.Pass, trace.FinalStatus);
        Assert.Empty(trace.ErrorCodes);
    }

    // ---------------------------------------------------------------
    // NTC uses its own Fb before run averaging (CLAUDE.md §8.3)
    // ---------------------------------------------------------------

    [Fact]
    public void Each_ntc_well_is_normalized_with_its_own_fb()
    {
        // Two NTC wells with different flat levels: A1 at FAM 100, A2 at
        // FAM 118. With Fm_run = 1000: A1 Y = 0 everywhere; A2 Y = 18/900
        // = 0.02 everywhere (inside the band) -> both pass; Fb_run = 109.
        WellSignals ntc1 = TestWells.Flat("A1", 100, 50);
        WellSignals ntc2 = TestWells.Flat("A2", 118, 50);

        Result<CalibrationResult> result = Engine.Calibrate(new CalibrationInput([TestWells.Positive100("B1")], [ntc1, ntc2]), Method);

        Assert.Equal(QcStatus.Pass, result.Value!.QcStatus);
        Assert.Equal(109.0, result.Value.RunFb[FluorescenceChannel.Fam]);
        Assert.Equal(50.0, result.Value.RunFb[FluorescenceChannel.Rox]);
    }

    [Fact]
    public void Ntc_well_passes_only_through_its_own_normalization()
    {
        // A2: flat 127 RFU until 118 min, endpoint jumps to 170 (Fb_2 = 170).
        // Own-Fb QC: early points Y = (127 − 170)/(1000 − 170) = −0.0518,
        // outside ±0.05 -> A2 FAILS through its own normalization. Mean-first
        // normalization (Fb_mean = 135) would give A2 Y ≈ −0.0092 and mask
        // the failure — the engine must not do that (Method-Spec §7.3).
        double[] minutes = [.. Enumerable.Range(0, 60).Select(i => (double)(i * 2))];
        double[] fam = [.. Enumerable.Repeat(127.0, 60)];
        double[] rox = [.. Enumerable.Repeat(50.0, 60)];
        WellSignals ntc2 = TestWells.Well("A2", [.. fam, 170.0], [.. rox, 50.0], [.. minutes, 120.0]);

        WellSignals ntc1 = TestWells.Flat("A1", 100, 50);

        Result<CalibrationResult> result = Engine.Calibrate(new CalibrationInput([TestWells.Positive100("B1")], [ntc1, ntc2]), Method);

        Assert.True(result.IsSuccess);
        Assert.Equal(QcStatus.Fail, result.Value!.QcStatus);
        WellDiagnostic diagnostic = result.Value.Diagnostics.Single();
        Assert.Equal(TestWells.Id("A2"), diagnostic.Well);
        Assert.Equal(ErrorCodes.CalibrationNtcRangeExceeded, diagnostic.Code);
        // Run Fb never forms after a failing well.
        Assert.Empty(result.Value.RunFb);
    }

    [Fact]
    public void Empty_sample_groups_are_skipped()
    {
        SampleAnalysisInput input = new([new SampleGroup("S1", []), new SampleGroup("S2", [TestWells.AtDrPrime("E1", 0.6)])]);
        var (calibration, controls) = ReferenceStages();

        Result<SampleAnalysisResult> result = Engine.AnalyzeSamples(input, controls.WtReference!, calibration, Method);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Results);
        Assert.Equal("S2", result.Value.Results[0].SampleId);
    }

    // ---------------------------------------------------------------
    // Multi-replicate Fm: run mean over passing wells
    // ---------------------------------------------------------------

    [Fact]
    public void Run_fm_is_the_mean_of_all_passing_positive_wells()
    {
        // B1 FAM 1000, B2 FAM 900 -> Fm_run = 950. Both flat (RR = 0).
        WellSignals b1 = TestWells.Flat("B1", 1000, 500);
        WellSignals b2 = TestWells.Flat("B2", 900, 450);

        Result<CalibrationResult> result = Engine.Calibrate(new CalibrationInput([b1, b2], [TestWells.Ntc("A1")]), Method);

        Assert.Equal(QcStatus.Pass, result.Value!.QcStatus);
        Assert.Equal(950.0, result.Value.RunFm[FluorescenceChannel.Fam]);
        Assert.Equal(475.0, result.Value.RunFm[FluorescenceChannel.Rox]);
    }

    // ---------------------------------------------------------------
    // Affine invariance (GD-020) at the full-chain level
    // ---------------------------------------------------------------

    [Fact]
    public void Full_chain_is_bit_exact_when_every_rfu_is_scaled_by_8()
    {
        // Scaling every RFU of the reference run by 8 (a power of two:
        // exact in binary64) leaves Y, dr', and every verdict bit-for-bit
        // identical: Fm, Fb scale by 8, numerators and denominators by 8,
        // and (8a-8b)/(8c-8b) == (a-b)/(c-b) exactly.
        var (calibration, controls) = ReferenceStages();
        Result<SampleAnalysisResult> baseline = Engine.AnalyzeSamples(TestWells.ReferenceSampleInput(), controls.WtReference!, calibration, Method);
        WellDrPrimeResult baselineWell = baseline.Value!.Results.Single().Wells[0];

        Result<CalibrationResult> cal8 = Engine.Calibrate(ScaledCalibration(8), Method);
        Result<ControlQcResult> ctrl8 = Engine.EvaluateControls(ScaledControls(8), cal8.Value!, Method);
        Result<SampleAnalysisResult> samples8 = Engine.AnalyzeSamples(ScaledSamples(8), ctrl8.Value!.WtReference!, cal8.Value!, Method);

        Assert.Equal(QcStatus.Pass, cal8.Value!.QcStatus);
        Assert.Equal(QcStatus.Pass, ctrl8.Value!.QcStatus);
        WellDrPrimeResult scaledWell = samples8.Value!.Results.Single().Wells[0];
        Assert.Equal(baselineWell.DrPrime, scaledWell.DrPrime); // bit-exact
        Assert.Equal(baselineWell.Call, scaledWell.Call);
        Assert.Equal(baseline.Value!.Results.Single().Call, samples8.Value.Results.Single().Call);
        // Y values are invariant too (trace level).
        Assert.Equal(baselineWell.Trace!.GetIntermediate("Y_FAM"), scaledWell.Trace!.GetIntermediate("Y_FAM"));
    }

    [Fact]
    public void Full_chain_is_invariant_within_1e_12_when_every_rfu_is_scaled_by_10()
    {
        var (calibration, controls) = ReferenceStages();
        Result<SampleAnalysisResult> baseline = Engine.AnalyzeSamples(TestWells.ReferenceSampleInput(), controls.WtReference!, calibration, Method);
        double baselineDr = baseline.Value!.Results.Single().Wells[0].DrPrime!.Value;

        Result<CalibrationResult> cal10 = Engine.Calibrate(ScaledCalibration(10), Method);
        Result<ControlQcResult> ctrl10 = Engine.EvaluateControls(ScaledControls(10), cal10.Value!, Method);
        Result<SampleAnalysisResult> samples10 = Engine.AnalyzeSamples(ScaledSamples(10), ctrl10.Value!.WtReference!, cal10.Value!, Method);

        Assert.Equal(QcStatus.Pass, cal10.Value!.QcStatus);
        Assert.Equal(QcStatus.Pass, ctrl10.Value!.QcStatus);
        double scaledDr = samples10.Value!.Results.Single().Wells[0].DrPrime!.Value;
        Assert.Equal(baselineDr, scaledDr, 12);
        Assert.Equal(baseline.Value!.Results.Single().Call, samples10.Value.Results.Single().Call);
    }

    private static CalibrationInput ScaledCalibration(double k) => new(
        [Scale(TestWells.Positive100("B1"), k)],
        [Scale(TestWells.Ntc("A1"), k)]);

    private static ControlQcInput ScaledControls(double k) => new(
        [Scale(TestWells.Wt("C1"), k), Scale(TestWells.Wt("C2"), k)],
        [Scale(TestWells.AtDrPrime("D1", 0.6), k)]);

    private static SampleAnalysisInput ScaledSamples(double k) => new(
    [
        new SampleGroup("S1", [Scale(TestWells.AtDrPrime("E1", 0.6), k), Scale(TestWells.AtDrPrime("E2", 0.6), k)]),
    ]);

    /// <summary>Scales every RFU of every point of both channels of a well.</summary>
    private static WellSignals Scale(WellSignals well, double k)
    {
        static List<SignalPoint> ScaleSeries(SignalSeries series, double k)
        {
            List<SignalPoint> scaled = [];
            foreach (SignalPoint p in series.Points)
            {
                scaled.Add(new SignalPoint(p.Elapsed, p.Rfu * k));
            }

            return scaled;
        }

        return new WellSignals(well.Well, new Dictionary<FluorescenceChannel, SignalSeries>
        {
            [FluorescenceChannel.Fam] = SignalSeries.Create(well.Channels[FluorescenceChannel.Fam].RunId, well.Well, FluorescenceChannel.Fam, ScaleSeries(well.Channels[FluorescenceChannel.Fam], k)),
            [FluorescenceChannel.Rox] = SignalSeries.Create(well.Channels[FluorescenceChannel.Rox].RunId, well.Well, FluorescenceChannel.Rox, ScaleSeries(well.Channels[FluorescenceChannel.Rox], k)),
        });
    }

    /// <summary>Runs the reference calibration and control stages once.</summary>
    private static (CalibrationResult Calibration, ControlQcResult Controls) ReferenceStages()
    {
        Result<CalibrationResult> calibration = Engine.Calibrate(TestWells.ReferenceCalibrationInput(), Method);
        Assert.True(calibration.IsSuccess);
        Result<ControlQcResult> controls = Engine.EvaluateControls(TestWells.ReferenceControlInput(), calibration.Value!, Method);
        Assert.True(controls.IsSuccess);
        return (calibration.Value!, controls.Value!);
    }
}
