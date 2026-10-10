namespace FluoAxis.Golden.Tests;

using System.Reflection;
using System.Text.Json;
using FluoAxis.Analysis;
using FluoAxis.Domain;
using FluoAxis.Golden.Tests.Reference;
using Xunit;
using Oracle = FluoAxis.Golden.Tests.Reference.IndependentReferenceCalculator;

public sealed class GoldenCandidateTests
{
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-000000000020");
    // Preserve the V1.0 candidate evidence and its historical expected values.
    private static readonly BrafMethodSnapshot Method = new(version: "1.0");
    private static readonly Oracle.Method ReferenceMethod = new();
    private static readonly BrafAnalysisEngine Engine = new();

    [Fact]
    public void Candidate_manifest_is_explicitly_unapproved_complete_and_bound_to_the_method()
    {
        Assembly assembly = typeof(GoldenCandidateTests).Assembly;
        string resourceName = Assert.Single(assembly.GetManifestResourceNames(), name => name.EndsWith("candidate-manifest-v1.json", StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resourceName)!;
        using JsonDocument manifest = JsonDocument.Parse(stream);
        JsonElement root = manifest.RootElement;

        Assert.Equal("candidate-unapproved", root.GetProperty("approvalStatus").GetString());
        Assert.Equal(Method.MethodId, root.GetProperty("methodId").GetString());
        Assert.Equal(Method.Version, root.GetProperty("methodVersion").GetString());
        string expectedHash = Method.ComputeCanonicalHash();
        string? manifestHash = root.GetProperty("methodSnapshotHash").GetString();
        Assert.True(string.Equals(expectedHash, manifestHash, StringComparison.Ordinal), $"Method hash mismatch. Expected: {expectedHash}; manifest: {manifestHash}");

        string[] expected = ["GD-001", "GD-002", "GD-003", "GD-004", "GD-005", "GD-006", "GD-007", "GD-008", "GD-009", "GD-010", "GD-011", "GD-012", "GD-018", "GD-019", "GD-020"];
        string[] actual = [.. root.GetProperty("cases").EnumerateArray().Select(item => item.GetProperty("id").GetString()!)];
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("GD-001", 0.60, "Positive")]
    [InlineData("GD-002", 0.35, "Negative")]
    [InlineData("GD-010", 0.40, "Positive")]
    public void Full_chain_candidate_matches_independent_oracle(string caseId, double sampleDistance, string expectedDirection)
    {
        _ = caseId;
        Candidate candidate = Standard(sampleDistance);
        ChainComparison comparison = RunBoth(candidate);

        AssertCalibrationEqual(comparison.ReferenceCalibration, comparison.ProductionCalibration);
        AssertControlsEqual(comparison.ReferenceControls, comparison.ProductionControls);
        AssertSampleEqual(comparison.ReferenceSample, comparison.ProductionSample);
        Assert.Equal(expectedDirection, comparison.ReferenceSample.Direction);
    }

    [Fact]
    public void GD_003_ntc_out_of_band_fails_in_both_implementations()
    {
        Candidate candidate = Standard(0.6) with { Ntcs = [Well("A1", [100, 200, 100, 100], [50, 50, 50, 50])] };
        AssertCalibrationFailure(candidate, "CALIBRATION_NTC_RANGE_EXCEEDED", expectedValid: true);
    }

    [Fact]
    public void GD_004_plateau_range_above_one_percent_fails_in_both_implementations()
    {
        Candidate candidate = Standard(0.6) with { Positives = [Well("B1", [1000, 990, 1000, 1010], [500, 495, 500, 505])] };
        AssertCalibrationFailure(candidate, "CALIBRATION_PLATEAU_RANGE_EXCEEDED", expectedValid: true);
    }

    [Fact]
    public void GD_005_wt_at_exclusive_limit_fails_in_both_implementations()
    {
        Candidate candidate = Standard(0.6) with { Wt = [AtY("C1", 0.05, 0)] };
        ChainComparison comparison = RunBoth(candidate, analyzeSample: false);

        Assert.True(comparison.ReferenceControls.IsValid);
        Assert.False(comparison.ReferenceControls.IsPass);
        Assert.Equal("CONTROL_WT_MEAN_EXCEEDED", comparison.ReferenceControls.Code);
        Assert.Equal(QcStatus.Fail, comparison.ProductionControls.QcStatus);
        Assert.Equal(ErrorCodes.ControlWtMeanExceeded, Assert.Single(comparison.ProductionControls.Diagnostics).Code);
    }

    [Fact]
    public void GD_006_negative_lqc_direction_fails_in_both_implementations()
    {
        Candidate candidate = Standard(0.6) with { Lqc = [AtY("D1", 0.39, 0)] };
        ChainComparison comparison = RunBoth(candidate, analyzeSample: false);

        Assert.True(comparison.ReferenceControls.IsValid);
        Assert.False(comparison.ReferenceControls.IsPass);
        Assert.Equal("CONTROL_LQC_DIRECTION_NEGATIVE", comparison.ReferenceControls.Code);
        Assert.Equal(QcStatus.Fail, comparison.ProductionControls.QcStatus);
        Assert.Equal(ErrorCodes.ControlLqcDirectionNegative, Assert.Single(comparison.ProductionControls.Diagnostics).Code);
    }

    [Fact]
    public void GD_007_mixed_sample_replicates_are_invalid_without_a_mean()
    {
        Candidate candidate = Standard(0.6) with { Samples = [AtY("E1", 0.35, 0), AtY("E2", 0.45, 0)] };
        ChainComparison comparison = RunBoth(candidate);

        AssertSampleEqual(comparison.ReferenceSample, comparison.ProductionSample);
        Assert.False(comparison.ReferenceSample.IsValid);
        Assert.Null(comparison.ProductionSample.Call);
        Assert.Null(comparison.ProductionSample.MeanDrPrime);
        Assert.Equal(ErrorCodes.SampleReplicateClassificationInconsistent, comparison.ProductionSample.ErrorCode);
    }

    [Theory]
    [InlineData("GD-008", 7184.9, false)]
    [InlineData("GD-018", 7185.0, true)]
    [InlineData("GD-019", 7184.9, false)]
    public void Endpoint_tolerance_candidates_match_closed_fifteen_second_boundary(string caseId, double endpointSeconds, bool accepted)
    {
        _ = caseId;
        double[] seconds = [0, 5400, 6300, endpointSeconds];
        Candidate candidate = Standard(0.6) with
        {
            Positives = [Well("B1", [1000, 1000, 1000, 1000], [500, 500, 500, 500], seconds)],
            Ntcs = [Well("A1", [100, 100, 100, 100], [50, 50, 50, 50], seconds)],
        };

        Oracle.Calibration reference = Oracle.Calibrate(candidate.Positives, candidate.Ntcs, ReferenceMethod);
        Result<CalibrationResult> production = Engine.Calibrate(ProductionCalibration(candidate), Method);
        Assert.Equal(accepted, reference.IsPass);
        Assert.Equal(accepted, production.IsSuccess);
        if (!accepted)
        {
            Assert.Equal("CALIBRATION_MISSING_ENDPOINT", reference.Code);
            Assert.Equal(ErrorCodes.CalibrationMissingEndpoint, Assert.Single(production.Errors).Code);
        }
    }

    [Fact]
    public void GD_009_non_positive_denominator_is_rejected_by_both_implementations()
    {
        Candidate candidate = Standard(0.6) with
        {
            Positives = [Well("B1", [100, 100, 100, 100], [50, 50, 50, 50])],
        };
        AssertCalibrationFailure(candidate, "CALIBRATION_INVALID_DENOMINATOR", expectedValid: false);
    }

    [Fact]
    public void GD_011_relative_range_exactly_one_percent_passes()
    {
        Candidate candidate = Standard(0.6) with { Positives = [Well("B1", [1000, 995, 1000, 1005], [500, 497.5, 500, 502.5])] };
        ChainComparison comparison = RunBoth(candidate);

        Assert.True(comparison.ReferenceCalibration.IsPass);
        Assert.Equal(0.01, comparison.ReferenceCalibration.PositiveFamRelativeRange!.Value, 14);
        Assert.Equal(0.01, comparison.ReferenceCalibration.PositiveRoxRelativeRange!.Value, 14);
        Assert.Equal(QcStatus.Pass, comparison.ProductionCalibration.QcStatus);
    }

    [Fact]
    public void GD_012_single_plateau_point_is_rejected_by_both_implementations()
    {
        double[] seconds = [0, 7200];
        Candidate candidate = Standard(0.6) with { Positives = [Well("B1", [1000, 1000], [500, 500], seconds)] };
        AssertCalibrationFailure(candidate, "CALIBRATION_INSUFFICIENT_WINDOW_POINTS", expectedValid: false);
    }

    [Fact]
    public void GD_020_tenfold_scale_preserves_normalized_values_and_dr_prime()
    {
        ChainComparison baseline = RunBoth(Standard(0.6));
        ChainComparison scaled = RunBoth(Scale(Standard(0.6), 10));

        AssertNullableEqual(baseline.ReferenceControls.WtFam, scaled.ReferenceControls.WtFam);
        AssertNullableEqual(baseline.ReferenceControls.WtRox, scaled.ReferenceControls.WtRox);
        AssertNullableEqual(baseline.ReferenceSample.MeanDrPrime, scaled.ReferenceSample.MeanDrPrime);
        AssertNullableEqual(baseline.ProductionSample.MeanDrPrime, scaled.ProductionSample.MeanDrPrime);
        Assert.Equal(baseline.ProductionSample.Call, scaled.ProductionSample.Call);
    }

    [Fact]
    public void Deterministic_two_dimensional_reference_sweep_matches_all_intermediates_and_calls()
    {
        var random = new Random(20261007);
        for (int vector = 0; vector < 512; vector++)
        {
            double wtFam = (random.NextDouble() - 0.5) * 0.06;
            double wtRox = (random.NextDouble() - 0.5) * 0.06;
            double sampleFam = random.NextDouble() - 0.2;
            double sampleRox = random.NextDouble() - 0.2;
            Candidate candidate = Standard(0.6) with
            {
                Wt = [AtY("C1", wtFam, wtRox), AtY("C2", wtFam, wtRox)],
                Samples = [AtY("E1", sampleFam, sampleRox), AtY("E2", sampleFam, sampleRox)],
            };
            ChainComparison comparison = RunBoth(Scale(candidate, 0.25 + random.NextDouble() * 200));
            AssertCalibrationEqual(comparison.ReferenceCalibration, comparison.ProductionCalibration);
            AssertControlsEqual(comparison.ReferenceControls, comparison.ProductionControls);
            AssertSampleEqual(comparison.ReferenceSample, comparison.ProductionSample);
        }
    }

    private static void AssertCalibrationFailure(Candidate candidate, string code, bool expectedValid)
    {
        Oracle.Calibration reference = Oracle.Calibrate(candidate.Positives, candidate.Ntcs, ReferenceMethod);
        Result<CalibrationResult> production = Engine.Calibrate(ProductionCalibration(candidate), Method);
        Assert.Equal(expectedValid, reference.IsValid);
        Assert.Equal(code, reference.Code);
        if (expectedValid)
        {
            Assert.True(production.IsSuccess);
            Assert.Equal(QcStatus.Fail, production.Value!.QcStatus);
            Assert.NotEmpty(production.Value.Diagnostics);
            Assert.All(production.Value.Diagnostics, diagnostic => Assert.Equal(code, diagnostic.Code));
        }
        else
        {
            Assert.False(production.IsSuccess);
            Assert.Equal(code, Assert.Single(production.Errors).Code);
        }
    }

    private static ChainComparison RunBoth(Candidate candidate, bool analyzeSample = true)
    {
        Oracle.Calibration referenceCalibration = Oracle.Calibrate(candidate.Positives, candidate.Ntcs, ReferenceMethod);
        Assert.True(referenceCalibration.IsPass);
        Oracle.Controls referenceControls = Oracle.EvaluateControls(candidate.Wt, candidate.Lqc, referenceCalibration, ReferenceMethod);

        Result<CalibrationResult> productionCalibrationResult = Engine.Calibrate(ProductionCalibration(candidate), Method);
        Assert.True(productionCalibrationResult.IsSuccess);
        CalibrationResult productionCalibration = productionCalibrationResult.Value!;
        Result<ControlQcResult> productionControlsResult = Engine.EvaluateControls(ProductionControls(candidate), productionCalibration, Method);
        Assert.True(productionControlsResult.IsSuccess);
        ControlQcResult productionControls = productionControlsResult.Value!;

        Oracle.Sample referenceSample = new(false, null, null, []);
        SampleResult productionSample = new("not-run", QcStatus.Invalid, null, null, []);
        if (analyzeSample)
        {
            Assert.True(referenceControls.IsPass);
            referenceSample = Oracle.AnalyzeSample(candidate.Samples, referenceControls, referenceCalibration, ReferenceMethod);
            Result<SampleAnalysisResult> productionSamples = Engine.AnalyzeSamples(
                new SampleAnalysisInput([new SampleGroup("S1", candidate.Samples.Select(ProductionWell).ToArray())]),
                productionControls.WtReference!,
                productionCalibration,
                Method);
            Assert.True(productionSamples.IsSuccess);
            productionSample = Assert.Single(productionSamples.Value!.Results);
        }

        return new(referenceCalibration, referenceControls, referenceSample, productionCalibration, productionControls, productionSample);
    }

    private static void AssertCalibrationEqual(Oracle.Calibration reference, CalibrationResult production)
    {
        Assert.True(reference.IsPass);
        Assert.Equal(QcStatus.Pass, production.QcStatus);
        AssertNullableEqual(reference.FmFam, production.RunFm[FluorescenceChannel.Fam]);
        AssertNullableEqual(reference.FmRox, production.RunFm[FluorescenceChannel.Rox]);
        AssertNullableEqual(reference.FbFam, production.RunFb[FluorescenceChannel.Fam]);
        AssertNullableEqual(reference.FbRox, production.RunFb[FluorescenceChannel.Rox]);
    }

    private static void AssertControlsEqual(Oracle.Controls reference, ControlQcResult production)
    {
        Assert.Equal(reference.IsPass ? QcStatus.Pass : QcStatus.Fail, production.QcStatus);
        AssertNullableEqual(reference.WtFam, production.WtReference!.YFamMean);
        AssertNullableEqual(reference.WtRox, production.WtReference.YRoxMean);
        Assert.Equal(reference.LqcDrPrime.Count, production.LqcWells.Count);
        for (int i = 0; i < reference.LqcDrPrime.Count; i++)
        {
            Assert.Equal(reference.LqcDrPrime[i], production.LqcWells[i].DrPrime!.Value, 14);
        }
    }

    private static void AssertSampleEqual(Oracle.Sample reference, SampleResult production)
    {
        Assert.Equal(reference.IsValid ? QcStatus.Pass : QcStatus.Invalid, production.Status);
        string? productionDirection = production.Call == Method.Classification.PositiveLabel ? "Positive" : production.Call == Method.Classification.NegativeLabel ? "Negative" : null;
        Assert.Equal(reference.Direction, productionDirection);
        AssertNullableEqual(reference.MeanDrPrime, production.MeanDrPrime);
        Assert.Equal(reference.PerWell.Count, production.Wells.Count);
        for (int i = 0; i < reference.PerWell.Count; i++)
        {
            Assert.Equal(reference.PerWell[i], production.Wells[i].DrPrime!.Value, 14);
        }
    }

    private static Candidate Standard(double sampleDistance) => new(
        [Well("B1", [1000, 1000, 1000, 1000], [500, 500, 500, 500])],
        [Well("A1", [100, 100, 100, 100], [50, 50, 50, 50])],
        [AtY("C1", 0, 0)],
        [AtY("D1", 0.6, 0)],
        [AtY("E1", sampleDistance, 0), AtY("E2", sampleDistance, 0)]);

    private static void AssertNullableEqual(double? expected, double? actual)
    {
        Assert.Equal(expected.HasValue, actual.HasValue);
        if (expected.HasValue)
        {
            Assert.Equal(expected.Value, actual!.Value, 14);
        }
    }

    private static Oracle.Well AtY(string id, double yFam, double yRox) =>
        Well(id, [100 + (900 * yFam), 100 + (900 * yFam), 100 + (900 * yFam), 100 + (900 * yFam)], [50 + (450 * yRox), 50 + (450 * yRox), 50 + (450 * yRox), 50 + (450 * yRox)]);

    private static Oracle.Well Well(string id, double[] fam, double[] rox, double[]? seconds = null)
    {
        double[] timeline = seconds ?? [0, 5400, 6300, 7200];
        return new Oracle.Well(id, new Oracle.Series(timeline, fam), new Oracle.Series(timeline, rox));
    }

    private static CalibrationInput ProductionCalibration(Candidate candidate) =>
        new(candidate.Positives.Select(ProductionWell).ToArray(), candidate.Ntcs.Select(ProductionWell).ToArray());

    private static ControlQcInput ProductionControls(Candidate candidate) =>
        new(candidate.Wt.Select(ProductionWell).ToArray(), candidate.Lqc.Select(ProductionWell).ToArray());

    private static WellSignals ProductionWell(Oracle.Well source)
    {
        WellId id = WellId.Parse(source.Id);
        SignalSeries Series(FluorescenceChannel channel, Oracle.Series values) => SignalSeries.Create(
            RunId, id, channel, values.Seconds.Select((seconds, index) => new SignalPoint(TimeSpan.FromSeconds(seconds), values.Rfu[index])));
        return new WellSignals(id, new Dictionary<FluorescenceChannel, SignalSeries>
        {
            [FluorescenceChannel.Fam] = Series(FluorescenceChannel.Fam, source.Fam),
            [FluorescenceChannel.Rox] = Series(FluorescenceChannel.Rox, source.Rox),
        });
    }

    private static Candidate Scale(Candidate source, double factor)
    {
        static Oracle.Series ScaleSeries(Oracle.Series series, double multiplier) => new(series.Seconds, [.. series.Rfu.Select(value => value * multiplier)]);
        static Oracle.Well ScaleWell(Oracle.Well well, double multiplier) => new(well.Id, ScaleSeries(well.Fam, multiplier), ScaleSeries(well.Rox, multiplier));
        return new Candidate(
            [.. source.Positives.Select(w => ScaleWell(w, factor))],
            [.. source.Ntcs.Select(w => ScaleWell(w, factor))],
            [.. source.Wt.Select(w => ScaleWell(w, factor))],
            [.. source.Lqc.Select(w => ScaleWell(w, factor))],
            [.. source.Samples.Select(w => ScaleWell(w, factor))]);
    }

    private sealed record Candidate(
        IReadOnlyList<Oracle.Well> Positives,
        IReadOnlyList<Oracle.Well> Ntcs,
        IReadOnlyList<Oracle.Well> Wt,
        IReadOnlyList<Oracle.Well> Lqc,
        IReadOnlyList<Oracle.Well> Samples);

    private sealed record ChainComparison(
        Oracle.Calibration ReferenceCalibration,
        Oracle.Controls ReferenceControls,
        Oracle.Sample ReferenceSample,
        CalibrationResult ProductionCalibration,
        ControlQcResult ProductionControls,
        SampleResult ProductionSample);
}
