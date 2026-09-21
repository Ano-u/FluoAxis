namespace FluoAxis.Integration.Tests;

using FluoAxis.Contracts;
using FluoAxis.Domain;

/// <summary>
/// The same synthetic plate shape the Application workflow tests use,
/// repeated here so the integration project is self-contained. Everything is
/// derived from the method's normalized definitions, never from real data.
/// </summary>
internal static class PersistedPlate
{
    private static readonly WellId NtcWell = new('A', 1);
    private static readonly WellId PositiveWell = new('A', 2);
    private static readonly WellId WtWell = new('A', 3);
    private static readonly WellId LqcWell = new('A', 4);
    private static readonly WellId PosSample1 = new('B', 1);
    private static readonly WellId PosSample2 = new('B', 2);
    private static readonly WellId NegSample1 = new('C', 1);
    private static readonly WellId NegSample2 = new('C', 2);

    private static readonly WellId[] AllWells =
        [NtcWell, PositiveWell, WtWell, LqcWell, PosSample1, PosSample2, NegSample1, NegSample2];

    private const double FbFam = 100.0;
    private const double FbRox = 50.0;
    private const double FmFam = 1000.0;
    private const double FmRox = 500.0;

    public static IReadOnlyList<SignalSeries> BuildSeries(Guid runId)
    {
        List<SignalSeries> series = [];
        foreach (WellId well in AllWells)
        {
            foreach (FluorescenceChannel channel in new[] { FluorescenceChannel.Fam, FluorescenceChannel.Rox })
            {
                series.Add(SignalSeries.Create(runId, well, channel, PointsFor(well, channel)));
            }
        }

        return series;
    }

    private static IEnumerable<SignalPoint> PointsFor(WellId well, FluorescenceChannel channel)
    {
        (double fb, double fm) = channel == FluorescenceChannel.Fam ? (FbFam, FmFam) : (FbRox, FmRox);
        for (int step = 0; step <= 60; step++)
        {
            double minutes = step * 2.0;
            double rfu = well == NtcWell
                ? fb + (0.001 * Math.Sin(minutes / 20.0) * (FmFam - fb))
                : well == PositiveWell
                    ? PositiveValue(fm, minutes)
                    : fb + (TargetY(well, channel) * (minutes / 120.0) * (fm - fb));
            yield return new SignalPoint(TimeSpan.FromMinutes(minutes), rfu);
        }
    }

    private static double PositiveValue(double fm, double minutes) =>
        minutes < 90
            ? fm * (0.5 + (0.5 * minutes / 90.0))
            : fm * (1.0 + (0.002 * Math.Sin(minutes)));

    private static double TargetY(WellId well, FluorescenceChannel channel)
    {
        if (well == WtWell)
        {
            return 0.0;
        }

        if (well == LqcWell)
        {
            return channel == FluorescenceChannel.Fam ? 0.6 : 0.0;
        }

        if (well == PosSample1 || well == PosSample2)
        {
            return channel == FluorescenceChannel.Fam ? 0.7 : 0.0;
        }

        return channel == FluorescenceChannel.Fam ? 0.2 : 0.0;
    }

    private static ChannelRegionMappingDto Region(string channel) => new(
        channel,
        channel == ChannelNames.Fam ? "FAM" : "ROX",
        headerRowIndex: 0,
        timeColumnIndex: 0,
        [.. AllWells.Select((well, index) => new WellColumnMappingDto(index + 1, well.ToString()))]);
}

/// <summary>The workflow commands the integration test submits, built the way a frontend would.</summary>
internal static class PersistedCommands
{
    private static DateTimeOffset At(int minutes) => new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.Zero).AddMinutes(minutes);

    public static ConfirmMappingCommand Mapping(Guid runId, int expectedRevision) => new(
        Guid.NewGuid(), runId, expectedRevision, "researcher", At(5),
        PersistedPlateRegion(ChannelNames.Fam, "FAM"),
        PersistedPlateRegion(ChannelNames.Rox, "ROX"),
        "synthetic-run.xlsx", "data/raw/synthetic-run.xlsx", new string('a', 64), 2048,
        SourceFileDto.MediaTypeNames.Xlsx);

    public static AssignWellsCommand Assign(Guid runId, int expectedRevision) => new(
        Guid.NewGuid(), runId, expectedRevision, "researcher", At(10),
        [
            new WellAssignmentDto("A1", WellRoleNames.Ntc),
            new WellAssignmentDto("A2", WellRoleNames.Positive100),
            new WellAssignmentDto("A3", WellRoleNames.WildType),
            new WellAssignmentDto("A4", WellRoleNames.Lqc01Percent),
            new WellAssignmentDto("B1", WellRoleNames.Sample, sampleId: "S-POS", replicateIndex: 1),
            new WellAssignmentDto("B2", WellRoleNames.Sample, sampleId: "S-POS", replicateIndex: 2),
            new WellAssignmentDto("C1", WellRoleNames.Sample, sampleId: "S-NEG", replicateIndex: 1),
            new WellAssignmentDto("C2", WellRoleNames.Sample, sampleId: "S-NEG", replicateIndex: 2),
        ]);

    public static RunCalibrationCommand Calibration(Guid runId, int expectedRevision) =>
        new(Guid.NewGuid(), runId, expectedRevision, "researcher", At(20));

    public static RunControlQcCommand Control(Guid runId, int expectedRevision) =>
        new(Guid.NewGuid(), runId, expectedRevision, "researcher", At(30));

    public static AnalyzeSamplesCommand Analysis(Guid runId, int expectedRevision) =>
        new(Guid.NewGuid(), runId, expectedRevision, "researcher", At(40));

    private static ChannelRegionMappingDto PersistedPlateRegion(string channel, string sheetName)
    {
        string[] wells = ["A1", "A2", "A3", "A4", "B1", "B2", "C1", "C2"];
        return new ChannelRegionMappingDto(
            channel,
            sheetName,
            headerRowIndex: 0,
            timeColumnIndex: 0,
            [.. wells.Select((well, index) => new WellColumnMappingDto(index + 1, well))]);
    }
}
