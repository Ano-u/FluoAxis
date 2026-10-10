namespace FluoAxis.Golden.Tests.Reference;

/// <summary>
/// Deliberately standalone scalar implementation of the candidate Method.
/// This file must not reference FluoAxis.Analysis, FluoAxis.Domain, or their helpers.
/// </summary>
internal static class IndependentReferenceCalculator
{
    internal sealed record Series(double[] Seconds, double[] Rfu);

    internal sealed record Well(string Id, Series Fam, Series Rox);

    internal sealed record Method(
        double EndpointSeconds = 7200,
        double EndpointToleranceSeconds = 15,
        double PlateauStartSeconds = 5400,
        double PlateauEndSeconds = 7200,
        double RelativeRangeMax = 0.01,
        int MinWindowPoints = 2,
        double NtcMin = -0.05,
        double NtcMax = 0.05,
        double WtMaxExclusive = 0.05,
        double DrPrimeThresholdInclusive = 0.4,
        bool RoxFmFromEndpoint = false);

    internal sealed record Calibration(
        bool IsValid,
        bool IsPass,
        string? Code,
        double? FmFam,
        double? FmRox,
        double? FbFam,
        double? FbRox,
        double? PositiveFamRelativeRange = null,
        double? PositiveRoxRelativeRange = null);

    internal sealed record Controls(
        bool IsValid,
        bool IsPass,
        string? Code,
        double? WtFam,
        double? WtRox,
        IReadOnlyList<double> LqcDrPrime);

    internal sealed record Sample(bool IsValid, string? Direction, double? MeanDrPrime, IReadOnlyList<double> PerWell);

    internal static Calibration Calibrate(IReadOnlyList<Well> positives, IReadOnlyList<Well> ntcs, Method method)
    {
        var fmFam = new List<double>();
        var fmRox = new List<double>();
        double? firstFamRr = null;
        double? firstRoxRr = null;

        foreach (Well well in positives)
        {
            (bool foundFam, _) = Endpoint(well.Fam, method);
            (bool foundRox, _) = Endpoint(well.Rox, method);
            if (!foundFam || !foundRox)
            {
                return Invalid("CALIBRATION_MISSING_ENDPOINT");
            }

            (bool validFam, bool passFam, double famMean, double famRr) = Plateau(well.Fam, method);
            (bool validRox, bool passRox, double roxMean, double roxRr) = method.RoxFmFromEndpoint
                ? (true, true, Endpoint(well.Rox, method).Rfu, double.NaN)
                : Plateau(well.Rox, method);
            firstFamRr ??= famRr;
            if (!method.RoxFmFromEndpoint)
            {
                firstRoxRr ??= roxRr;
            }
            if (!validFam || !validRox)
            {
                return Invalid("CALIBRATION_INSUFFICIENT_WINDOW_POINTS");
            }

            if (!passFam || !passRox)
            {
                return new Calibration(true, false, "CALIBRATION_PLATEAU_RANGE_EXCEEDED", null, null, null, null, firstFamRr, firstRoxRr);
            }

            fmFam.Add(famMean);
            fmRox.Add(roxMean);
        }

        double runFmFam = Average(fmFam);
        double runFmRox = Average(fmRox);
        var fbFam = new List<double>();
        var fbRox = new List<double>();

        foreach (Well well in ntcs)
        {
            (bool famFound, double famEndpoint) = Endpoint(well.Fam, method);
            (bool roxFound, double roxEndpoint) = Endpoint(well.Rox, method);
            if (!famFound || !roxFound)
            {
                return Invalid("CALIBRATION_MISSING_ENDPOINT");
            }

            if (runFmFam - famEndpoint <= 0 || runFmRox - roxEndpoint <= 0)
            {
                return Invalid("CALIBRATION_INVALID_DENOMINATOR");
            }

            bool famPass = FullCoursePass(well.Fam, famEndpoint, runFmFam, method);
            bool roxPass = FullCoursePass(well.Rox, roxEndpoint, runFmRox, method);
            if (!famPass || !roxPass)
            {
                return new Calibration(true, false, "CALIBRATION_NTC_RANGE_EXCEEDED", runFmFam, runFmRox, null, null, firstFamRr, firstRoxRr);
            }

            fbFam.Add(famEndpoint);
            fbRox.Add(roxEndpoint);
        }

        return new Calibration(true, true, null, runFmFam, runFmRox, Average(fbFam), Average(fbRox), firstFamRr, firstRoxRr);
    }

    internal static Controls EvaluateControls(IReadOnlyList<Well> wt, IReadOnlyList<Well> lqc, Calibration calibration, Method method)
    {
        var wtFam = wt.Select(w => Normalize(w.Fam, calibration.FmFam!.Value, calibration.FbFam!.Value, method)).ToArray();
        var wtRox = wt.Select(w => Normalize(w.Rox, calibration.FmRox!.Value, calibration.FbRox!.Value, method)).ToArray();
        double meanFam = Average(wtFam);
        double meanRox = Average(wtRox);
        if (meanFam >= method.WtMaxExclusive || meanRox >= method.WtMaxExclusive)
        {
            return new Controls(true, false, "CONTROL_WT_MEAN_EXCEEDED", null, null, []);
        }

        double[] distances = lqc.Select(w => Distance(w, meanFam, meanRox, calibration, method)).ToArray();
        bool[] directions = distances.Select(d => d >= method.DrPrimeThresholdInclusive).ToArray();
        if (directions.Distinct().Count() > 1)
        {
            return new Controls(false, false, "CONTROL_LQC_REPLICATE_INCONSISTENT", meanFam, meanRox, distances);
        }

        bool pass = directions.All(x => x);
        return new Controls(true, pass, pass ? null : "CONTROL_LQC_DIRECTION_NEGATIVE", meanFam, meanRox, distances);
    }

    internal static Sample AnalyzeSample(IReadOnlyList<Well> wells, Controls controls, Calibration calibration, Method method)
    {
        double[] distances = wells.Select(w => Distance(w, controls.WtFam!.Value, controls.WtRox!.Value, calibration, method)).ToArray();
        bool[] directions = distances.Select(d => d >= method.DrPrimeThresholdInclusive).ToArray();
        if (directions.Distinct().Count() > 1)
        {
            return new Sample(false, null, null, distances);
        }

        return new Sample(true, directions[0] ? "Positive" : "Negative", Average(distances), distances);
    }

    private static Calibration Invalid(string code) => new(false, false, code, null, null, null, null);

    private static (bool Found, double Rfu) Endpoint(Series series, Method method)
    {
        int best = -1;
        double bestDelta = double.MaxValue;
        for (int i = 0; i < series.Seconds.Length; i++)
        {
            double delta = Math.Abs(series.Seconds[i] - method.EndpointSeconds);
            if (delta <= method.EndpointToleranceSeconds && (delta < bestDelta || (delta == bestDelta && series.Seconds[i] > series.Seconds[best])))
            {
                best = i;
                bestDelta = delta;
            }
        }

        return best < 0 ? (false, double.NaN) : (true, series.Rfu[best]);
    }

    private static (bool Valid, bool Pass, double Mean, double RelativeRange) Plateau(Series series, Method method)
    {
        double[] values = Enumerable.Range(0, series.Seconds.Length)
            .Where(i => series.Seconds[i] >= method.PlateauStartSeconds && series.Seconds[i] <= method.PlateauEndSeconds)
            .Select(i => series.Rfu[i])
            .ToArray();
        if (values.Length < method.MinWindowPoints)
        {
            return (false, false, double.NaN, double.NaN);
        }

        double mean = Average(values);
        double rr = (values.Max() - values.Min()) / mean;
        return (true, rr <= method.RelativeRangeMax, mean, rr);
    }

    private static bool FullCoursePass(Series series, double fb, double fm, Method method)
    {
        for (int i = 0; i < series.Seconds.Length; i++)
        {
            if (series.Seconds[i] < 0 || series.Seconds[i] > method.EndpointSeconds)
            {
                continue;
            }

            double y = (series.Rfu[i] - fb) / (fm - fb);
            if (y < method.NtcMin || y > method.NtcMax)
            {
                return false;
            }
        }

        return true;
    }

    private static double Normalize(Series series, double fm, double fb, Method method)
    {
        (bool found, double endpoint) = Endpoint(series, method);
        if (!found || fm - fb <= 0)
        {
            return double.NaN;
        }

        return (endpoint - fb) / (fm - fb);
    }

    private static double Distance(Well well, double wtFam, double wtRox, Calibration calibration, Method method)
    {
        double fam = Normalize(well.Fam, calibration.FmFam!.Value, calibration.FbFam!.Value, method);
        double rox = Normalize(well.Rox, calibration.FmRox!.Value, calibration.FbRox!.Value, method);
        double dx = fam - wtFam;
        double dy = rox - wtRox;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static double Average(IEnumerable<double> values)
    {
        double sum = 0;
        int count = 0;
        foreach (double value in values)
        {
            sum += value;
            count++;
        }

        return sum / count;
    }
}
