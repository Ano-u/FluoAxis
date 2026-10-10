namespace FluoAxis.Analysis.Tests;

using FluoAxis.Analysis;
using FluoAxis.Domain;
using Xunit;

public sealed class ChannelFmTests
{
    private static readonly BrafAnalysisEngine Engine = new();

    [Fact]
    public void New_method_uses_fam_window_mean_and_rox_endpoint_without_rox_plateau_qc()
    {
        WellSignals positive = TestWells.Well("B1", [100, 998, 1002], [50, 100, 600], [0, 90, 120]);
        CalibrationResult result = Engine.Calibrate(new([positive], [TestWells.Ntc("A1")]), BrafMethodSnapshot.Default).Value!;
        Assert.Equal(QcStatus.Pass, result.QcStatus);
        Assert.Equal(1000, result.RunFm[FluorescenceChannel.Fam]);
        Assert.Equal(600, result.RunFm[FluorescenceChannel.Rox]);
        PositiveWellResult fam = Assert.Single(result.PositiveWells, well => well.Channel == FluorescenceChannel.Fam);
        Assert.Equal(0.004, fam.Plateau!.RelativeRange);
        Assert.Equal("plateauMean", fam.FmSource);
        PositiveWellResult rox = Assert.Single(result.PositiveWells, well => well.Channel == FluorescenceChannel.Rox);
        Assert.Null(rox.Plateau);
        Assert.Empty(rox.WindowPoints);
        Assert.Equal("endpoint", rox.FmSource);
        Assert.Equal(QcStatus.Pass, rox.QcStatus);
        Assert.Equal(2, rox.EndpointPointIndex);
        Assert.Equal(600, rox.Endpoint!.Value.Rfu);
    }

    [Fact]
    public void Legacy_method_retains_rox_plateau_mean_and_failure()
    {
        var method = new BrafMethodSnapshot(version: "1.0");
        WellSignals flatEnough = TestWells.Well("B1", [100, 998, 1002], [50, 499, 501], [0, 90, 120]);
        CalibrationResult result = Engine.Calibrate(new([flatEnough], [TestWells.Ntc("A1")]), method).Value!;
        Assert.Equal(500, result.RunFm[FluorescenceChannel.Rox]);
        Assert.NotNull(Assert.Single(result.PositiveWells, well => well.Channel == FluorescenceChannel.Rox).Plateau);
        WellSignals drift = TestWells.Well("B1", [100, 998, 1002], [50, 100, 600], [0, 90, 120]);
        CalibrationResult failed = Engine.Calibrate(new([drift], [TestWells.Ntc("A1")]), method).Value!;
        Assert.Equal(QcStatus.Fail, failed.QcStatus);
        Assert.Empty(failed.RunFm);
        Assert.Equal(FluorescenceChannel.Rox, Assert.Single(failed.Diagnostics).Channel);
    }

    [Fact]
    public void Rox_prior_points_do_not_need_a_positive_plateau_mean_while_fam_still_needs_a_window()
    {
        WellSignals positive = TestWells.Well("B1", [1000, 1000], [-600, 600], [90, 120]);
        Assert.Equal(600, Engine.Calibrate(new([positive], [TestWells.Ntc("A1")]), BrafMethodSnapshot.Default).Value!.RunFm[FluorescenceChannel.Rox]);
        Assert.False(Engine.Calibrate(new([positive], [TestWells.Ntc("A1")]), new(version: "1.0")).IsSuccess);
        WellSignals noFamWindow = TestWells.Well("B1", [1000], [600], [120]);
        Assert.False(Engine.Calibrate(new([noFamWindow], [TestWells.Ntc("A1")]), BrafMethodSnapshot.Default).IsSuccess);
    }

    [Fact]
    public void Each_rox_endpoint_contributes_to_run_mean_even_when_prior_points_differ()
    {
        WellSignals first = TestWells.Well("B1", [998, 1002], [100, 600], [90, 120]);
        WellSignals second = TestWells.Well("B2", [900, 900], [900, 800], [90, 120]);
        CalibrationResult result = Engine.Calibrate(new([first, second], [TestWells.Ntc("A1")]), BrafMethodSnapshot.Default).Value!;
        Assert.Equal(950, result.RunFm[FluorescenceChannel.Fam]);
        Assert.Equal(700, result.RunFm[FluorescenceChannel.Rox]);
    }

    [Fact]
    public void Rox_endpoint_uses_inclusive_tolerance_and_later_tie_without_interpolation()
    {
        WellSignals positive = TestWells.Well("B1", [1000, 1000, 1000], [10, 500, 700], [90, 119.75, 120.25]);
        CalibrationResult result = Engine.Calibrate(new([positive], [TestWells.Ntc("A1")]), BrafMethodSnapshot.Default).Value!;
        Assert.Equal(700, result.RunFm[FluorescenceChannel.Rox]);
        Assert.Equal(TimeSpan.FromMinutes(120.25), Assert.Single(result.PositiveWells, well => well.Channel == FluorescenceChannel.Rox).Endpoint!.Value.Elapsed);
    }

    [Fact]
    public void Missing_rox_endpoint_and_nonpositive_denominator_remain_invalid()
    {
        WellSignals missing = TestWells.Well("B1", [1000, 1000], [600, 600], [90, 119.74]);
        Assert.Equal(ErrorCodes.CalibrationMissingEndpoint, Assert.Single(Engine.Calibrate(new([missing], [TestWells.Ntc("A1")]), BrafMethodSnapshot.Default).Errors).Code);
        WellSignals atBackground = TestWells.Well("B1", [1000, 1000], [600, 50], [90, 120]);
        Assert.Equal(ErrorCodes.CalibrationInvalidDenominator, Assert.Single(Engine.Calibrate(new([atBackground], [TestWells.Ntc("A1")]), BrafMethodSnapshot.Default).Errors).Code);
    }

    [Fact]
    public void Unknown_method_version_is_rejected_at_every_analysis_entry()
    {
        var unknown = new BrafMethodSnapshot(version: "future");
        CalibrationResult calibration = Engine.Calibrate(TestWells.ReferenceCalibrationInput(), BrafMethodSnapshot.Default).Value!;
        Assert.Equal(AnalysisErrorCodes.MethodVersionUnsupported, Assert.Single(Engine.Calibrate(TestWells.ReferenceCalibrationInput(), unknown).Errors).Code);
        Assert.Equal(AnalysisErrorCodes.MethodVersionUnsupported, Assert.Single(Engine.EvaluateControls(TestWells.ReferenceControlInput(), calibration, unknown).Errors).Code);
        Assert.Equal(AnalysisErrorCodes.MethodVersionUnsupported, Assert.Single(Engine.AnalyzeSamples(new([], QcStatus.Pass, true), new(0, 0), calibration, unknown).Errors).Code);
    }
}
