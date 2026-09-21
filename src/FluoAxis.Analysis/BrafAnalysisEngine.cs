namespace FluoAxis.Analysis;

using System.Globalization;
using System.Runtime.InteropServices;
using FluoAxis.Domain;

/// <summary>Entry points of the deterministic BRAF V600E analysis engine (plan.md §9.2).</summary>
public interface IAnalysisEngine
{
    Result<CalibrationResult> Calibrate(CalibrationInput input, BrafMethodSnapshot method);

    Result<ControlQcResult> EvaluateControls(ControlQcInput input, CalibrationResult calibration, BrafMethodSnapshot method);

    Result<SampleAnalysisResult> AnalyzeSamples(
        SampleAnalysisInput input,
        WtReference wtReference,
        CalibrationResult calibration,
        BrafMethodSnapshot method);
}

/// <summary>
/// Pure deterministic implementation of the BRAF V600E research method
/// (Method-Specification V1.0). The computation order of §2 is mandatory
/// and is never rearranged. All means are sequential sums (§3.7); scientific
/// invalidity is returned as structured results, never thrown.
/// </summary>
public sealed class BrafAnalysisEngine : IAnalysisEngine
{
    private static readonly FluorescenceChannel[] Channels = [FluorescenceChannel.Fam, FluorescenceChannel.Rox];

    /// <summary>Stage 1: 100% positive plateau QC, run Fm, NTC background QC, run Fb.</summary>
    public Result<CalibrationResult> Calibrate(CalibrationInput input, BrafMethodSnapshot method)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(method);

        AppError? cardinality = CheckCalibrationCardinality(input, method);
        if (cardinality is not null)
        {
            return Result<CalibrationResult>.Fail(cardinality);
        }

        List<WellDiagnostic> diagnostics = [];
        List<PositiveWellResult> positiveResults = [];
        List<NtcWellResult> ntcResults = [];

        // Step 1: every well/channel must carry a selectable endpoint before
        // any other computation (Method-Spec §4.3, fail closed; no interpolation).
        Dictionary<WellId, int>[] positiveEndpointIndices = AllocateIndices();
        AppError? endpointError = SelectAllEndpoints(input.Positive100Wells, method, positiveEndpointIndices);
        if (endpointError is not null)
        {
            return Result<CalibrationResult>.Fail(endpointError);
        }

        Dictionary<WellId, int>[] ntcEndpointIndices = AllocateIndices();
        endpointError = SelectAllEndpoints(input.NtcWells, method, ntcEndpointIndices);
        if (endpointError is not null)
        {
            return Result<CalibrationResult>.Fail(endpointError);
        }

        // Step 2: per-well plateau QC and Fm_i (Method-Spec §5).
        Dictionary<FluorescenceChannel, List<double>> fmContributions = [];
        bool plateauFailed = false;

        foreach (WellSignals well in input.Positive100Wells)
        {
            foreach (FluorescenceChannel channel in Channels)
            {
                SignalSeries series = well.Channels[channel];
                IReadOnlyList<SignalPoint> window = AnalysisMath.SelectWindow(series.Points, method);
                double[] values = new double[window.Count];
                for (int i = 0; i < window.Count; i++)
                {
                    values[i] = window[i].Rfu;
                }

                PlateauEvaluation plateau = AnalysisMath.EvaluatePlateau(values, method.Plateau.RelativeRangeMax, method.Plateau.MinWindowPoints);

                if (plateau.QcStatus == QcStatus.Invalid)
                {
                    return Result<CalibrationResult>.Fail(WellError(well.Well, channel, plateau.ErrorCode!, plateau.ErrorMessage!));
                }

                if (plateau.QcStatus == QcStatus.Fail)
                {
                    plateauFailed = true;
                    diagnostics.Add(new WellDiagnostic(
                        well.Well,
                        channel,
                        AnalysisErrorCodes.CalibrationPlateauRangeExceeded,
                        $"Plateau relative range exceeded the method maximum of {method.Plateau.RelativeRangeMax.ToString(CultureInfo.InvariantCulture)}.",
                        plateau.RelativeRange));
                }

                double? fm = plateau.QcStatus == QcStatus.Pass ? plateau.Mean : null;
                if (fm is not null)
                {
                    Collect(fmContributions, channel, fm.Value);
                }

                positiveResults.Add(new PositiveWellResult(well.Well, channel, plateau, fm, positiveEndpointIndices[(int)channel][well.Well]));
            }
        }

        // A failing plateau well fails the whole calibration and the well is
        // neither removed nor replaced (Method-Spec §5.5); the run-level Fm
        // never forms, so the NTC stage cannot run.
        if (plateauFailed)
        {
            return Result<CalibrationResult>.Ok(new CalibrationResult(
                positiveResults,
                ntcResults,
                EmptyChannelMap(),
                EmptyChannelMap(),
                QcStatus.Fail,
                diagnostics));
        }

        // Step 3: run-level Fm per channel = mean of passing wells (Method-Spec §5.6).
        Dictionary<FluorescenceChannel, double> runFm = MeanPerChannel(fmContributions);
        AppError? fmError = ValidateRunMeans(runFm, "Fm");
        if (fmError is not null)
        {
            return Result<CalibrationResult>.Fail(fmError);
        }

        // Step 4: NTC per-well Fb at endpoint + full-course Y QC against the
        // well's OWN Fb (Method-Spec §7).
        Dictionary<FluorescenceChannel, List<double>> fbContributions = [];
        bool ntcFailed = false;

        foreach (WellSignals well in input.NtcWells)
        {
            foreach (FluorescenceChannel channel in Channels)
            {
                SignalSeries series = well.Channels[channel];
                double fb = series.Points[ntcEndpointIndices[(int)channel][well.Well]].Rfu;

                if (!double.IsFinite(fb))
                {
                    return Result<CalibrationResult>.Fail(WellError(
                        well.Well,
                        channel,
                        ErrorCodes.CalibrationNonFiniteValue,
                        "The endpoint fluorescence is not finite."));
                }

                IReadOnlyList<SignalPoint> course = AnalysisMath.SelectFullCourse(series.Points, method);
                if (course.Count == 0)
                {
                    return Result<CalibrationResult>.Fail(WellError(
                        well.Well,
                        channel,
                        AnalysisErrorCodes.CalibrationNtcNoValidPoints,
                        $"No valid point inside the 0–{method.EndpointMinutes.ToString(CultureInfo.InvariantCulture)} min QC domain."));
                }

                double fmRun = runFm[channel];
                List<NtcPointDiagnostic> outOfBand = [];
                bool wellFailed = false;

                for (int i = 0; i < course.Count; i++)
                {
                    // Denominator checked before the division (Method-Spec §6.3).
                    double denominator = fmRun - fb;
                    if (!double.IsFinite(denominator) || denominator <= 0)
                    {
                        return Result<CalibrationResult>.Fail(WellError(
                            well.Well,
                            channel,
                            ErrorCodes.CalibrationInvalidDenominator,
                            "The normalization denominator Fm_run − Fb_i is not positive and finite (fail closed)."));
                    }

                    double y = (course[i].Rfu - fb) / denominator;
                    if (!double.IsFinite(y))
                    {
                        return Result<CalibrationResult>.Fail(WellError(
                            well.Well,
                            channel,
                            ErrorCodes.CalibrationNonFiniteValue,
                            $"The normalized value of point {i} is not finite."));
                    }

                    if (y < method.Ntc.NormalizedMin || y > method.Ntc.NormalizedMax)
                    {
                        wellFailed = true;
                        outOfBand.Add(new NtcPointDiagnostic(
                            i,
                            course[i].Elapsed.TotalMinutes,
                            y,
                            method.Ntc.NormalizedMin,
                            method.Ntc.NormalizedMax));
                    }
                }

                if (wellFailed)
                {
                    ntcFailed = true;
                    diagnostics.Add(new WellDiagnostic(
                        well.Well,
                        channel,
                        ErrorCodes.CalibrationNtcRangeExceeded,
                        $"A full-course point fell outside the NTC acceptance band [{method.Ntc.NormalizedMin.ToString(CultureInfo.InvariantCulture)}, {method.Ntc.NormalizedMax.ToString(CultureInfo.InvariantCulture)}].",
                        outOfBand[0].Y));
                }
                else
                {
                    Collect(fbContributions, channel, fb);
                }

                ntcResults.Add(new NtcWellResult(well.Well, channel, fb, ntcEndpointIndices[(int)channel][well.Well], course.Count, outOfBand));
            }
        }

        // Any NTC well/channel failure fails the calibration (Method-Spec
        // §7.5) and the run-level Fb never forms.
        if (ntcFailed)
        {
            return Result<CalibrationResult>.Ok(new CalibrationResult(
                positiveResults,
                ntcResults,
                runFm,
                EmptyChannelMap(),
                QcStatus.Fail,
                diagnostics));
        }

        // Step 5: run-level Fb per channel (Method-Spec §7.6).
        Dictionary<FluorescenceChannel, double> runFb = MeanPerChannel(fbContributions);
        AppError? fbError = ValidateRunMeans(runFb, "Fb");
        if (fbError is not null)
        {
            return Result<CalibrationResult>.Fail(fbError);
        }

        return Result<CalibrationResult>.Ok(new CalibrationResult(
            positiveResults,
            ntcResults,
            runFm,
            runFb,
            QcStatus.Pass,
            diagnostics));
    }

    /// <summary>Stage 2: WT negative-control QC and LQC direction QC.</summary>
    public Result<ControlQcResult> EvaluateControls(ControlQcInput input, CalibrationResult calibration, BrafMethodSnapshot method)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(calibration);
        ArgumentNullException.ThrowIfNull(method);

        if (calibration.QcStatus != QcStatus.Pass)
        {
            return Result<ControlQcResult>.Fail(StageError(
                AnalysisErrorCodes.ControlCalibrationNotPassed,
                "Control QC requires a calibration that passed; the run must be recalibrated."));
        }

        AppError? cardinality = CheckControlCardinality(input, method);
        if (cardinality is not null)
        {
            return Result<ControlQcResult>.Fail(cardinality);
        }

        // Run-level denominator check before any WT division
        // (Method-Spec §6.2/§6.4).
        AppError? denominatorError = ValidateRunDenominators(calibration);
        if (denominatorError is not null)
        {
            return Result<ControlQcResult>.Fail(denominatorError);
        }

        List<WellDiagnostic> diagnostics = [];
        List<WellEndpointY> wtResults = [];
        List<WellDrPrimeResult> lqcResults = [];

        // WT per-well endpoint Y with run-level Fm/Fb (Method-Spec §8).
        List<double> wtFam = [];
        List<double> wtRox = [];

        foreach (WellSignals well in input.WtWells)
        {
            EndpointYOutcome fam = ComputeEndpointY(well, FluorescenceChannel.Fam, calibration.RunFm[FluorescenceChannel.Fam], calibration.RunFb[FluorescenceChannel.Fam], method);
            if (fam.Error is not null)
            {
                return Result<ControlQcResult>.Fail(fam.Error);
            }

            EndpointYOutcome rox = ComputeEndpointY(well, FluorescenceChannel.Rox, calibration.RunFm[FluorescenceChannel.Rox], calibration.RunFb[FluorescenceChannel.Rox], method);
            if (rox.Error is not null)
            {
                return Result<ControlQcResult>.Fail(rox.Error);
            }

            wtFam.Add(fam.Y!.Value);
            wtRox.Add(rox.Y!.Value);
            wtResults.Add(new WellEndpointY(well.Well, fam.Y, rox.Y, fam.PointIndex, rox.PointIndex));
        }

        double meanYFamWt = AnalysisMath.Mean(CollectionsMarshal.AsSpan(wtFam))!.Value;
        double meanYRoxWt = AnalysisMath.Mean(CollectionsMarshal.AsSpan(wtRox))!.Value;

        if (meanYFamWt >= method.Wt.FamMeanMaxExclusive || meanYRoxWt >= method.Wt.RoxMeanMaxExclusive)
        {
            diagnostics.Add(new WellDiagnostic(
                input.WtWells[0].Well,
                meanYFamWt >= method.Wt.FamMeanMaxExclusive ? FluorescenceChannel.Fam : FluorescenceChannel.Rox,
                ErrorCodes.ControlWtMeanExceeded,
                $"The WT group mean reached the exclusive limit (FAM < {method.Wt.FamMeanMaxExclusive.ToString(CultureInfo.InvariantCulture)}, ROX < {method.Wt.RoxMeanMaxExclusive.ToString(CultureInfo.InvariantCulture)} is required).",
                meanYFamWt >= method.Wt.FamMeanMaxExclusive ? meanYFamWt : meanYRoxWt));

            // WT FAIL -> Run Invalid; the LQC stage never runs (Method-Spec
            // §8.3) and the WT reference coordinate is not published for
            // downstream dr' computation.
            return Result<ControlQcResult>.Ok(new ControlQcResult(
                wtResults,
                lqcResults,
                wtReference: null,
                qcStatus: QcStatus.Fail,
                diagnostics));
        }

        WtReference wtReference = new(meanYFamWt, meanYRoxWt);

        // LQC per-well dr' against the WT reference (Method-Spec §9).
        foreach (WellSignals well in input.LqcWells)
        {
            WellDrPrimeResult? drPrime = ComputeWellDrPrime(well, calibration, wtReference, method, out AppError? error);
            if (error is not null)
            {
                return Result<ControlQcResult>.Fail(error);
            }

            lqcResults.Add(drPrime!);
        }

        EvaluateReplicateDirection(lqcResults, out DrPrimeCall? direction, out bool mixed);

        if (mixed)
        {
            // Mixed LQC replicates: INVALID, never a mean-masked verdict
            // (Method-Spec §9.4 applied to the LQC group).
            diagnostics.Add(new WellDiagnostic(
                input.LqcWells[0].Well,
                FluorescenceChannel.Fam,
                AnalysisErrorCodes.ControlLqcReplicateInconsistent,
                "LQC replicate wells classified in mixed directions.",
                null));
        }
        else if (direction == DrPrimeCall.Negative)
        {
            diagnostics.Add(new WellDiagnostic(
                input.LqcWells[0].Well,
                FluorescenceChannel.Fam,
                ErrorCodes.ControlLqcDirectionNegative,
                "The LQC group direction is Negative; a 0.1% LQC must classify Positive (Method-Spec §9.7).",
                null));
        }

        QcStatus controlStatus = mixed ? QcStatus.Invalid : direction == DrPrimeCall.Negative ? QcStatus.Fail : QcStatus.Pass;

        return Result<ControlQcResult>.Ok(new ControlQcResult(
            wtResults,
            lqcResults,
            wtReference,
            controlStatus,
            diagnostics,
            lqcDirection: direction));
    }

    /// <summary>Stage 3: per-sample dr' classification with replicate consistency.</summary>
    public Result<SampleAnalysisResult> AnalyzeSamples(
        SampleAnalysisInput input,
        WtReference wtReference,
        CalibrationResult calibration,
        BrafMethodSnapshot method)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(wtReference);
        ArgumentNullException.ThrowIfNull(calibration);
        ArgumentNullException.ThrowIfNull(method);

        if (calibration.QcStatus != QcStatus.Pass)
        {
            return Result<SampleAnalysisResult>.Fail(StageError(
                AnalysisErrorCodes.ControlCalibrationNotPassed,
                "Sample analysis requires a calibration that passed."));
        }

        // Defense in depth beyond the Application state machine: a sample
        // dr' must never be measured from a control stage that did not pass,
        // because a failed or invalid control publishes no trustworthy WT
        // reference coordinate (Method-Spec §8.3/§9.7).
        if (input.ControlStatus != QcStatus.Pass || !input.HasWtReference)
        {
            return Result<SampleAnalysisResult>.Fail(StageError(
                AnalysisErrorCodes.ControlWtReferenceUnavailable,
                "Sample analysis requires a control stage that passed and published a WT reference coordinate."));
        }

        AppError? denominatorError = ValidateRunDenominators(calibration);
        if (denominatorError is not null)
        {
            return Result<SampleAnalysisResult>.Fail(denominatorError);
        }

        List<SampleResult> results = [];

        foreach (SampleGroup group in input.Samples)
        {
            // Empty sample groups are skipped entirely (Method-Spec §10.2).
            if (group.Wells.Count == 0)
            {
                continue;
            }

            List<WellDrPrimeResult> wells = [];
            foreach (WellSignals well in group.Wells)
            {
                WellDrPrimeResult? drPrime = ComputeWellDrPrime(well, calibration, wtReference, method, out AppError? error);
                if (error is not null)
                {
                    return Result<SampleAnalysisResult>.Fail(error);
                }

                wells.Add(drPrime!);
            }

            EvaluateReplicateDirection(wells, out DrPrimeCall? direction, out bool mixed);

            if (mixed)
            {
                // Mixed directions: INVALID, no group call, no mean dr'
                // (Method-Spec §9.4; the mean may never mask disagreement).
                results.Add(new SampleResult(group.SampleId, QcStatus.Invalid, call: null, meanDrPrime: null, wells, ErrorCodes.SampleReplicateClassificationInconsistent));
                continue;
            }

            string call = direction == DrPrimeCall.Positive
                ? method.Classification.PositiveLabel
                : method.Classification.NegativeLabel;

            double[] drPrimes = [.. wells.Select(w => w.DrPrime!.Value)];
            double? mean = AnalysisMath.Mean(drPrimes);

            results.Add(new SampleResult(group.SampleId, QcStatus.Pass, call, mean, wells, errorCode: null, direction: direction));
        }

        return Result<SampleAnalysisResult>.Ok(new SampleAnalysisResult(results));
    }

    private static AppError? CheckCalibrationCardinality(CalibrationInput input, BrafMethodSnapshot method)
    {
        if (input.NtcWells.Count < method.Roles.NtcMinimumWells)
        {
            return CardinalityError("NTC", input.NtcWells.Count, method.Roles.NtcMinimumWells);
        }

        if (input.Positive100Wells.Count < method.Roles.Positive100MinimumWells)
        {
            return CardinalityError("100% positive", input.Positive100Wells.Count, method.Roles.Positive100MinimumWells);
        }

        return null;
    }

    private static AppError? CheckControlCardinality(ControlQcInput input, BrafMethodSnapshot method)
    {
        if (input.WtWells.Count < method.Roles.WildTypeMinimumWells)
        {
            return CardinalityError("WT", input.WtWells.Count, method.Roles.WildTypeMinimumWells);
        }

        if (input.LqcWells.Count < method.Roles.Lqc01PercentMinimumWells)
        {
            return CardinalityError("LQC", input.LqcWells.Count, method.Roles.Lqc01PercentMinimumWells);
        }

        return null;
    }

    private static AppError CardinalityError(string role, int actual, int required) =>
        new(
            ErrorCodes.PlateRoleCardinality,
            ErrorSeverity.Blocking,
            $"The plate provides {actual} {role} well(s); the method requires at least {required} (fail closed, no substitution).");

    private static AppError WellError(WellId well, FluorescenceChannel channel, string code, string message) =>
        new(code, ErrorSeverity.Blocking, $"Well {well} channel {channel}: {message}");

    private static AppError StageError(string code, string message) =>
        new(code, ErrorSeverity.Blocking, message);

    /// <summary>Validates Fm_run − Fb_run &gt; 0 and finite for both channels (Method-Spec §6.2).</summary>
    private static AppError? ValidateRunDenominators(CalibrationResult calibration)
    {
        foreach (FluorescenceChannel channel in Channels)
        {
            double denominator = calibration.RunFm[channel] - calibration.RunFb[channel];
            if (!double.IsFinite(denominator) || denominator <= 0)
            {
                return StageError(
                    ErrorCodes.CalibrationInvalidDenominator,
                    $"Channel {channel}: the run-level denominator Fm_run − Fb_run is not positive and finite.");
            }
        }

        return null;
    }

    private static EndpointYOutcome ComputeEndpointY(
        WellSignals well,
        FluorescenceChannel channel,
        double runFm,
        double runFb,
        BrafMethodSnapshot method)
    {
        SignalSeries series = well.Channels[channel];
        EndpointSelection endpoint = AnalysisMath.SelectEndpoint(series.Points, method);
        if (!endpoint.IsFound)
        {
            return EndpointYOutcome.Failed(endpoint.Error!);
        }

        double f = endpoint.Point!.Value.Rfu;
        if (!double.IsFinite(f))
        {
            return EndpointYOutcome.Failed(WellError(
                well.Well,
                channel,
                ErrorCodes.CalibrationNonFiniteValue,
                "The endpoint fluorescence is not finite."));
        }

        // Denominator checked before the division (Method-Spec §6.3).
        double denominator = runFm - runFb;
        if (!double.IsFinite(denominator) || denominator <= 0)
        {
            return EndpointYOutcome.Failed(WellError(
                well.Well,
                channel,
                ErrorCodes.CalibrationInvalidDenominator,
                "The run-level normalization denominator Fm_run − Fb_run is not positive and finite."));
        }

        double y = (f - runFb) / denominator;
        if (!double.IsFinite(y))
        {
            return EndpointYOutcome.Failed(WellError(
                well.Well,
                channel,
                ErrorCodes.CalibrationNonFiniteValue,
                "The normalized endpoint value is not finite."));
        }

        return EndpointYOutcome.Computed(y, f, endpoint.PointIndex!.Value);
    }

    /// <summary>Per-well dr' with single-well classification and full trace (Method-Spec §9.1–§9.3).</summary>
    private static WellDrPrimeResult? ComputeWellDrPrime(
        WellSignals well,
        CalibrationResult calibration,
        WtReference wtReference,
        BrafMethodSnapshot method,
        out AppError? error)
    {
        EndpointYOutcome fam = ComputeEndpointY(well, FluorescenceChannel.Fam, calibration.RunFm[FluorescenceChannel.Fam], calibration.RunFb[FluorescenceChannel.Fam], method);
        if (fam.Error is not null)
        {
            error = fam.Error;
            return null;
        }

        EndpointYOutcome rox = ComputeEndpointY(well, FluorescenceChannel.Rox, calibration.RunFm[FluorescenceChannel.Rox], calibration.RunFb[FluorescenceChannel.Rox], method);
        if (rox.Error is not null)
        {
            error = rox.Error;
            return null;
        }

        double drPrime = AnalysisMath.Distance2D(fam.Y!.Value, rox.Y!.Value, wtReference.YFamMean, wtReference.YRoxMean);

        if (!double.IsFinite(drPrime))
        {
            error = WellError(
                well.Well,
                FluorescenceChannel.Fam,
                ErrorCodes.CalibrationNonFiniteValue,
                "dr' is not finite.");
            return null;
        }

        DrPrimeCall call = drPrime >= method.Classification.DrPrimeThresholdInclusive ? DrPrimeCall.Positive : DrPrimeCall.Negative;

        CalculationTrace trace = new(
            traceId: Guid.NewGuid(),
            algorithm: CalculationTrace.AlgorithmVersion,
            methodSnapshotHash: method.ComputeCanonicalHash(),
            sourceWell: well.Well,
            sourceChannel: FluorescenceChannel.Fam,
            sourcePointIndex: fam.PointIndex!.Value,
            intermediateValues:
            [
                new("F_FAM_120", fam.RawEndpoint!.Value),
                new("Fb_FAM", calibration.RunFb[FluorescenceChannel.Fam]),
                new("Fm_FAM", calibration.RunFm[FluorescenceChannel.Fam]),
                new("Y_FAM", fam.Y.Value),
                new("F_ROX_120", rox.RawEndpoint!.Value),
                new("Fb_ROX", calibration.RunFb[FluorescenceChannel.Rox]),
                new("Fm_ROX", calibration.RunFm[FluorescenceChannel.Rox]),
                new("Y_ROX", rox.Y.Value),
                new("WT_Y_FAM_mean", wtReference.YFamMean),
                new("WT_Y_ROX_mean", wtReference.YRoxMean),
            ],
            comparisonOperator: ">=",
            thresholdRaw: method.Classification.DrPrimeThresholdInclusive,
            resultRaw: drPrime,
            finalStatus: QcStatus.Pass,
            errorCodes: []);

        error = null;
        return new WellDrPrimeResult(well.Well, drPrime, call, trace);
    }

    /// <summary>
    /// Replicate direction consistency (Method-Spec §9.4): all Positive or
    /// all Negative passes; a mixed group is INVALID with no mean and no
    /// call. The rule is the frozen scientific invariant (CLAUDE.md
    /// invariant 6); a mixed group never yields a group verdict, whatever
    /// presentation the caller builds on top.
    /// </summary>
    private static void EvaluateReplicateDirection(
        List<WellDrPrimeResult> wells,
        out DrPrimeCall? direction,
        out bool mixed)
    {
        direction = wells[0].Call;
        mixed = false;

        foreach (WellDrPrimeResult well in wells)
        {
            if (well.Call != direction)
            {
                direction = null;
                mixed = true;
                return;
            }
        }
    }

    private static Dictionary<WellId, int>[] AllocateIndices() =>
        [
            new Dictionary<WellId, int>(),
            new Dictionary<WellId, int>(),
        ];

    /// <summary>Selects and records the endpoint index of every well/channel; any missing endpoint fails the stage.</summary>
    private static AppError? SelectAllEndpoints(IReadOnlyList<WellSignals> wells, BrafMethodSnapshot method, Dictionary<WellId, int>[] indices)
    {
        foreach (WellSignals well in wells)
        {
            foreach (FluorescenceChannel channel in Channels)
            {
                EndpointSelection endpoint = AnalysisMath.SelectEndpoint(well.Channels[channel].Points, method);
                if (!endpoint.IsFound)
                {
                    return endpoint.Error!;
                }

                indices[(int)channel][well.Well] = endpoint.PointIndex!.Value;
            }
        }

        return null;
    }

    private static Dictionary<FluorescenceChannel, double> MeanPerChannel(Dictionary<FluorescenceChannel, List<double>> contributions)
    {
        Dictionary<FluorescenceChannel, double> means = [];
        foreach (KeyValuePair<FluorescenceChannel, List<double>> pair in contributions)
        {
            double? mean = AnalysisMath.Mean(CollectionsMarshal.AsSpan(pair.Value));
            means[pair.Key] = mean ?? double.NaN;
        }

        return means;
    }

    private static AppError? ValidateRunMeans(Dictionary<FluorescenceChannel, double> means, string quantityName)
    {
        foreach (FluorescenceChannel channel in Channels)
        {
            if (!means.TryGetValue(channel, out double mean) || !double.IsFinite(mean))
            {
                return StageError(
                    ErrorCodes.CalibrationNonFiniteValue,
                    $"Channel {channel}: the run-level {quantityName} is not finite.");
            }
        }

        return null;
    }

    private static Dictionary<FluorescenceChannel, double> EmptyChannelMap() => [];

    private static void Collect(Dictionary<FluorescenceChannel, List<double>> contributions, FluorescenceChannel channel, double value)
    {
        if (!contributions.TryGetValue(channel, out List<double>? list))
        {
            list = [];
            contributions[channel] = list;
        }

        list.Add(value);
    }

    private readonly record struct EndpointYOutcome(double? Y, double? RawEndpoint, int? PointIndex, AppError? Error)
    {
        public static EndpointYOutcome Computed(double y, double rawEndpoint, int pointIndex) => new(y, rawEndpoint, pointIndex, null);

        public static EndpointYOutcome Failed(AppError error) => new(null, null, null, error);
    }
}
