namespace FluoAxis.Application;

using FluoAxis.Analysis;
using FluoAxis.Contracts;
using FluoAxis.Domain;

/// <summary>
/// The run workflow orchestration half of <see cref="ApplicationGateway"/>.
/// Every method here follows the same shape, so the revision/state/audit
/// discipline is uniform and auditable:
/// <list type="number">
///   <item>validate the contract version, then the observed revision, then the current state;</item>
///   <item>read the persisted inputs the stage needs and fail closed if any is missing;</item>
///   <item>run the deterministic Analysis engine on those inputs;</item>
///   <item>commit the new state, the stage's results, the incremented revision, and the audit event in one atomic mutation.</item>
/// </list>
/// A scientific FAIL is a successful command with a failed status: the
/// engine's own contract treats invalidity as a value, not an exception.
/// </summary>
public sealed partial class ApplicationGateway
{
    private const string EventImportCommitted = "run.imported";
    private const string EventWellsAssigned = "run.assigned";
    private const string EventCalibration = "run.calibrated";
    private const string EventControlQc = "run.controlQc";
    private const string EventSamplesAnalyzed = "run.analyzed";

    private async Task<GatewayResult<RunSnapshotDto>> ConfirmMappingCoreAsync(
        ConfirmMappingCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        GatewayResult<RunSnapshotDto>? replay = await ReplaySnapshotAsync(command, RunState.New, EventImportCommitted, cancellationToken).ConfigureAwait(false);
        if (replay is not null) return replay;

        ProjectRunAggregate? aggregate = await ValidateMutationAsync(command, RunState.New, cancellationToken).ConfigureAwait(false);
        if (aggregate is null)
        {
            return GatewayResult.Fail<RunSnapshotDto>(await ClassifyMutationFailureAsync(command, RunState.New, cancellationToken).ConfigureAwait(false) ?? MutationFailureFor(RunMutationFailure.WrongState));
        }

        if (signalPreparation is null)
        {
            return CapabilityUnavailable<RunSnapshotDto>(
                "Importing requires a managed-resource Infrastructure adapter.");
        }

        PreparedSourceIdentity source;
        PreparedDataMapping mapping;
        try
        {
            source = new PreparedSourceIdentity(
                command.OriginalFileName,
                RelativePath.Parse(command.RelativeResourcePath),
                command.SourceSha256,
                command.SourceSizeBytes,
                command.SourceMediaType, command.IssuedUtc);
            mapping = new PreparedDataMapping(
                command.RunId,
                ToPreparedRegion(command.Fam),
                ToPreparedRegion(command.Rox), command.Options);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return Fail<RunSnapshotDto>(
                ErrorCodes.ImportManagedResourceUnsafe,
                "The confirmed mapping or staged resource identity is not valid.",
                "Re-inspect the source and confirm the mapping again.");
        }

        Result<PreparedRunSignals> prepared = await signalPreparation
            .PrepareAsync(new PreparedSignalRequest(source, mapping), progress: null, cancellationToken)
            .ConfigureAwait(false);
        if (!prepared.IsSuccess)
        {
            return prepared.ToGatewayResult(_ => default(RunSnapshotDto)!);
        }

        RunWorkflowPayload payload = new()
        {
            Source = prepared.Value!.Source,
            Signals = prepared.Value.Series,
            Mapping = prepared.Value.Mapping,
        };

        // Imported is a real state distinct from Mapped: the signals now
        // exist and are persisted, but no role assignment has been made yet.
        RunMutationOutcome outcome = await repository.MutateAsync(
            Mutation(command, RunState.New, RunState.Imported, EventImportCommitted),
            payload,
            cancellationToken).ConfigureAwait(false);
        return await ToSnapshotResultAsync(outcome, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GatewayResult<RunSnapshotDto>> AssignWellsCoreAsync(
        AssignWellsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        GatewayResult<RunSnapshotDto>? replay = await ReplaySnapshotAsync(command, RunState.Imported, EventWellsAssigned, cancellationToken).ConfigureAwait(false);
        if (replay is not null) return replay;

        ProjectRunAggregate? aggregate = await ValidateMutationAsync(command, RunState.Imported, cancellationToken).ConfigureAwait(false);
        if (aggregate is null)
        {
            return GatewayResult.Fail<RunSnapshotDto>(await ClassifyMutationFailureAsync(command, RunState.Imported, cancellationToken).ConfigureAwait(false) ?? MutationFailureFor(RunMutationFailure.WrongState));
        }

        WellAssignment[] assignments;
        try
        {
            assignments = [.. command.Assignments.Select(item => item.ToDomain())];
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return Fail<RunSnapshotDto>(
                ErrorCodes.PlateRoleCardinality,
                "The plate assignment is not valid.",
                "Correct the well roles and sample metadata, then assign the plate again.");
        }

        // A plate cannot assign the same well twice; the DTO layer allows a
        // list, so the duplicate check belongs here where the whole set is
        // visible.
        if (assignments.Select(item => item.Well).Distinct().Count() != assignments.Length)
        {
            return Fail<RunSnapshotDto>(
                ErrorCodes.PlateRoleCardinality,
                "A well was assigned more than once in the same plate.",
                "Assign exactly one role per well and submit the plate again.");
        }

        // Every assigned well must exist in the persisted signals, or the
        // engine would silently analyse a different plate than the operator
        // described.
        IReadOnlyList<SignalSeries> signals = await repository.GetSignalsAsync(command.RunId, cancellationToken).ConfigureAwait(false);
        HashSet<WellId> signalWells = [.. signals.Select(series => series.Well)];
        foreach (WellAssignment assignment in assignments)
        {
            if (!signalWells.Contains(assignment.Well))
            {
                return Fail<RunSnapshotDto>(
                    ErrorCodes.PlateRoleCardinality,
                    $"Well {assignment.Well} is assigned a role but carries no imported signal.",
                    "Remove the unrelated well from the assignment or import data that contains it.");
            }
        }

        // AssignWells is the full plate submission. An explicit Ignore role
        // distinguishes intentional exclusion from a forgotten mapping row.
        HashSet<WellId> assignedWells = [.. assignments.Select(assignment => assignment.Well)];
        if (!signalWells.SetEquals(assignedWells))
        {
            return Fail<RunSnapshotDto>(
                ErrorCodes.PlateRoleCardinality,
                "Every imported well requires an explicit role assignment, including Ignore for intentionally unused wells.",
                "Assign a role to every mapped well, then submit the plate again.");
        }

        (WellRole Role, int Minimum)[] requiredRoles =
        [
            (WellRole.Ntc, Math.Max(1, aggregate.Method.Roles.NtcMinimumWells)),
            (WellRole.Positive100, Math.Max(1, aggregate.Method.Roles.Positive100MinimumWells)),
            (WellRole.WildType, Math.Max(1, aggregate.Method.Roles.WildTypeMinimumWells)),
            (WellRole.Lqc01Percent, Math.Max(1, aggregate.Method.Roles.Lqc01PercentMinimumWells)),
        ];
        foreach ((WellRole role, int minimum) in requiredRoles)
        {
            if (assignments.Count(assignment => assignment.Role == role) < minimum)
            {
                return Fail<RunSnapshotDto>(
                    ErrorCodes.PlateRoleCardinality,
                    $"The Method requires at least {minimum} {role} well(s) before the plate can be confirmed.",
                    "Correct the required control roles while the plate is still editable, then submit it again.");
            }
        }

        foreach (IGrouping<string, WellAssignment> sample in assignments
            .Where(assignment => assignment.Sample is not null)
            .GroupBy(assignment => assignment.Sample!.SampleId, StringComparer.Ordinal))
        {
            if (sample.Select(assignment => assignment.Sample!.ReplicateIndex).Distinct().Count() != sample.Count())
            {
                return Fail<RunSnapshotDto>(
                    ErrorCodes.PlateRoleCardinality,
                    $"Sample {sample.Key} has duplicate replicate indices.",
                    "Give every well in this Sample group a distinct replicate index, then submit the plate again.");
            }
        }

        Result<PreparedRoleGroups> grouped = AnalysisInputBinding.GroupByRole(signals, assignments);
        if (!grouped.IsSuccess)
        {
            return grouped.ToGatewayResult(_ => default(RunSnapshotDto)!);
        }

        RunMutationOutcome outcome = await repository.MutateAsync(
            Mutation(command, RunState.Imported, RunState.Mapped, EventWellsAssigned),
            new RunWorkflowPayload { Assignments = assignments },
            cancellationToken).ConfigureAwait(false);
        return await ToSnapshotResultAsync(outcome, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GatewayResult<CalibrationResultDto>> RunCalibrationCoreAsync(
        RunCalibrationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        GatewayResult<CalibrationResultDto>? replay = await ReplayCalibrationAsync(command, cancellationToken).ConfigureAwait(false);
        if (replay is not null) return replay;

        ProjectRunAggregate? aggregate = await ValidateMutationAsync(command, RunState.Mapped, cancellationToken).ConfigureAwait(false);
        if (aggregate is null)
        {
            return GatewayResult.Fail<CalibrationResultDto>(await ClassifyMutationFailureAsync(command, RunState.Mapped, cancellationToken).ConfigureAwait(false) ?? MutationFailureFor(RunMutationFailure.WrongState));
        }

        IReadOnlyList<WellAssignment> assignments = await repository.GetAssignmentsAsync(command.RunId, cancellationToken).ConfigureAwait(false);
        if (assignments.Count == 0)
        {
            return Fail<CalibrationResultDto>(
                ErrorCodes.SystemInvalidRunState,
                "Calibration requires a persisted plate assignment.",
                "Assign the plate roles before running calibration.");
        }

        IReadOnlyList<SignalSeries> signals = await repository.GetSignalsAsync(command.RunId, cancellationToken).ConfigureAwait(false);
        Result<PreparedRoleGroups> grouped = AnalysisInputBinding.GroupByRole(signals, assignments);
        if (!grouped.IsSuccess)
        {
            return grouped.ToGatewayResult(_ => default(CalibrationResultDto)!);
        }

        Result<CalibrationResult> calibrated = engine.Calibrate(
            AnalysisInputBinding.ToCalibrationInput(grouped.Value!),
            aggregate.Method);

        // A stage precondition failure (missing endpoints, bad cardinality,
        // non-finite values) is a refused command: the run does not advance.
        if (!calibrated.IsSuccess)
        {
            return calibrated.ToGatewayResult(_ => default(CalibrationResultDto)!);
        }

        CalibrationResult result = calibrated.Value!;
        ScientificEvidenceCapture? capture = await CreateEvidenceCaptureAsync(aggregate, assignments, signals, cancellationToken).ConfigureAwait(false);
        var scientific = capture?.Calibration(result, aggregate.Method);
        PersistedCalibration persisted = new(
            AnalysisInputBinding.ToWireStatus(result.QcStatus),
            result.RunFm.TryGetValue(FluorescenceChannel.Fam, out double fmFam) ? fmFam : null,
            result.RunFm.TryGetValue(FluorescenceChannel.Rox, out double fmRox) ? fmRox : null,
            result.RunFb.TryGetValue(FluorescenceChannel.Fam, out double fbFam) ? fbFam : null,
            result.RunFb.TryGetValue(FluorescenceChannel.Rox, out double fbRox) ? fbRox : null,
            aggregate.Method.ComputeCanonicalHash(),
            [.. result.Diagnostics.Select(AnalysisInputBinding.ToPersisted)]);

        // The state diagram's intermediate Calibrating/CalibrationFailed
        // nodes are validated as a path, but the public command commits its
        // terminal state and result payload atomically. A failed scientific
        // result continues to RunInvalid in the same mutation.
        bool calibrationPassed = result.QcStatus == QcStatus.Pass;
        RunState target = calibrationPassed ? RunState.CalibrationPassed : RunState.RunInvalid;
        IReadOnlyList<RunState> calibrationPath = calibrationPassed
            ? [RunState.Mapped, RunState.Calibrating, RunState.CalibrationPassed]
            : [RunState.Mapped, RunState.Calibrating, RunState.CalibrationFailed, RunState.RunInvalid];
        RunMutationOutcome outcome = await repository.MutateAsync(
            Mutation(command, RunState.Mapped, target, EventCalibration, calibrationPath),
            new RunWorkflowPayload { Calibration = persisted, ScientificStages = scientific is { } evidence ? [evidence.Stage] : null, ScientificPoints = scientific?.Points },
            cancellationToken).ConfigureAwait(false);
        if (!outcome.IsSuccess)
        {
            return GatewayResult.Fail<CalibrationResultDto>(MutationFailureFor(outcome));
        }

        return GatewayResult.Ok(ToCalibrationDto(persisted));
    }

    private async Task<GatewayResult<ControlQcResultDto>> RunControlQcCoreAsync(
        RunControlQcCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        GatewayResult<ControlQcResultDto>? replay = await ReplayControlAsync(command, cancellationToken).ConfigureAwait(false);
        if (replay is not null) return replay;

        ProjectRunAggregate? aggregate = await ValidateMutationAsync(command, RunState.CalibrationPassed, cancellationToken).ConfigureAwait(false);
        if (aggregate is null)
        {
            return GatewayResult.Fail<ControlQcResultDto>(await ClassifyMutationFailureAsync(command, RunState.CalibrationPassed, cancellationToken).ConfigureAwait(false) ?? MutationFailureFor(RunMutationFailure.WrongState));
        }

        PersistedCalibration? calibration = await repository.GetCalibrationAsync(command.RunId, cancellationToken).ConfigureAwait(false);
        if (calibration is null || AnalysisInputBinding.ParseStatus(calibration.Status) != QcStatus.Pass)
        {
            return Fail<ControlQcResultDto>(
                ErrorCodes.SystemInvalidRunState,
                "Control QC requires persisted calibration results that passed.",
                "Re-run calibration before evaluating the controls.");
        }

        Result<CalibrationResult> calibrationInput = AnalysisInputBinding.Rehydrate(calibration, aggregate.Method);
        if (!calibrationInput.IsSuccess)
        {
            return calibrationInput.ToGatewayResult(_ => default(ControlQcResultDto)!);
        }

        IReadOnlyList<WellAssignment> assignments = await repository.GetAssignmentsAsync(command.RunId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<SignalSeries> signals = await repository.GetSignalsAsync(command.RunId, cancellationToken).ConfigureAwait(false);
        Result<PreparedRoleGroups> grouped = AnalysisInputBinding.GroupByRole(signals, assignments);
        if (!grouped.IsSuccess)
        {
            return grouped.ToGatewayResult(_ => default(ControlQcResultDto)!);
        }

        Result<ControlQcResult> controls = engine.EvaluateControls(
            AnalysisInputBinding.ToControlQcInput(grouped.Value!),
            calibrationInput.Value!,
            aggregate.Method);
        if (!controls.IsSuccess)
        {
            return controls.ToGatewayResult(_ => default(ControlQcResultDto)!);
        }

        ControlQcResult result = controls.Value!;
        ScientificEvidenceCapture? capture = await CreateEvidenceCaptureAsync(aggregate, assignments, signals, cancellationToken).ConfigureAwait(false);

        // The engine builds the WT reference before the LQC loop, so a
        // non-null reference can accompany a failed LQC verdict. Persisting
        // it in that case would let a later stage origin dr' from a control
        // that did not pass; gate it on the control verdict.
        bool passed = result.QcStatus == QcStatus.Pass;
        PersistedControl persisted = new(
            AnalysisInputBinding.ToWireStatus(result.QcStatus),
            passed ? result.WtReference?.YFamMean : null,
            passed ? result.WtReference?.YRoxMean : null,
            LqcCallOf(result),
            result.LqcMeanDrPrime,
            [.. result.Diagnostics.Select(AnalysisInputBinding.ToPersisted)]);

        RunState target = passed ? RunState.ReadyForAnalysis : RunState.RunInvalid;
        // Both a failed WT/LQC control and a mixed LQC replicate direction are
        // scientific control-stage failures.  The domain state machine has one
        // legal edge from ControlQc for this outcome; keep the detailed status
        // and diagnostics in the persisted control snapshot instead of trying
        // to route an invalid control through the sample-only conflict node.
        IReadOnlyList<RunState> controlPath = passed
            ? [RunState.CalibrationPassed, RunState.ControlQc, RunState.ReadyForAnalysis]
            : [RunState.CalibrationPassed, RunState.ControlQc, RunState.ControlQcFailed, RunState.RunInvalid];
        RunMutationOutcome outcome = await repository.MutateAsync(
            Mutation(command, RunState.CalibrationPassed, target, EventControlQc, controlPath),
            new RunWorkflowPayload
            {
                Control = persisted,
                Traces = [.. result.LqcWells.Where(well => well.Trace is not null).Select(well => ToPersistedTrace(well.Trace!, capture))],
                ScientificStages = capture is null ? null : [capture.Controls(result, calibrationInput.Value!, aggregate.Method)],
            },
            cancellationToken).ConfigureAwait(false);
        if (!outcome.IsSuccess)
        {
            return GatewayResult.Fail<ControlQcResultDto>(MutationFailureFor(outcome));
        }

        return GatewayResult.Ok(ToControlDto(persisted));
    }

    private async Task<GatewayResult<IReadOnlyList<SampleAnalysisDto>>> AnalyzeSamplesCoreAsync(
        AnalyzeSamplesCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        GatewayResult<IReadOnlyList<SampleAnalysisDto>>? replay = await ReplaySamplesAsync(command, cancellationToken).ConfigureAwait(false);
        if (replay is not null) return replay;

        ProjectRunAggregate? aggregate = await ValidateMutationAsync(command, RunState.ReadyForAnalysis, cancellationToken).ConfigureAwait(false);
        if (aggregate is null)
        {
            return GatewayResult.Fail<IReadOnlyList<SampleAnalysisDto>>(await ClassifyMutationFailureAsync(command, RunState.ReadyForAnalysis, cancellationToken).ConfigureAwait(false) ?? MutationFailureFor(RunMutationFailure.WrongState));
        }

        PersistedCalibration? calibration = await repository.GetCalibrationAsync(command.RunId, cancellationToken).ConfigureAwait(false);
        PersistedControl? control = await repository.GetControlAsync(command.RunId, cancellationToken).ConfigureAwait(false);
        if (calibration is null
            || AnalysisInputBinding.ParseStatus(calibration.Status) != QcStatus.Pass
            || control is null
            || AnalysisInputBinding.ParseStatus(control.Status) != QcStatus.Pass)
        {
            return Fail<IReadOnlyList<SampleAnalysisDto>>(
                ErrorCodes.SystemInvalidRunState,
                "Sample analysis requires a passed calibration and control QC.",
                "Recalibrate and re-evaluate the controls before analysing samples.");
        }

        Result<CalibrationResult> calibrationInput = AnalysisInputBinding.Rehydrate(calibration, aggregate.Method);
        if (!calibrationInput.IsSuccess)
        {
            return calibrationInput.ToGatewayResult(_ => default(IReadOnlyList<SampleAnalysisDto>)!);
        }

        WtReference? reference = AnalysisInputBinding.RehydrateReference(control);
        if (reference is null)
        {
            // Defense in depth: a passing control stage always publishes a
            // reference; a missing one means the stage did not really pass.
            return Fail<IReadOnlyList<SampleAnalysisDto>>(
                ErrorCodes.SystemInvalidRunState,
                "The persisted control stage did not publish a WT reference coordinate.",
                "Re-evaluate the controls before analysing samples.");
        }

        IReadOnlyList<WellAssignment> assignments = await repository.GetAssignmentsAsync(command.RunId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<SignalSeries> signals = await repository.GetSignalsAsync(command.RunId, cancellationToken).ConfigureAwait(false);
        Result<PreparedRoleGroups> grouped = AnalysisInputBinding.GroupByRole(signals, assignments);
        if (!grouped.IsSuccess)
        {
            return grouped.ToGatewayResult(_ => default(IReadOnlyList<SampleAnalysisDto>)!);
        }

        Result<SampleAnalysisResult> analyzed = engine.AnalyzeSamples(
            AnalysisInputBinding.ToSampleAnalysisInput(grouped.Value!),
            reference,
            calibrationInput.Value!,
            aggregate.Method);
        if (!analyzed.IsSuccess)
        {
            return analyzed.ToGatewayResult(_ => default(IReadOnlyList<SampleAnalysisDto>)!);
        }

        SampleAnalysisResult result = analyzed.Value!;
        ScientificEvidenceCapture? capture = await CreateEvidenceCaptureAsync(aggregate, assignments, signals, cancellationToken).ConfigureAwait(false);
        bool conflict = result.Results.Any(sample => sample.Status != QcStatus.Pass);

        PersistedSample[] samples = [.. result.Results.Select(ToPersistedSample)];
        PersistedTrace[] traces = [.. result.Results.SelectMany(sample => sample.Wells).Where(well => well.Trace is not null).Select(well => ToPersistedTrace(well.Trace!, capture))];

        RunState target = conflict ? RunState.RunInvalid : RunState.Analyzed;
        IReadOnlyList<RunState> analysisPath = conflict
            ? [RunState.ReadyForAnalysis, RunState.Analyzing, RunState.ReplicateConflict, RunState.RunInvalid]
            : [RunState.ReadyForAnalysis, RunState.Analyzing, RunState.Analyzed];
        RunMutationOutcome outcome = await repository.MutateAsync(
            Mutation(command, RunState.ReadyForAnalysis, target, EventSamplesAnalyzed, analysisPath),
            new RunWorkflowPayload { Samples = samples, Traces = traces, ScientificStages = capture is null ? null : [capture.Samples(result)] },
            cancellationToken).ConfigureAwait(false);
        if (!outcome.IsSuccess)
        {
            return GatewayResult.Fail<IReadOnlyList<SampleAnalysisDto>>(MutationFailureFor(outcome));
        }

        return GatewayResult.Ok<IReadOnlyList<SampleAnalysisDto>>([.. result.Results.Select(ToSampleDto)]);
    }

    private async Task<GatewayResult<RunSnapshotDto>> ToSnapshotResultAsync(RunMutationOutcome outcome, CancellationToken cancellationToken)
    {
        if (!outcome.IsSuccess)
        {
            return GatewayResult.Fail<RunSnapshotDto>(MutationFailureFor(outcome));
        }

        ProjectRunAggregate aggregate = outcome.Aggregate!;
        return GatewayResult.Ok(await BuildSnapshotAsync(aggregate, cancellationToken).ConfigureAwait(false));
    }

    private async Task<StoredRunMutation?> ReplayAsync(
        CommandBase command,
        RunState expectedState,
        string eventType,
        CancellationToken cancellationToken)
    {
        StoredRunMutation? stored = await repository.GetMutationAsync(command.RequestId, cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            return null;
        }

        RunMutationRequest request = stored.Request;
        return request.RunId == command.RunId
            && request.ExpectedRunRevision == command.ExpectedRunRevision
            && request.ExpectedState == expectedState
            && request.ActorId == command.ActorId
            && request.IssuedUtc == command.IssuedUtc
            && request.ContractVersion == command.ContractVersion
            && string.Equals(request.EventType, eventType, StringComparison.Ordinal)
            ? stored
            : null;
    }

    private async Task<GatewayResult<RunSnapshotDto>?> ReplaySnapshotAsync(
        CommandBase command,
        RunState expectedState,
        string eventType,
        CancellationToken cancellationToken)
    {
        StoredRunMutation? replay = await ReplayAsync(command, expectedState, eventType, cancellationToken).ConfigureAwait(false);
        return replay is null ? null : await ToSnapshotResultAsync(replay.Outcome, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GatewayResult<CalibrationResultDto>?> ReplayCalibrationAsync(
        RunCalibrationCommand command,
        CancellationToken cancellationToken)
    {
        StoredRunMutation? replay = await ReplayAsync(command, RunState.Mapped, EventCalibration, cancellationToken).ConfigureAwait(false);
        if (replay is null) return null;
        PersistedCalibration? calibration = await repository.GetCalibrationAsync(command.RunId, cancellationToken).ConfigureAwait(false);
        return calibration is null ? null : GatewayResult.Ok(ToCalibrationDto(calibration));
    }

    private async Task<GatewayResult<ControlQcResultDto>?> ReplayControlAsync(
        RunControlQcCommand command,
        CancellationToken cancellationToken)
    {
        StoredRunMutation? replay = await ReplayAsync(command, RunState.CalibrationPassed, EventControlQc, cancellationToken).ConfigureAwait(false);
        if (replay is null) return null;
        PersistedControl? control = await repository.GetControlAsync(command.RunId, cancellationToken).ConfigureAwait(false);
        return control is null ? null : GatewayResult.Ok(ToControlDto(control));
    }

    private async Task<GatewayResult<IReadOnlyList<SampleAnalysisDto>>?> ReplaySamplesAsync(
        AnalyzeSamplesCommand command,
        CancellationToken cancellationToken)
    {
        StoredRunMutation? replay = await ReplayAsync(command, RunState.ReadyForAnalysis, EventSamplesAnalyzed, cancellationToken).ConfigureAwait(false);
        if (replay is null) return null;
        IReadOnlyList<PersistedSample> samples = await repository.GetSamplesAsync(command.RunId, cancellationToken).ConfigureAwait(false);
        return GatewayResult.Ok<IReadOnlyList<SampleAnalysisDto>>([.. samples.Select(ToSampleDto)]);
    }

    /// <summary>Builds the snapshot with the stage summaries the run has actually produced.</summary>
    private async Task<RunSnapshotDto> BuildSnapshotAsync(ProjectRunAggregate aggregate, CancellationToken cancellationToken)
    {
        PreparedSourceIdentity? source = await repository.GetSourceAsync(aggregate.RunId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<WellAssignment> assignments = await repository.GetAssignmentsAsync(aggregate.RunId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<SignalSeries> signals = await repository.GetSignalsAsync(aggregate.RunId, cancellationToken).ConfigureAwait(false);
        PersistedCalibration? calibration = await repository.GetCalibrationAsync(aggregate.RunId, cancellationToken).ConfigureAwait(false);
        PersistedControl? control = await repository.GetControlAsync(aggregate.RunId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<PersistedSample> samples = await repository.GetSamplesAsync(aggregate.RunId, cancellationToken).ConfigureAwait(false);

        return BuildSnapshot(aggregate, source, assignments,
            signals.Select(series => series.Well).Distinct().Count(), calibration, control, samples);
    }

    private static RunSnapshotDto BuildSnapshot(ProjectRunAggregate aggregate, PreparedSourceIdentity? source,
        IReadOnlyList<WellAssignment> assignments, int totalWells, PersistedCalibration? calibration,
        PersistedControl? control, IReadOnlyList<PersistedSample> samples)
    {
        SourceFileDto[] sourceFiles = source is null
            ? []
            : [new SourceFileDto(source.DisplayName, source.ResourcePath.Value, source.Sha256, source.SizeBytes, source.ImportedAtUtc ?? aggregate.CreatedAtUtc)];

        MappingSummaryDto? mappingSummary = assignments.Count == 0
            ? null
            : new MappingSummaryDto(
                totalWells,
                assignments.Count,
                [.. assignments.Where(item => item.Sample is not null).Select(item => item.Sample!.SampleId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);

        CalibrationSummaryDto? calibrationSummary = calibration is null
            ? null
            : new CalibrationSummaryDto(
                calibration.Status,
                calibration.RunFmFam,
                calibration.RunFmRox,
                calibration.RunFbFam,
                calibration.RunFbRox);

        ControlSummaryDto? controlSummary = control is null
            ? null
            : new ControlSummaryDto(control.Status, control.WtYFamMean, control.WtYRoxMean, control.LqcCall, control.LqcMeanDrPrime);

        // Counts are derived from the persisted sample rows and must be
        // mutually exclusive and exhaustive, which the DTO enforces.
        int positiveCount = samples.Count(sample => sample.Call == CallNames.Positive);
        int negativeCount = samples.Count(sample => sample.Call == CallNames.Negative);
        int invalidCount = samples.Count - positiveCount - negativeCount;
        AnalysisSummaryDto? analysisSummary = samples.Count == 0
            ? null
            : new AnalysisSummaryDto(
                AnalysisInputBinding.ToWireStatus(aggregate.RunState == RunState.Analyzed ? QcStatus.Pass : QcStatus.Invalid),
                samples.Count,
                positiveCount,
                negativeCount,
                invalidCount);

        IssueDto[] issues =
        [
            .. (calibration?.Diagnostics ?? []).Select(ToIssue),
            .. (control?.Diagnostics ?? []).Select(ToIssue),
            .. samples.Where(sample => sample.ErrorCode is not null).Select(ToSampleIssue),
        ];

        return new RunSnapshotDto(
            aggregate.RunId,
            aggregate.RunRevision,
            RunStateNames.FromDomain(aggregate.RunState),
            RunSnapshotPolicy.AllowedActions(aggregate.RunState),
            blockingIssues: issues,
            warningIssues: [],
            aggregate.Method.MethodId,
            aggregate.Method.Version,
            aggregate.Method.ComputeCanonicalHash(),
            sourceFiles,
            mappingSummary,
            calibrationSummary,
            controlSummary,
            analysisSummary,
            aggregate.UpdatedUtc);
    }

    private static RunMutationRequest Mutation(
        CommandBase command,
        RunState from,
        RunState to,
        string eventType,
        IReadOnlyList<RunState>? transitionPath = null) =>
        new(command.RequestId, command.RunId, command.ExpectedRunRevision, from, to, command.ActorId, command.IssuedUtc, command.ContractVersion, eventType, transitionPath);

    private static PreparedChannelRegion ToPreparedRegion(ChannelRegionMappingDto region) => new(
        StringToChannel(region.Channel),
        region.SheetName,
        region.HeaderRowIndex,
        region.TimeColumnIndex,
        region.WellColumns.ToDictionary(item => item.ColumnIndex, item => WellId.Parse(item.Well)), region.EndRowIndex);

    private static FluorescenceChannel StringToChannel(string channel) =>
        channel == ChannelNames.Fam ? FluorescenceChannel.Fam : FluorescenceChannel.Rox;

    private static string? LqcCallOf(ControlQcResult result) => result.LqcDirection switch
    {
        DrPrimeCall.Positive => CallNames.Positive,
        DrPrimeCall.Negative => CallNames.Negative,
        _ => null,
    };

    private static CalibrationResultDto ToCalibrationDto(PersistedCalibration calibration) => new(
        calibration.Status,
        calibration.RunFmFam,
        calibration.RunFmRox,
        calibration.RunFbFam,
        calibration.RunFbRox,
        [.. calibration.Diagnostics.Select(ToDiagnosticDto)]);

    private static ControlQcResultDto ToControlDto(PersistedControl control) => new(
        control.Status,
        control.WtYFamMean,
        control.WtYRoxMean,
        control.LqcCall,
        control.LqcMeanDrPrime,
        [.. control.Diagnostics.Select(ToIssue)]);

    private static SampleAnalysisDto ToSampleDto(SampleResult sample) => new(
        sample.SampleId,
        AnalysisInputBinding.ToWireStatus(sample.Status),
        CallNameOf(sample.Direction),
        sample.MeanDrPrime,
        [.. sample.Wells.Select(ToPerWellDto)]);

    private static SampleAnalysisDto ToSampleDto(PersistedSample sample) => new(
        sample.SampleId,
        sample.Status,
        sample.Call,
        sample.MeanDrPrime,
        [.. sample.Wells.Select(well => new PerWellResultDto(
            well.Well,
            well.YFam,
            well.YRox,
            well.DrPrime,
            well.Call,
            well.TraceId))]);

    private static PerWellResultDto ToPerWellDto(WellDrPrimeResult well) => new(
        well.Well.ToString(),
        well.Trace?.GetIntermediate("Y_FAM") ?? double.NaN,
        well.Trace?.GetIntermediate("Y_ROX") ?? double.NaN,
        well.DrPrime ?? double.NaN,
        CallNameOf(well.Call) ?? CallNames.NotDetermined,
        well.Trace?.TraceId.ToString("D") ?? Guid.Empty.ToString("D"));

    private static PersistedSample ToPersistedSample(SampleResult sample) => new(
        sample.SampleId,
        AnalysisInputBinding.ToWireStatus(sample.Status),
        CallNameOf(sample.Direction),
        sample.MeanDrPrime,
        sample.ErrorCode,
        [.. sample.Wells.Select(well => new PersistedSampleWell(
            well.Well.ToString(),
            well.Trace?.GetIntermediate("Y_FAM") ?? double.NaN,
            well.Trace?.GetIntermediate("Y_ROX") ?? double.NaN,
            well.DrPrime ?? double.NaN,
            CallNameOf(well.Call) ?? CallNames.NotDetermined,
            well.Trace?.TraceId.ToString("D") ?? Guid.Empty.ToString("D")))]);

    private static string? CallNameOf(DrPrimeCall? direction) => direction switch
    {
        DrPrimeCall.Positive => CallNames.Positive,
        DrPrimeCall.Negative => CallNames.Negative,
        _ => null,
    };

    private static PersistedTrace ToPersistedTrace(CalculationTrace trace, ScientificEvidenceCapture? capture = null) => new(
        trace.TraceId.ToString("D"),
        trace.Algorithm,
        trace.MethodSnapshotHash,
        trace.SourceWell.ToString(),
        (int)trace.SourceChannel,
        trace.SourcePointIndex,
        [.. trace.IntermediateValues.Select(pair => new PersistedTraceValue(pair.Key, pair.Value))],
        trace.ComparisonOperator,
        trace.ThresholdRaw,
        trace.ResultRaw,
        AnalysisInputBinding.ToWireStatus(trace.FinalStatus), capture?.Provenance(trace));

    private async Task<ScientificEvidenceCapture?> CreateEvidenceCaptureAsync(ProjectRunAggregate run,
        IReadOnlyList<WellAssignment> assignments, IReadOnlyList<SignalSeries> signals, CancellationToken cancellationToken)
    {
        PreparedSourceIdentity? source = await repository.GetSourceAsync(run.RunId, cancellationToken).ConfigureAwait(false);
        PreparedDataMapping? mapping = await repository.GetMappingAsync(run.RunId, cancellationToken).ConfigureAwait(false);
        return source is null || mapping is null ? null : new ScientificEvidenceCapture(run, source, mapping, assignments, signals);
    }

    private static WellDiagnosticDto ToDiagnosticDto(PersistedDiagnostic diagnostic) => new(
        diagnostic.Well,
        diagnostic.Channel == (int)FluorescenceChannel.Fam ? ChannelNames.Fam : ChannelNames.Rox,
        diagnostic.Code,
        diagnostic.Message,
        diagnostic.Value);

    private static IssueDto ToIssue(PersistedDiagnostic diagnostic) => new(
        diagnostic.Code,
        diagnostic.Message,
        "Review the well and the method tolerances, then correct the plate or the source data.",
        diagnostic.Well,
        diagnostic.Channel == (int)FluorescenceChannel.Fam ? ChannelNames.Fam : ChannelNames.Rox);

    private static IssueDto ToSampleIssue(PersistedSample sample) => new(
        sample.ErrorCode!,
        sample.ErrorCode == ErrorCodes.SampleReplicateClassificationInconsistent
            ? $"Sample {sample.SampleId}: replicate wells classified in different directions; no valid group call is available."
            : $"Sample {sample.SampleId}: the stored analysis has an unresolved error ({sample.ErrorCode}).",
        "Repeat the experiment for this Sample group and review every replicate; do not classify from the mean.");

    /// <summary>Maps a refused mutation to the structured wire error the frontend already understands.</summary>
    private static AppErrorDto MutationFailureFor(RunMutationOutcome outcome) =>
        MutationFailureFor(outcome.Failure);

    private static AppErrorDto MutationFailureFor(RunMutationFailure failure) => failure switch
    {
        RunMutationFailure.NotFound => Error(ErrorCodes.ProjectRunNotFound, "The requested Run does not exist.", "Refresh the project list and select an existing Run."),
        RunMutationFailure.StaleRevision => Error(ErrorCodes.SystemStaleRunRevision, "The Run changed after this command was prepared.", "Refresh the Run snapshot before trying again."),
        RunMutationFailure.WrongState => Error(ErrorCodes.SystemInvalidRunState, "The operation is not allowed in the current Run state.", "Refresh the Run snapshot and use one of its allowed actions."),
        RunMutationFailure.IllegalTransition => Error(ErrorCodes.SystemInvalidRunState, "The requested state transition is not part of the run lifecycle.", "Use the action the current Run state allows."),
        RunMutationFailure.RequestConflict => Error(ErrorCodes.SystemRequestIdConflict, "The request ID was already used with different command data.", "Submit the operation with a new request ID."),
        _ => Error(ErrorCodes.SystemInvalidRunState, "The operation could not be applied.", "Refresh the Run snapshot and retry."),
    };
}
