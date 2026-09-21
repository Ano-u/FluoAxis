namespace FluoAxis.Analysis.Tests;

using FluoAxis.Analysis;
using FluoAxis.Domain;

/// <summary>
/// Shared fixture builder: dual-channel well signals with role assignment.
/// All numbers used across the suite are chosen to be hand-computable
/// (Fm_FAM = 1000, Fb_FAM = 100, denominator 900 etc.), per the test plan.
/// </summary>
public static class TestWells
{
    public static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    /// <summary>Default method snapshot (Method-Specification V1.0 appendix JSON).</summary>
    public static readonly BrafMethodSnapshot Method = BrafMethodSnapshot.Default;

    public static WellId Id(string well) => WellId.Parse(well);

    public static SignalPoint P(double minutes, double rfu) => new(TimeSpan.FromMinutes(minutes), rfu);

    /// <summary>
    /// Builds a two-channel well. FAM/ROX values are constant over the whole
    /// timeline unless overridden: a flat positive well, or a custom ramp.
    /// </summary>
    public static WellSignals Well(
        string well,
        IReadOnlyList<double> famValues,
        IReadOnlyList<double> roxValues,
        IReadOnlyList<double> minutes)
    {
        if (famValues.Count != minutes.Count || roxValues.Count != minutes.Count)
        {
            throw new ArgumentException("fam/rox/minutes lengths must match.");
        }

        List<SignalPoint> fam = [];
        List<SignalPoint> rox = [];
        for (int i = 0; i < minutes.Count; i++)
        {
            fam.Add(P(minutes[i], famValues[i]));
            rox.Add(P(minutes[i], roxValues[i]));
        }

        WellId id = Id(well);
        return new WellSignals(id, new Dictionary<FluorescenceChannel, SignalSeries>
        {
            [FluorescenceChannel.Fam] = SignalSeries.Create(RunId, id, FluorescenceChannel.Fam, fam),
            [FluorescenceChannel.Rox] = SignalSeries.Create(RunId, id, FluorescenceChannel.Rox, rox),
        });
    }

    /// <summary>Flat two-channel well (same RFU at every point).</summary>
    public static WellSignals Flat(string well, double famRfu, double roxRfu)
    {
        const int Points = 61; // 0..120 min at 2 min
        double[] minutes = [.. Enumerable.Range(0, Points).Select(i => (double)(i * 2))];
        return Well(well, [.. Enumerable.Repeat(famRfu, Points)], [.. Enumerable.Repeat(roxRfu, Points)], minutes);
    }

    /// <summary>
    /// A passing 100% positive well: FAM plateau 1000, ROX plateau 500,
    /// flat everywhere so RR = 0.
    /// </summary>
    public static WellSignals Positive100(string well) => Flat(well, 1000, 500);

    /// <summary>
    /// A passing NTC well: FAM 100, ROX 50, flat, so every full-course Y is
    /// exactly 0 (Fm_FAM_run = 1000, Fb_FAM = 100, denominator 900).
    /// </summary>
    public static WellSignals Ntc(string well) => Flat(well, 100, 50);

    /// <summary>
    /// WT well whose endpoint Y is (0.0, 0.0): FAM 100, ROX 50 (equal to Fb).
    /// </summary>
    public static WellSignals Wt(string well) => Flat(well, 100, 50);

    /// <summary>
    /// A well whose endpoint Y is (yFam, yRox): FAM = Fb_FAM + (yFam * 900),
    /// ROX = Fb_ROX + (yRox * 450) with the reference run (Fm = 1000/500,
    /// Fb = 100/50).
    /// </summary>
    public static WellSignals AtY(string well, double yFam, double yRox) =>
        Flat(well, 100 + (900 * yFam), 50 + (450 * yRox));

    /// <summary>
    /// LQC/sample well in the reference frame: dr' is the distance from
    /// (yFam, yRox) to the WT reference (0, 0).
    /// </summary>
    public static WellSignals AtDrPrime(string well, double drPrime)
    {
        // Place the point on the FAM axis: Y = (dr', 0) -> dr' = |yFam|.
        return AtY(well, drPrime, 0);
    }

    /// <summary>Standard passing calibration, controls, and sample inputs of the reference run.</summary>
    public static CalibrationInput ReferenceCalibrationInput() => new([Positive100("B1")], [Ntc("A1")]);

    public static ControlQcInput ReferenceControlInput() => new([Wt("C1"), Wt("C2")], [AtDrPrime("D1", 0.6)]);

    public static SampleAnalysisInput ReferenceSampleInput() => new(
    [
        new SampleGroup("S1", [AtDrPrime("E1", 0.6), AtDrPrime("E2", 0.6)]),
    ]);
}
