namespace FluoAxis.Application.Tests;

using FluoAxis.Contracts;
using FluoAxis.Domain;

/// <summary>
/// A synthetic 96-well plate whose wells are engineered to satisfy (or, when
/// a flag is set, to violate) the frozen method rules, so the Application
/// workflow can be exercised end to end without real de-identified data.
/// <para>
/// The numbers here are constructed from the method's own normalized
/// definitions, not copied from any real dataset: a plate whose NTC stays at
/// its own endpoint, whose positive plateau is flat, whose WT sits at
/// Y = 0, and whose samples sit at chosen distances from the WT origin. The
/// expected run-level values are therefore derived, not asserted from a
/// recording.
/// </para>
/// </summary>
internal sealed record SyntheticPlate
{
    /// <summary>100% positive plateau is flat (RR well under 1%) unless disabled.</summary>
    public bool PositivePlateaus { get; init; } = true;

    /// <summary>One sample group carries a positive and a negative replicate.</summary>
    public bool MixedSampleReplicates { get; init; }

    /// <summary>When enabled, a second LQC replicate is placed on the plate and
    /// classified on the negative side to exercise the control-conflict path.</summary>
    public bool MixedLqcReplicates { get; init; }

    // Well layout: A1 = NTC, A2 = 100% positive, A3 = WT, A4 = LQC 0.1%,
    // B1/B2 = sample S-POS (both positive), C1/C2 = sample S-NEG (both negative).
    private static readonly WellId NtcWell = new('A', 1);
    private static readonly WellId PositiveWell = new('A', 2);
    private static readonly WellId WtWell = new('A', 3);
    private static readonly WellId LqcWell = new('A', 4);
    private static readonly WellId MixedLqcWell = new('A', 5);
    private static readonly WellId PosSample1 = new('B', 1);
    private static readonly WellId PosSample2 = new('B', 2);
    private static readonly WellId NegSample1 = new('C', 1);
    private static readonly WellId NegSample2 = new('C', 2);

    private static readonly WellId[] AllWells =
        [NtcWell, PositiveWell, WtWell, LqcWell, PosSample1, PosSample2, NegSample1, NegSample2];

    // Plate geometry chosen so the run-level means are round and checkable.
    private const double FbFam = 100.0;
    private const double FbRox = 50.0;
    private const double FmFam = 1000.0;
    private const double FmRox = 500.0;

    /// <summary>
    /// Expected run-level Fm_FAM: the mean of the positive well's own 90–120
    /// minute window, computed from the very points this fixture generates
    /// rather than assumed, so a change to the ripple shape cannot silently
    /// invalidate the expectation.
    /// </summary>
    public double ExpectedRunFmFam => PlateauMean(PositiveWell, FluorescenceChannel.Fam);

    /// <summary>Expected run-level Fb_FAM: the NTC well's own 120-minute endpoint.</summary>
    public double ExpectedRunFbFam => EndpointValue(NtcWell, FluorescenceChannel.Fam);

    private double PlateauMean(WellId well, FluorescenceChannel channel)
    {
        double[] window = [.. PointsFor(well, channel)
            .Where(point => point.Elapsed >= TimeSpan.FromMinutes(90) && point.Elapsed <= TimeSpan.FromMinutes(120))
            .Select(point => point.Rfu)];
        return window.Average();
    }

    private double EndpointValue(WellId well, FluorescenceChannel channel)
    {
        SignalPoint endpoint = PointsFor(well, channel)
            .First(point => point.Elapsed == TimeSpan.FromMinutes(120));
        return endpoint.Rfu;
    }

    public IReadOnlyList<SignalSeries> BuildSeries(Guid runId)
    {
        List<SignalSeries> series = [];
        foreach (WellId well in Wells())
        {
            foreach (FluorescenceChannel channel in new[] { FluorescenceChannel.Fam, FluorescenceChannel.Rox })
            {
                series.Add(SignalSeries.Create(runId, well, channel, PointsFor(well, channel)));
            }
        }

        return series;
    }

    /// <summary>
    /// The 0–120 min time course of one well/channel, built so the method's
    /// rules hold: NTC flat at its own endpoint, positive flat in 90–120,
    /// everything else at a chosen normalized endpoint value.
    /// </summary>
    private IEnumerable<SignalPoint> PointsFor(WellId well, FluorescenceChannel channel)
    {
        (double fb, double fm) = channel == FluorescenceChannel.Fam ? (FbFam, FmFam) : (FbRox, FmRox);

        // 61 points, every 2 minutes, matching the manuscript cadence. The
        // method never assumes this cadence; it is only how this fixture
        // happens to sample.
        for (int step = 0; step <= 60; step++)
        {
            double minutes = step * 2.0;
            double rfu = well == NtcWell
                ? NtcValue(fb, minutes)
                : well == PositiveWell
                    ? PositiveValue(fm, minutes)
                    : NormalizedValue(well, channel, fb, fm, minutes);

            yield return new SignalPoint(TimeSpan.FromMinutes(minutes), rfu);
        }
    }

    /// <summary>
    /// NTC: raw fluorescence that normalizes to exactly 0 across the whole
    /// course against its own Fb and the run Fm, so it passes the ±0.05 band.
    /// </summary>
    private static double NtcValue(double fb, double minutes)
    {
        // A tiny, sub-band drift keeps the series from being a flat line
        // while Y stays far inside the acceptance band.
        double y = 0.001 * Math.Sin(minutes / 20.0);
        return fb + (y * (FmFam - fb));
    }

    /// <summary>Positive: flat plateau in 90–120 min at the run-level Fm.</summary>
    private double PositiveValue(double fm, double minutes)
    {
        // Before the plateau the signal ramps; inside it, a sub-1% ripple
        // keeps RR passing while the mean stays at Fm.
        if (minutes < 90)
        {
            return fm * (0.5 + (0.5 * minutes / 90.0));
        }

        double ripple = PositivePlateaus ? 0.002 * Math.Sin(minutes) : 0.5 * Math.Sin(minutes);
        return fm * (1.0 + ripple);
    }

    /// <summary>
    /// Every other well sits at a chosen normalized endpoint value, reached
    /// at 120 min, so the engine recovers exactly that Y.
    /// </summary>
    private double NormalizedValue(WellId well, FluorescenceChannel channel, double fb, double fm, double minutes)
    {
        double y = TargetY(well, channel);
        double fraction = minutes / 120.0;
        return fb + (y * fraction * (fm - fb));
    }

    /// <summary>The normalized endpoint value each control or sample well targets.</summary>
    private double TargetY(WellId well, FluorescenceChannel channel)
    {
        if (well == WtWell)
        {
            // WT sits at the origin so dr' equals the raw (Y_FAM, Y_ROX) norm.
            return 0.0;
        }

        if (well == LqcWell)
        {
            // 0.6 along the FAM axis: dr' = 0.6 >= 0.4, so the LQC reads positive.
            return channel == FluorescenceChannel.Fam ? 0.6 : 0.0;
        }

        if (well == MixedLqcWell)
        {
            return channel == FluorescenceChannel.Fam ? 0.2 : 0.0;
        }

        if (well == PosSample1 || well == PosSample2)
        {
            // S-POS replicates: both at 0.7 on FAM, unless the mixed flag
            // flips the second replicate to the negative side.
            bool flip = MixedSampleReplicates && well == PosSample2;
            return channel == FluorescenceChannel.Fam ? (flip ? 0.2 : 0.7) : 0.0;
        }

        // S-NEG replicates: both at 0.2 on FAM, dr' = 0.2 < 0.4 -> negative.
        return channel == FluorescenceChannel.Fam ? 0.2 : 0.0;
    }

    private ChannelRegionMappingDto FamRegion() => new(
        ChannelNames.Fam,
        "FAM",
        headerRowIndex: 0,
        timeColumnIndex: 0,
        [.. Wells().Select((well, index) => new WellColumnMappingDto(index + 1, well.ToString()))]);

    private ChannelRegionMappingDto RoxRegion() => new(
        ChannelNames.Rox,
        "ROX",
        headerRowIndex: 0,
        timeColumnIndex: 0,
        [.. Wells().Select((well, index) => new WellColumnMappingDto(index + 1, well.ToString()))]);

    private static Guid Generated(Guid? requestId) => requestId ?? Guid.NewGuid();

    private static DateTimeOffset At(int minutes, int revision) =>
        new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero).AddMinutes(minutes + revision);

    public ConfirmMappingCommand MappingCommand(Guid runId, int revision, Guid? requestId = null) => new(
        Generated(requestId),
        runId,
        revision,
        "researcher",
        At(6, revision),
        FamRegion(),
        RoxRegion(),
        "synthetic-run.xlsx",
        "data/raw/synthetic-run.xlsx",
        new string('a', 64),
        2048,
        SourceFileDto.MediaTypeNames.Xlsx);

    public AssignWellsCommand AssignCommand(Guid runId, int revision, Guid? requestId = null) => new(
        Generated(requestId),
        runId,
        revision,
        "researcher",
        At(10, revision),
        [
            new WellAssignmentDto(NtcWell.ToString(), WellRoleNames.Ntc),
            new WellAssignmentDto(PositiveWell.ToString(), WellRoleNames.Positive100),
            new WellAssignmentDto(WtWell.ToString(), WellRoleNames.WildType),
            .. (MixedLqcReplicates
                ? new[]
                {
                    new WellAssignmentDto(LqcWell.ToString(), WellRoleNames.Lqc01Percent),
                    new WellAssignmentDto(MixedLqcWell.ToString(), WellRoleNames.Lqc01Percent),
                }
                : new[] { new WellAssignmentDto(LqcWell.ToString(), WellRoleNames.Lqc01Percent) }),
            new WellAssignmentDto(PosSample1.ToString(), WellRoleNames.Sample, sampleId: "S-POS", replicateIndex: 1),
            new WellAssignmentDto(PosSample2.ToString(), WellRoleNames.Sample, sampleId: "S-POS", replicateIndex: 2),
            new WellAssignmentDto(NegSample1.ToString(), WellRoleNames.Sample, sampleId: "S-NEG", replicateIndex: 1),
            new WellAssignmentDto(NegSample2.ToString(), WellRoleNames.Sample, sampleId: "S-NEG", replicateIndex: 2),
        ]);

    public RunCalibrationCommand CalibrationCommand(Guid runId, int revision, Guid? requestId = null) => new(
        requestId ?? Guid.NewGuid(), runId, revision, "researcher", At(20, revision));

    public RunControlQcCommand ControlCommand(Guid runId, int revision, Guid? requestId = null) => new(
        requestId ?? Guid.NewGuid(), runId, revision, "researcher", At(30, revision));

    public AnalyzeSamplesCommand AnalysisCommand(Guid runId, int revision, Guid? requestId = null) => new(
        requestId ?? Guid.NewGuid(), runId, revision, "researcher", At(40, revision));

    public static SyntheticPlate Passing() => new();

    private IReadOnlyList<WellId> Wells() => MixedLqcReplicates
        ? [.. AllWells, MixedLqcWell]
        : AllWells;
}
