namespace FluoAxis.Golden.Tests;

using System.Reflection;
using System.Text.Json;
using FluoAxis.Analysis;
using FluoAxis.Domain;
using Xunit;
using Oracle = FluoAxis.Golden.Tests.Reference.IndependentReferenceCalculator;

/// <summary>Synthetic, unapproved V1.1 evidence, independent of the retained V1.0 candidates.</summary>
public sealed class ChannelFmGoldenCandidateTests
{
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-000000000021");
    private static readonly BrafMethodSnapshot Method = new(version: "1.1");
    private static readonly Oracle.Method ReferenceMethod = new(RoxFmFromEndpoint: true);
    private static readonly BrafAnalysisEngine Engine = new();

    [Fact]
    public void Candidate_manifest_is_unapproved_and_bound_to_version_1_1()
    {
        Assembly assembly = typeof(ChannelFmGoldenCandidateTests).Assembly;
        string resource = Assert.Single(assembly.GetManifestResourceNames(), name => name.EndsWith("candidate-manifest-v1.1.json", StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resource)!;
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;
        Assert.Equal("candidate-unapproved", root.GetProperty("approvalStatus").GetString());
        Assert.Equal(Method.MethodId, root.GetProperty("methodId").GetString());
        Assert.Equal(Method.Version, root.GetProperty("methodVersion").GetString());
        Assert.Equal(Method.ComputeCanonicalHash(), root.GetProperty("methodSnapshotHash").GetString());
        Assert.Equal(Enumerable.Range(1, 11).Select(i => $"GD11-{i:000}"),
            root.GetProperty("cases").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
    }

    [Fact]
    public void GD11_001_large_rox_change_uses_endpoint_and_does_not_fail_plateau()
    {
        Oracle.Well positive = Positive([100, 250, 800]);
        (Oracle.Calibration reference, CalibrationResult production) = Calibrate([positive]);
        Assert.Equal(1000, reference.FmFam);
        Assert.Equal(800, reference.FmRox);
        Assert.Null(reference.PositiveRoxRelativeRange);
        PositiveWellResult rox = Assert.Single(production.PositiveWells, value => value.Channel == FluorescenceChannel.Rox);
        Assert.Null(rox.Plateau);
        Assert.Equal(800, rox.Fm);
        Assert.Equal(TimeSpan.FromMinutes(120), rox.Endpoint!.Value.Elapsed);
        Assert.Empty(rox.WindowPoints);

        Oracle.Calibration legacy = Oracle.Calibrate([positive], [Ntc()], new Oracle.Method());
        Assert.False(legacy.IsPass);
        Assert.Equal("CALIBRATION_PLATEAU_RANGE_EXCEEDED", legacy.Code);
        Result<CalibrationResult> legacyProduction = Engine.Calibrate(Input([positive]), new BrafMethodSnapshot(version: "1.0"));
        Assert.True(legacyProduction.IsSuccess);
        Assert.Equal(QcStatus.Fail, legacyProduction.Value!.QcStatus);
    }

    [Fact]
    public void GD11_002_rox_uses_only_endpoint_evidence_but_fam_still_requires_its_window()
    {
        Oracle.Well positive = Positive([100, 300, 800], [5400, 6300, 7215]);
        (_, CalibrationResult production) = Calibrate([positive]);
        Assert.Equal(800, production.RunFm[FluorescenceChannel.Rox]);
        PositiveWellResult rox = Assert.Single(production.PositiveWells, value => value.Channel == FluorescenceChannel.Rox);
        Assert.Equal(2, rox.EndpointPointIndex);
        Assert.Empty(rox.WindowPoints);
        Assert.Null(rox.Plateau);

        Oracle.Well endpointOnly = Positive([800], [7200]);
        Oracle.Calibration reference = Oracle.Calibrate([endpointOnly], [Ntc()], ReferenceMethod);
        Result<CalibrationResult> endpointOnlyProduction = Engine.Calibrate(Input([endpointOnly]), Method);
        Assert.Equal("CALIBRATION_INSUFFICIENT_WINDOW_POINTS", reference.Code);
        Assert.False(endpointOnlyProduction.IsSuccess);
        Assert.Equal(ErrorCodes.CalibrationInsufficientWindowPoints, Assert.Single(endpointOnlyProduction.Errors).Code);
    }

    [Fact]
    public void GD11_003_legacy_snapshot_preserves_rox_window_mean()
    {
        Oracle.Well positive = Positive([498, 500, 502]);
        (Oracle.Calibration reference, _) = Calibrate([positive]);
        Assert.Equal(502, reference.FmRox);
        Oracle.Calibration legacyReference = Oracle.Calibrate([positive], [Ntc()], new Oracle.Method());
        Result<CalibrationResult> legacy = Engine.Calibrate(Input([positive]), new BrafMethodSnapshot(version: "1.0"));
        Assert.True(legacy.IsSuccess);
        Assert.Equal(500, legacyReference.FmRox);
        Assert.Equal(legacyReference.FmRox, legacy.Value!.RunFm[FluorescenceChannel.Rox]);
    }

    [Fact]
    public void GD11_004_fam_still_requires_a_qualified_plateau()
    {
        Oracle.Well positive = Positive([100, 300, 800]) with { Fam = new Oracle.Series([5400, 6300, 7200], [990, 1000, 1010]) };
        Oracle.Calibration reference = Oracle.Calibrate([positive], [Ntc()], ReferenceMethod);
        Result<CalibrationResult> production = Engine.Calibrate(Input([positive]), Method);
        Assert.Equal("CALIBRATION_PLATEAU_RANGE_EXCEEDED", reference.Code);
        Assert.True(production.IsSuccess);
        Assert.Equal(QcStatus.Fail, production.Value!.QcStatus);
        Assert.All(production.Value.Diagnostics, diagnostic => Assert.Equal(FluorescenceChannel.Fam, diagnostic.Channel));
        Assert.Empty(production.Value.RunFm);
    }

    [Theory]
    [InlineData(7185.0, true)]
    [InlineData(7215.0, true)]
    [InlineData(7184.999, false)]
    [InlineData(7215.001, false)]
    public void GD11_005_rox_endpoint_tolerance_retains_closed_boundaries(double seconds, bool accepted)
    {
        Oracle.Well positive = Positive([100, 300, 800], [5400, 6300, seconds]);
        Oracle.Calibration reference = Oracle.Calibrate([positive], [Ntc()], ReferenceMethod);
        Result<CalibrationResult> production = Engine.Calibrate(Input([positive]), Method);
        Assert.Equal(accepted, reference.IsPass);
        Assert.Equal(accepted, production.IsSuccess);
        if (accepted)
        {
            Assert.Equal(800, production.Value!.RunFm[FluorescenceChannel.Rox]);
        }
        else
        {
            Assert.Equal("CALIBRATION_MISSING_ENDPOINT", reference.Code);
            Assert.Equal(ErrorCodes.CalibrationMissingEndpoint, Assert.Single(production.Errors).Code);
        }
    }

    [Fact]
    public void GD11_006_equal_distance_selects_the_later_rox_endpoint()
    {
        (_, CalibrationResult production) = Calibrate([Positive([100, 300, 700, 900], [5400, 6300, 7185, 7215])]);
        Assert.Equal(900, production.RunFm[FluorescenceChannel.Rox]);
        PositiveWellResult rox = Assert.Single(production.PositiveWells, value => value.Channel == FluorescenceChannel.Rox);
        Assert.Equal(3, rox.EndpointPointIndex);
        Assert.Equal(TimeSpan.FromSeconds(7215), rox.Endpoint!.Value.Elapsed);
    }

    [Fact]
    public void GD11_007_multiple_positive_endpoints_are_averaged_per_well()
    {
        Oracle.Well first = Positive([100, 300, 600]);
        Oracle.Well second = Positive([100, 300, 1000]) with { Id = "A2" };
        (Oracle.Calibration reference, CalibrationResult production) = Calibrate([first, second]);
        Assert.Equal(800, reference.FmRox);
        Assert.Equal(800, production.RunFm[FluorescenceChannel.Rox]);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(49)]
    public void GD11_008_endpoint_denominator_is_checked_before_normalization(double endpoint)
    {
        Oracle.Well positive = Positive([500, 500, endpoint]);
        Oracle.Calibration reference = Oracle.Calibrate([positive], [Ntc()], ReferenceMethod);
        Result<CalibrationResult> production = Engine.Calibrate(Input([positive]), Method);
        Assert.Equal("CALIBRATION_INVALID_DENOMINATOR", reference.Code);
        Assert.False(production.IsSuccess);
        Assert.Equal(ErrorCodes.CalibrationInvalidDenominator, Assert.Single(production.Errors).Code);
    }

    [Theory]
    [InlineData(298, 357.5, 0.4472135954999579, "Positive")]
    [InlineData(208, 132.5, 0.1414213562373095, "Negative")]
    public void GD11_009_endpoint_fm_propagates_through_both_axes_and_dr_prime(double fam, double rox, double distance, string direction)
    {
        Oracle.Well positive = Positive([100, 250, 800]);
        SampleResult sample = CompareChain(positive, Constant("H1", 118, 57.5), Constant("B2", fam, rox), Method, ReferenceMethod);
        Assert.Equal(distance, sample.MeanDrPrime!.Value, 14);
        Assert.Equal(direction == "Positive" ? Method.Classification.PositiveLabel : Method.Classification.NegativeLabel, sample.Call);
    }

    [Fact]
    public void GD11_010_method_versions_keep_distinct_classification_when_rox_normalization_crosses_threshold()
    {
        Oracle.Well positive = Positive([498, 500, 502]);
        Oracle.Well sample = Constant("B2", 100, 230);
        Oracle.Well wt = Constant("H1", 100, 50);
        SampleResult current = CompareChain(positive, wt, sample, Method, ReferenceMethod);
        SampleResult legacy = CompareChain(positive, wt, sample, new BrafMethodSnapshot(version: "1.0"), new Oracle.Method());
        Assert.Equal("BRAF-ENGINE-1.0", Assert.Single(legacy.Wells).Trace!.Algorithm);
        Assert.Equal("BRAF-ENGINE-1.1", Assert.Single(current.Wells).Trace!.Algorithm);
        Assert.Equal(180.0 / 452, current.MeanDrPrime);
        Assert.Equal(Method.Classification.NegativeLabel, current.Call);
        Assert.Equal(0.4, legacy.MeanDrPrime);
        Assert.Equal(Method.Classification.PositiveLabel, legacy.Call);
    }

    [Fact]
    public void GD11_011_deterministic_endpoint_sweep_matches_independent_full_chain()
    {
        var random = new Random(20261010);
        for (int i = 0; i < 128; i++)
        {
            double endpoint = 100 + random.NextDouble() * 1900;
            Oracle.Well positive = Positive([endpoint / 4, endpoint / 2, endpoint]);
            Oracle.Well sample = Constant("B2", 100 + random.NextDouble() * 900, 50 + random.NextDouble() * (endpoint - 50));
            _ = CompareChain(positive, Constant("H1", 100, 50), sample, Method, ReferenceMethod);
        }
    }

    private static SampleResult CompareChain(Oracle.Well positive, Oracle.Well wt, Oracle.Well sample, BrafMethodSnapshot method, Oracle.Method referenceMethod)
    {
        Oracle.Well lqc = Constant("H12", 640, 50);
        Oracle.Calibration referenceCalibration = Oracle.Calibrate([positive], [Ntc()], referenceMethod);
        Assert.True(referenceCalibration.IsPass);
        Result<CalibrationResult> calibration = Engine.Calibrate(Input([positive]), method);
        Assert.True(calibration.IsSuccess);
        AssertCalibration(referenceCalibration, calibration.Value!);
        Oracle.Controls referenceControls = Oracle.EvaluateControls([wt], [lqc], referenceCalibration, referenceMethod);
        Assert.True(referenceControls.IsPass);
        Result<ControlQcResult> controls = Engine.EvaluateControls(new ControlQcInput([ToProduction(wt)], [ToProduction(lqc)]), calibration.Value!, method);
        Assert.True(controls.IsSuccess);
        Assert.Equal(QcStatus.Pass, controls.Value!.QcStatus);
        Assert.Equal(referenceControls.WtFam!.Value, controls.Value.WtReference!.YFamMean, 14);
        Assert.Equal(referenceControls.WtRox!.Value, controls.Value.WtReference.YRoxMean, 14);
        Assert.Equal(referenceControls.LqcDrPrime[0], Assert.Single(controls.Value.LqcWells).DrPrime!.Value, 14);
        Oracle.Sample referenceSample = Oracle.AnalyzeSample([sample], referenceControls, referenceCalibration, referenceMethod);
        Result<SampleAnalysisResult> result = Engine.AnalyzeSamples(new SampleAnalysisInput([new SampleGroup("S1", [ToProduction(sample)])]), controls.Value.WtReference, calibration.Value!, method);
        Assert.True(result.IsSuccess);
        SampleResult actual = Assert.Single(result.Value!.Results);
        Assert.Equal(referenceSample.MeanDrPrime!.Value, actual.MeanDrPrime!.Value, 14);
        Assert.Equal(referenceSample.Direction == "Positive" ? method.Classification.PositiveLabel : method.Classification.NegativeLabel, actual.Call);
        return actual;
    }

    private static (Oracle.Calibration Reference, CalibrationResult Production) Calibrate(Oracle.Well[] positives)
    {
        Oracle.Calibration reference = Oracle.Calibrate(positives, [Ntc()], ReferenceMethod);
        Result<CalibrationResult> production = Engine.Calibrate(Input(positives), Method);
        Assert.True(production.IsSuccess);
        AssertCalibration(reference, production.Value!);
        return (reference, production.Value!);
    }

    private static void AssertCalibration(Oracle.Calibration reference, CalibrationResult production)
    {
        Assert.True(reference.IsPass);
        Assert.Equal(QcStatus.Pass, production.QcStatus);
        Assert.Equal(reference.FmFam, production.RunFm[FluorescenceChannel.Fam]);
        Assert.Equal(reference.FmRox, production.RunFm[FluorescenceChannel.Rox]);
        Assert.Equal(reference.FbFam, production.RunFb[FluorescenceChannel.Fam]);
        Assert.Equal(reference.FbRox, production.RunFb[FluorescenceChannel.Rox]);
    }

    private static Oracle.Well Positive(double[] rox, double[]? seconds = null)
    {
        double[] timeline = seconds ?? [5400, 6300, 7200];
        double[] fam = seconds is null ? [998, 1000, 1002] : Enumerable.Repeat(1000.0, timeline.Length).ToArray();
        return new Oracle.Well("A1", new Oracle.Series(timeline, fam), new Oracle.Series(timeline, rox));
    }

    private static Oracle.Well Ntc() => Constant("A12", 100, 50);

    private static Oracle.Well Constant(string id, double fam, double rox) => new(id,
        new Oracle.Series([0, 5400, 7200], [fam, fam, fam]), new Oracle.Series([0, 5400, 7200], [rox, rox, rox]));

    private static CalibrationInput Input(Oracle.Well[] positives) => new(positives.Select(ToProduction).ToArray(), [ToProduction(Ntc())]);

    private static WellSignals ToProduction(Oracle.Well source)
    {
        WellId id = WellId.Parse(source.Id);
        SignalSeries Series(FluorescenceChannel channel, Oracle.Series values) => SignalSeries.Create(
            RunId, id, channel, values.Seconds.Select((seconds, i) => new SignalPoint(TimeSpan.FromSeconds(seconds), values.Rfu[i])));
        return new WellSignals(id, new Dictionary<FluorescenceChannel, SignalSeries>
        {
            [FluorescenceChannel.Fam] = Series(FluorescenceChannel.Fam, source.Fam),
            [FluorescenceChannel.Rox] = Series(FluorescenceChannel.Rox, source.Rox),
        });
    }
}
