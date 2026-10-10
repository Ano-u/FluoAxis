namespace FluoAxis.Analysis.Tests;

using FluoAxis.Analysis;
using FluoAxis.Domain;
using Xunit;

public sealed class ScientificEvidenceTests
{
    [Fact]
    public void Engine_retains_actual_window_points_ntc_normalization_and_both_trace_endpoints()
    {
        var engine = new BrafAnalysisEngine();
        BrafMethodSnapshot method = BrafMethodSnapshot.Default;
        CalibrationResult calibration = engine.Calibrate(TestWells.ReferenceCalibrationInput(), method).Value!;
        PositiveWellResult positive = Assert.Single(calibration.PositiveWells, result => result.Channel == FluorescenceChannel.Fam);
        Assert.Equal(16, positive.WindowPoints.Count);
        Assert.Equal(45, positive.WindowPoints[0].PointIndex);
        Assert.Equal(60, positive.WindowPoints[^1].PointIndex);
        Assert.Equal(1000, positive.Endpoint!.Value.Rfu);
        Assert.All(positive.WindowPoints, point => Assert.Equal(1000, point.Point.Rfu));
        NtcWellResult ntc = Assert.Single(calibration.NtcWells, result => result.Channel == FluorescenceChannel.Fam);
        Assert.Equal(61, ntc.QcPoints.Count);
        Assert.Equal(900, ntc.Denominator);
        Assert.Equal(0, ntc.MinimumY); Assert.Equal(0, ntc.MaximumY);
        Assert.All(ntc.QcPoints, point => { Assert.Equal(0, point.Normalized); Assert.True(point.WithinBand); });
        ControlQcResult controls = engine.EvaluateControls(TestWells.ReferenceControlInput(), calibration, method).Value!;
        Assert.All(controls.WtWells, well => { Assert.Equal(100, well.FamEndpoint!.Value.Rfu); Assert.Equal(50, well.RoxEndpoint!.Value.Rfu); });
        CalculationTrace trace = Assert.Single(controls.LqcWells).Trace!;
        Assert.Equal([FluorescenceChannel.Fam, FluorescenceChannel.Rox], trace.SourcePoints.Select(point => point.Channel));
        Assert.All(trace.SourcePoints, point => Assert.Equal(60, point.PointIndex));
    }

    [Fact]
    public void Ntc_qc_evidence_uses_original_indices_after_negative_time_points()
    {
        WellSignals ntc = TestWells.Well("A1", [100, 200, 100, 100], [50, 50, 50, 50], [-2, 0, 90, 120]);
        CalibrationResult result = new BrafAnalysisEngine().Calibrate(new CalibrationInput([TestWells.Positive100("B1")], [ntc]), BrafMethodSnapshot.Default).Value!;
        NtcWellResult fam = Assert.Single(result.NtcWells, well => well.Channel == FluorescenceChannel.Fam);
        Assert.Equal(QcStatus.Fail, result.QcStatus);
        Assert.Equal([1, 2, 3], fam.QcPoints.Select(point => point.PointIndex));
        Assert.Equal(1, Assert.Single(fam.OutOfBandPoints).PointIndex);
        Assert.Equal(100.0 / 900.0, fam.MaximumY);
        Assert.Equal(fam.MaximumY, fam.QcPoints[0].Normalized);
        Assert.False(fam.QcPoints[0].WithinBand);
    }

    [Fact]
    public void Failed_wt_retains_its_evaluated_reference_but_does_not_publish_a_valid_reference()
    {
        var engine = new BrafAnalysisEngine();
        CalibrationResult calibration = engine.Calibrate(TestWells.ReferenceCalibrationInput(), BrafMethodSnapshot.Default).Value!;
        ControlQcResult control = engine.EvaluateControls(new ControlQcInput([TestWells.AtY("C1", 0.05, 0)], [TestWells.AtDrPrime("D1", 0.6)]), calibration, BrafMethodSnapshot.Default).Value!;
        Assert.Equal(QcStatus.Fail, control.QcStatus);
        Assert.Null(control.WtReference);
        Assert.Equal(0.05, control.EvaluatedWtReference!.YFamMean);
        Assert.Empty(control.LqcWells);
    }
}
