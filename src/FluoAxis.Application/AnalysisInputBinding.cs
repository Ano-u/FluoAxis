namespace FluoAxis.Application;

using FluoAxis.Analysis;
using FluoAxis.Contracts;
using FluoAxis.Domain;

/// <summary>
/// Deterministic conversion between the persisted workflow data of one run
/// and the deterministic Analysis engine's own input/result types. Keeping
/// this in one place means the engine is fed exactly the wells the plate
/// assignment declares, and every result written back is derived from the
/// engine's output rather than recomputed.
/// </summary>
internal static class AnalysisInputBinding
{
    /// <summary>
    /// Groups the run's persisted series by the role each well carries, in
    /// plate order. Wells with no assignment, or assigned
    /// <see cref="WellRole.Ignore"/>, never reach the engine.
    /// </summary>
    public static PreparedRoleGroups GroupByRole(
        IReadOnlyList<SignalSeries> signals,
        IReadOnlyList<WellAssignment> assignments)
    {
        Dictionary<WellId, SignalSeries>[] byChannel = BuildChannelIndex(signals);
        Dictionary<WellId, WellAssignment> roleByWell = [];
        foreach (WellAssignment assignment in assignments)
        {
            if (assignment.Role != WellRole.Ignore)
            {
                roleByWell[assignment.Well] = assignment;
            }
        }

        List<WellSignals> positive = [];
        List<WellSignals> ntc = [];
        List<WellSignals> wt = [];
        List<WellSignals> lqc = [];
        List<SampleMember> samples = [];

        foreach (WellId well in WellId.AllWells)
        {
            if (!roleByWell.TryGetValue(well, out WellAssignment? assignment))
            {
                continue;
            }

            // A well missing one of its channels cannot be analysed. The
            // import layer guarantees matched channel well sets, so this is a
            // defensive check rather than an expected path.
            if (!byChannel[0].TryGetValue(well, out SignalSeries? fam)
                || !byChannel[1].TryGetValue(well, out SignalSeries? rox))
            {
                continue;
            }

            var signalsForWell = new WellSignals(well, new Dictionary<FluorescenceChannel, SignalSeries>
            {
                [FluorescenceChannel.Fam] = fam,
                [FluorescenceChannel.Rox] = rox,
            });

            switch (assignment.Role)
            {
                case WellRole.Positive100:
                    positive.Add(signalsForWell);
                    break;
                case WellRole.Ntc:
                    ntc.Add(signalsForWell);
                    break;
                case WellRole.WildType:
                    wt.Add(signalsForWell);
                    break;
                case WellRole.Lqc01Percent:
                    lqc.Add(signalsForWell);
                    break;
                case WellRole.Sample:
                    if (assignment.Sample is { } sample)
                    {
                        samples.Add(new SampleMember(sample.SampleId, sample.ReplicateIndex, well, signalsForWell));
                    }

                    break;
                default:
                    break;
            }
        }

        // Sample groups are keyed only by SampleId and ordered by replicate
        // index, so the group the engine sees is deterministic regardless of
        // the order the assignment rows were persisted in.
        List<PreparedSampleGroup> groups = [];
        foreach (IGrouping<string, SampleMember> group in samples
            .GroupBy(member => member.SampleId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            WellSignals[] wells =
            [
                .. group
                    .OrderBy(member => member.ReplicateIndex)
                    .ThenBy(member => member.Well.ToString(), StringComparer.Ordinal)
                    .Select(member => member.Signals),
            ];
            groups.Add(new PreparedSampleGroup(group.Key, wells));
        }

        return new PreparedRoleGroups(positive, ntc, wt, lqc, groups);
    }

    /// <summary>Builds the calibration stage input from the role groups.</summary>
    public static CalibrationInput ToCalibrationInput(PreparedRoleGroups groups) =>
        new(groups.Positive100Wells, groups.NtcWells);

    /// <summary>Builds the control QC stage input from the role groups.</summary>
    public static ControlQcInput ToControlQcInput(PreparedRoleGroups groups) =>
        new(groups.WtWells, groups.LqcWells);

    /// <summary>Builds the sample analysis stage input from the role groups.</summary>
    public static SampleAnalysisInput ToSampleAnalysisInput(PreparedRoleGroups groups) =>
        new([.. groups.Samples.Select(group => new SampleGroup(group.SampleId, group.Wells))]);

    /// <summary>
    /// Reconstructs the calibration result the control and sample stages
    /// consume from what calibration persisted. The value is rebuilt exactly
    /// as the engine emitted it, so later stages never recompute a quantity
    /// the method computes once (Method-Spec §2).
    /// </summary>
    public static CalibrationResult Rehydrate(PersistedCalibration calibration) => new(
        positiveWells: [],
        ntcWells: [],
        runFm: ToChannelMap(calibration.RunFmFam, calibration.RunFmRox),
        runFb: ToChannelMap(calibration.RunFbFam, calibration.RunFbRox),
        qcStatus: ParseStatus(calibration.Status),
        diagnostics: [.. calibration.Diagnostics.Select(ToDiagnostic)]);

    /// <summary>Reconstructs the WT reference from what the control stage persisted.</summary>
    public static WtReference? RehydrateReference(PersistedControl control) =>
        control.WtYFamMean is { } fam && control.WtYRoxMean is { } rox ? new WtReference(fam, rox) : null;

    private static Dictionary<FluorescenceChannel, double> ToChannelMap(double? fam, double? rox)
    {
        Dictionary<FluorescenceChannel, double> map = [];
        if (fam is { } famValue)
        {
            map[FluorescenceChannel.Fam] = famValue;
        }

        if (rox is { } roxValue)
        {
            map[FluorescenceChannel.Rox] = roxValue;
        }

        return map;
    }

    private static Dictionary<WellId, SignalSeries>[] BuildChannelIndex(IReadOnlyList<SignalSeries> signals)
    {
        Dictionary<WellId, SignalSeries>[] byChannel =
        [
            new Dictionary<WellId, SignalSeries>(),
            new Dictionary<WellId, SignalSeries>(),
        ];
        foreach (SignalSeries series in signals)
        {
            byChannel[(int)series.Channel][series.Well] = series;
        }

        return byChannel;
    }

    internal static QcStatus ParseStatus(string status) => status switch
    {
        StatusNames.Pass => QcStatus.Pass,
        StatusNames.Fail => QcStatus.Fail,
        _ => QcStatus.Invalid,
    };

    internal static string ToWireStatus(QcStatus status) => status switch
    {
        QcStatus.Pass => StatusNames.Pass,
        QcStatus.Fail => StatusNames.Fail,
        _ => StatusNames.Invalid,
    };

    internal static WellDiagnostic ToDiagnostic(PersistedDiagnostic diagnostic) => new(
        WellId.Parse(diagnostic.Well),
        (FluorescenceChannel)diagnostic.Channel,
        diagnostic.Code,
        diagnostic.Message,
        diagnostic.Value);

    internal static PersistedDiagnostic ToPersisted(WellDiagnostic diagnostic) => new(
        diagnostic.Well.ToString(),
        (int)diagnostic.Channel,
        diagnostic.Code,
        diagnostic.Message,
        diagnostic.Value);

    private sealed record SampleMember(string SampleId, int ReplicateIndex, WellId Well, WellSignals Signals);
}

/// <summary>
/// One sample group of the plate with its replicate wells in plate order.
/// The Application layer builds these from the persisted well assignment;
/// the Analysis layer receives them as <see cref="SampleGroup"/>.
/// </summary>
internal sealed record PreparedSampleGroup
{
    public string SampleId { get; }
    public IReadOnlyList<WellSignals> Wells { get; }

    public PreparedSampleGroup(string sampleId, IReadOnlyList<WellSignals> wells)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sampleId);
        ArgumentNullException.ThrowIfNull(wells);

        SampleId = sampleId;
        Wells = wells;
    }
}

/// <summary>The run's wells, already grouped by the scientific role each plays.</summary>
internal sealed record PreparedRoleGroups(
    IReadOnlyList<WellSignals> Positive100Wells,
    IReadOnlyList<WellSignals> NtcWells,
    IReadOnlyList<WellSignals> WtWells,
    IReadOnlyList<WellSignals> LqcWells,
    IReadOnlyList<PreparedSampleGroup> Samples);
