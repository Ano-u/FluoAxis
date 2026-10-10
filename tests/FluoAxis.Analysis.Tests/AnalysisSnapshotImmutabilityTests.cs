namespace FluoAxis.Analysis.Tests;

using FluoAxis.Analysis;
using FluoAxis.Domain;
using Xunit;

public sealed class AnalysisSnapshotImmutabilityTests
{
    [Fact]
    public void Duplicate_wells_are_not_silently_counted_as_replicates()
    {
        var engine = new BrafAnalysisEngine();
        WellSignals positive = TestWells.Positive100("B1");
        var result = engine.Calibrate(new CalibrationInput([positive, positive], [TestWells.Ntc("A1")]), BrafMethodSnapshot.Default);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.PlateRoleCardinality, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Incomplete_passed_calibration_is_structured_failure_in_both_downstream_stages()
    {
        var engine = new BrafAnalysisEngine();
        var incomplete = new CalibrationResult([], [], new Dictionary<FluorescenceChannel, double>(),
            new Dictionary<FluorescenceChannel, double>(), QcStatus.Pass, []);
        var control = engine.EvaluateControls(TestWells.ReferenceControlInput(), incomplete, BrafMethodSnapshot.Default);
        var sample = engine.AnalyzeSamples(TestWells.ReferenceSampleInput(), new WtReference(0, 0), incomplete, BrafMethodSnapshot.Default);
        Assert.False(control.IsSuccess);
        Assert.False(sample.IsSuccess);
        Assert.Equal(ErrorCodes.CalibrationInvalidDenominator, Assert.Single(control.Errors).Code);
        Assert.Equal(ErrorCodes.CalibrationInvalidDenominator, Assert.Single(sample.Errors).Code);
    }

    [Fact]
    public void Caller_cannot_replace_calibration_parameters_after_validation()
    {
        var engine = new BrafAnalysisEngine();
        CalibrationResult result = engine.Calibrate(TestWells.ReferenceCalibrationInput(), BrafMethodSnapshot.Default).Value!;
        var fm = Assert.IsAssignableFrom<IDictionary<FluorescenceChannel, double>>(result.RunFm);
        Assert.Throws<NotSupportedException>(() => fm[FluorescenceChannel.Fam] = 1);
        var positives = Assert.IsAssignableFrom<IList<PositiveWellResult>>(result.PositiveWells);
        Assert.Throws<NotSupportedException>(() => positives.Clear());
        Assert.Equal(1000, result.RunFm[FluorescenceChannel.Fam]);
    }

    [Fact]
    public void Sample_input_and_computed_trace_do_not_follow_callers_mutations()
    {
        var wells = new List<WellSignals> { TestWells.AtDrPrime("E1", 0.6) };
        var group = new SampleGroup("S-1", wells);
        var groups = new List<SampleGroup> { group };
        var input = new SampleAnalysisInput(groups);
        wells.Clear(); groups.Clear();
        var engine = new BrafAnalysisEngine();
        CalibrationResult cal = engine.Calibrate(TestWells.ReferenceCalibrationInput(), BrafMethodSnapshot.Default).Value!;
        ControlQcResult controls = engine.EvaluateControls(TestWells.ReferenceControlInput(), cal, BrafMethodSnapshot.Default).Value!;
        SampleResult sample = Assert.Single(engine.AnalyzeSamples(input, controls.WtReference!, cal, BrafMethodSnapshot.Default).Value!.Results);
        CalculationTrace trace = Assert.Single(sample.Wells).Trace!;
        var values = Assert.IsAssignableFrom<IList<KeyValuePair<string, double>>>(trace.IntermediateValues);
        Assert.Throws<NotSupportedException>(() => values[0] = new("F_FAM_120", 0));
        Assert.Equal(640, trace.GetIntermediate("F_FAM_120"));
        Assert.Equal(DrPrimeCall.Positive, sample.Direction);
    }
}
