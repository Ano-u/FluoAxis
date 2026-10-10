namespace FluoAxis.Application.Tests;

using FluoAxis.Analysis;
using FluoAxis.Application;
using FluoAxis.Contracts;
using FluoAxis.Domain;
using Xunit;

/// <summary>
/// Proves the vertical scientific workflow: a synthetic dual-channel dataset
/// flows through the real Application orchestration into the deterministic
/// Analysis engine and out to a persisted, state-machine-governed result.
/// The signal port here is a test double that yields a fixed dataset, but
/// everything downstream of it is production code: the real
/// <see cref="BrafAnalysisEngine"/>, the real <see cref="RunStateMachine"/>
/// gate, and the real atomic repository mutation.
/// <para>
/// Commands read the current revision from the run snapshot rather than
/// hard-coding it, exactly as a frontend would, so the tests exercise the
/// optimistic-concurrency contract instead of assuming a commit count.
/// </para>
/// </summary>
public sealed class RunWorkflowEndToEndTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 12, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Synthetic_run_reaches_analyzed_with_engine_derived_results()
    {
        SyntheticPlate plate = SyntheticPlate.Passing();
        ApplicationGateway gateway = CreateGateway(plate);
        Guid runId = await CreateRunAsync(gateway);

        // New -> Imported (import lands the staged signals).
        RunSnapshotDto imported = await StepAsync(
            gateway,
            runId,
            (revision, command) => gateway.ConfirmMappingAsync(plate.MappingCommand(runId, revision, command), CancellationToken.None));
        Assert.Equal(RunStateNames.Imported, imported.DomainState);
        Assert.Single(imported.SourceFiles);
        DateTimeOffset importedAt = imported.SourceFiles[0].ImportedAtUtc;

        // Imported -> Mapped (assign the plate roles).
        RunSnapshotDto mapped = await StepAsync(
            gateway,
            runId,
            (revision, command) => gateway.AssignWellsAsync(plate.AssignCommand(runId, revision, command), CancellationToken.None));
        Assert.Equal(RunStateNames.Mapped, mapped.DomainState);
        Assert.NotNull(mapped.MappingSummary);
        Assert.Equal(8, mapped.MappingSummary!.AssignedWells);
        Assert.Equal(["S-NEG", "S-POS"], mapped.MappingSummary.SampleIds);

        // Mapped -> CalibrationPassed (the real engine computes Fm and Fb).
        GatewayResult<CalibrationResultDto> calibration = await gateway.RunCalibrationAsync(
            plate.CalibrationCommand(runId, await RevisionAsync(gateway, runId)), CancellationToken.None);
        Assert.True(calibration.IsSuccess, Describe(calibration));
        Assert.Equal(StatusNames.Pass, calibration.Value!.Status);
        Assert.Equal(plate.ExpectedRunFmFam, calibration.Value.RunFmFam!.Value, 9);
        Assert.Equal(plate.ExpectedRunFbFam, calibration.Value.RunFbFam!.Value, 9);

        RunSnapshotDto afterCalibration = await SnapshotAsync(gateway, runId);
        Assert.Equal(RunStateNames.CalibrationPassed, afterCalibration.DomainState);
        Assert.Equal([RunAction.RunControlQc, RunAction.ViewExplorer], afterCalibration.AllowedActions);
        Assert.NotNull(afterCalibration.CalibrationSummary);
        Assert.Equal(StatusNames.Pass, afterCalibration.CalibrationSummary!.Status);

        // CalibrationPassed -> ReadyForAnalysis (WT passes, LQC reads positive).
        GatewayResult<ControlQcResultDto> control = await gateway.RunControlQcAsync(
            plate.ControlCommand(runId, afterCalibration.Revision), CancellationToken.None);
        Assert.True(control.IsSuccess, Describe(control));
        Assert.Equal(StatusNames.Pass, control.Value!.Status);
        Assert.Equal(CallNames.Positive, control.Value.LqcCall);

        RunSnapshotDto ready = await SnapshotAsync(gateway, runId);
        Assert.Equal(RunStateNames.ReadyForAnalysis, ready.DomainState);
        Assert.Equal([RunAction.AnalyzeSamples, RunAction.ViewExplorer], ready.AllowedActions);
        Assert.NotNull(ready.ControlSummary);

        // ReadyForAnalysis -> Analyzed (samples classified by dr').
        GatewayResult<IReadOnlyList<SampleAnalysisDto>> analyzed = await gateway.AnalyzeSamplesAsync(
            plate.AnalysisCommand(runId, ready.Revision), CancellationToken.None);
        Assert.True(analyzed.IsSuccess, Describe(analyzed));

        SampleAnalysisDto positive = Assert.Single(analyzed.Value!, sample => sample.SampleId == "S-POS");
        Assert.Equal(CallNames.Positive, positive.Call);
        Assert.Equal(StatusNames.Pass, positive.Status);

        SampleAnalysisDto negative = Assert.Single(analyzed.Value, sample => sample.SampleId == "S-NEG");
        Assert.Equal(CallNames.Negative, negative.Call);

        RunSnapshotDto final = await SnapshotAsync(gateway, runId);
        Assert.Equal(RunStateNames.Analyzed, final.DomainState);
        Assert.Equal([RunAction.Export, RunAction.ViewExplorer], final.AllowedActions);
        Assert.NotNull(final.AnalysisSummary);
        Assert.Equal(2, final.AnalysisSummary!.SampleCount);
        Assert.Equal(1, final.AnalysisSummary.PositiveCount);
        Assert.Equal(1, final.AnalysisSummary.NegativeCount);
        Assert.Equal(0, final.AnalysisSummary.InvalidCount);
        Assert.Equal(importedAt, Assert.Single(final.SourceFiles).ImportedAtUtc);
    }

    [Fact]
    public async Task Exact_stage_retry_replays_without_another_revision()
    {
        SyntheticPlate plate = SyntheticPlate.Passing();
        ApplicationGateway gateway = CreateGateway(plate);
        Guid runId = await CreateRunAsync(gateway);
        await ConfirmAndAssignAsync(gateway, plate, runId);

        int revisionBefore = await RevisionAsync(gateway, runId);
        Guid requestId = Guid.NewGuid();
        RunCalibrationCommand command = plate.CalibrationCommand(runId, revisionBefore, requestId);

        GatewayResult<CalibrationResultDto> first = await gateway.RunCalibrationAsync(command, CancellationToken.None);
        GatewayResult<CalibrationResultDto> replay = await gateway.RunCalibrationAsync(command, CancellationToken.None);

        Assert.True(first.IsSuccess, Describe(first));
        Assert.True(replay.IsSuccess, Describe(replay));
        Assert.NotNull(first.Value);
        Assert.NotNull(replay.Value);
        Assert.Equal(first.Value!.Status, replay.Value!.Status);
        Assert.Equal(first.Value.RunFmFam, replay.Value.RunFmFam);
        Assert.Equal(first.Value.RunFmRox, replay.Value.RunFmRox);
        Assert.Equal(first.Value.RunFbFam, replay.Value.RunFbFam);
        Assert.Equal(first.Value.RunFbRox, replay.Value.RunFbRox);
        Assert.Equal(first.Value.PerWellDiagnostics, replay.Value.PerWellDiagnostics);
        Assert.Equal(revisionBefore + 1, await RevisionAsync(gateway, runId));
    }

    [Fact]
    public async Task Failed_stage_commits_terminal_invalid_state_in_one_revision()
    {
        SyntheticPlate plate = SyntheticPlate.Passing() with { PositivePlateaus = false };
        ApplicationGateway gateway = CreateGateway(plate);
        Guid runId = await CreateRunAsync(gateway);
        await ConfirmAndAssignAsync(gateway, plate, runId);

        int revisionBefore = await RevisionAsync(gateway, runId);
        GatewayResult<CalibrationResultDto> calibration = await gateway.RunCalibrationAsync(
            plate.CalibrationCommand(runId, revisionBefore), CancellationToken.None);

        Assert.True(calibration.IsSuccess, Describe(calibration));
        RunSnapshotDto snapshot = await SnapshotAsync(gateway, runId);
        Assert.Equal(RunStateNames.RunInvalid, snapshot.DomainState);
        Assert.Equal(revisionBefore + 1, snapshot.Revision);
    }

    [Fact]
    public async Task Plateau_failure_routes_through_calibration_failed_to_run_invalid()
    {
        // The positive well's plateau drifts far beyond the 1% relative range.
        SyntheticPlate plate = SyntheticPlate.Passing() with { PositivePlateaus = false };
        ApplicationGateway gateway = CreateGateway(plate);
        Guid runId = await CreateRunAsync(gateway);
        await ConfirmAndAssignAsync(gateway, plate, runId);

        GatewayResult<CalibrationResultDto> calibration = await gateway.RunCalibrationAsync(
            plate.CalibrationCommand(runId, await RevisionAsync(gateway, runId)), CancellationToken.None);

        // A scientific FAIL is a successful command carrying a failed status.
        Assert.True(calibration.IsSuccess, Describe(calibration));
        Assert.Equal(StatusNames.Fail, calibration.Value!.Status);

        // The run is then invalidated so no later stage can consume it.
        RunSnapshotDto snapshot = await SnapshotAsync(gateway, runId);
        Assert.Equal(RunStateNames.RunInvalid, snapshot.DomainState);
        Assert.Equal([RunAction.ViewExplorer], snapshot.AllowedActions);

        GatewayResult<ControlQcResultDto> blocked = await gateway.RunControlQcAsync(
            plate.ControlCommand(runId, snapshot.Revision), CancellationToken.None);
        Assert.False(blocked.IsSuccess);
    }

    [Fact]
    public async Task Mixed_sample_replicates_are_invalid_and_route_to_run_invalid()
    {
        SyntheticPlate plate = SyntheticPlate.Passing() with { MixedSampleReplicates = true };
        ApplicationGateway gateway = CreateGateway(plate);
        Guid runId = await CreateRunAsync(gateway);
        await ConfirmAndAssignAsync(gateway, plate, runId);

        await gateway.RunCalibrationAsync(
            plate.CalibrationCommand(runId, await RevisionAsync(gateway, runId)), CancellationToken.None);
        await gateway.RunControlQcAsync(
            plate.ControlCommand(runId, await RevisionAsync(gateway, runId)), CancellationToken.None);

        GatewayResult<IReadOnlyList<SampleAnalysisDto>> analyzed = await gateway.AnalyzeSamplesAsync(
            plate.AnalysisCommand(runId, await RevisionAsync(gateway, runId)), CancellationToken.None);
        Assert.True(analyzed.IsSuccess, Describe(analyzed));

        SampleAnalysisDto conflicted = Assert.Single(analyzed.Value!, sample => sample.SampleId == "S-POS");
        Assert.Equal(StatusNames.Invalid, conflicted.Status);
        Assert.Null(conflicted.Call);
        Assert.Null(conflicted.MeanDrPrime);

        RunSnapshotDto snapshot = await SnapshotAsync(gateway, runId);
        Assert.Equal(RunStateNames.RunInvalid, snapshot.DomainState);
    }

    [Fact]
    public async Task Mixed_lqc_replicates_are_invalid_and_commit_control_failure()
    {
        SyntheticPlate plate = SyntheticPlate.Passing() with { MixedLqcReplicates = true };
        ApplicationGateway gateway = CreateGateway(plate);
        Guid runId = await CreateRunAsync(gateway);
        await ConfirmAndAssignAsync(gateway, plate, runId);

        GatewayResult<CalibrationResultDto> calibration = await gateway.RunCalibrationAsync(
            plate.CalibrationCommand(runId, await RevisionAsync(gateway, runId)), CancellationToken.None);
        Assert.True(calibration.IsSuccess, Describe(calibration));
        Assert.Equal(StatusNames.Pass, calibration.Value!.Status);

        GatewayResult<ControlQcResultDto> control = await gateway.RunControlQcAsync(
            plate.ControlCommand(runId, await RevisionAsync(gateway, runId)), CancellationToken.None);
        Assert.True(control.IsSuccess, Describe(control));
        Assert.Equal(StatusNames.Invalid, control.Value!.Status);
        Assert.Null(control.Value.LqcCall);
        Assert.Null(control.Value.LqcMeanDrPrime);
        Assert.Contains(control.Value.Issues, issue => issue.Code == AnalysisErrorCodes.ControlLqcReplicateInconsistent);

        RunSnapshotDto snapshot = await SnapshotAsync(gateway, runId);
        Assert.Equal(RunStateNames.RunInvalid, snapshot.DomainState);
        Assert.Equal([RunAction.ViewExplorer], snapshot.AllowedActions);
    }

    [Fact]
    public async Task Stale_revision_is_refused_and_leaves_the_run_unchanged()
    {
        SyntheticPlate plate = SyntheticPlate.Passing();
        ApplicationGateway gateway = CreateGateway(plate);
        Guid runId = await CreateRunAsync(gateway);
        await gateway.ConfirmMappingAsync(plate.MappingCommand(runId, 0), CancellationToken.None);

        RunSnapshotDto current = await SnapshotAsync(gateway, runId);

        // Build a command against a revision the run has already moved past.
        GatewayResult<RunSnapshotDto> stale = await gateway.AssignWellsAsync(
            plate.AssignCommand(runId, current.Revision - 1), CancellationToken.None);
        Assert.False(stale.IsSuccess);
        Assert.Equal(ErrorCodes.SystemStaleRunRevision, Assert.Single(stale.Errors).Code);

        RunSnapshotDto unchanged = await SnapshotAsync(gateway, runId);
        Assert.Equal(current.Revision, unchanged.Revision);
        Assert.Equal(RunStateNames.Imported, unchanged.DomainState);
    }

    [Fact]
    public async Task Pre_canceled_command_creates_no_state_change()
    {
        SyntheticPlate plate = SyntheticPlate.Passing();
        ApplicationGateway gateway = CreateGateway(plate);
        Guid runId = await CreateRunAsync(gateway);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            gateway.ConfirmMappingAsync(plate.MappingCommand(runId, 0), cancellation.Token));

        RunSnapshotDto snapshot = await SnapshotAsync(gateway, runId);
        Assert.Equal(0, snapshot.Revision);
        Assert.Equal(RunStateNames.New, snapshot.DomainState);
    }

    [Fact]
    public async Task Sample_analysis_is_refused_without_a_passing_control_stage()
    {
        // The Application state machine alone proves this: a run that never
        // ran control QC cannot reach sample analysis.
        SyntheticPlate plate = SyntheticPlate.Passing();
        ApplicationGateway gateway = CreateGateway(plate);
        Guid runId = await CreateRunAsync(gateway);
        await ConfirmAndAssignAsync(gateway, plate, runId);
        await gateway.RunCalibrationAsync(
            plate.CalibrationCommand(runId, await RevisionAsync(gateway, runId)), CancellationToken.None);

        GatewayResult<IReadOnlyList<SampleAnalysisDto>> refused = await gateway.AnalyzeSamplesAsync(
            plate.AnalysisCommand(runId, await RevisionAsync(gateway, runId)), CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.Equal(ErrorCodes.SystemInvalidRunState, Assert.Single(refused.Errors).Code);
    }

    /// <summary>Runs one command at the run's current revision and returns the resulting snapshot.</summary>
    private static async Task<RunSnapshotDto> StepAsync(
        ApplicationGateway gateway,
        Guid runId,
        Func<int, Guid, Task<GatewayResult<RunSnapshotDto>>> step)
    {
        int revision = await RevisionAsync(gateway, runId);
        GatewayResult<RunSnapshotDto> result = await step(revision, Guid.NewGuid());
        Assert.True(result.IsSuccess, Describe(result));
        return result.Value!;
    }

    private static async Task ConfirmAndAssignAsync(ApplicationGateway gateway, SyntheticPlate plate, Guid runId)
    {
        await StepAsync(gateway, runId, (revision, _) => gateway.ConfirmMappingAsync(plate.MappingCommand(runId, revision), CancellationToken.None));
        await StepAsync(gateway, runId, (revision, _) => gateway.AssignWellsAsync(plate.AssignCommand(runId, revision), CancellationToken.None));
    }

    private static async Task<RunSnapshotDto> SnapshotAsync(ApplicationGateway gateway, Guid runId)
    {
        GatewayResult<RunSnapshotDto> snapshot = await gateway.GetRunAsync(
            new GetRunQuery(Guid.NewGuid(), runId), CancellationToken.None);
        Assert.True(snapshot.IsSuccess, Describe(snapshot));
        return snapshot.Value!;
    }

    private static async Task<int> RevisionAsync(ApplicationGateway gateway, Guid runId) =>
        (await SnapshotAsync(gateway, runId)).Revision;

    private static async Task<Guid> CreateRunAsync(ApplicationGateway gateway)
    {
        GatewayResult<ProjectSnapshotDto> created = await gateway.CreateProjectAsync(
            new CreateProjectCommand(Guid.NewGuid(), "Workflow", "researcher", Start), CancellationToken.None);
        Assert.True(created.IsSuccess);
        return created.Value!.RunId;
    }

    private static ApplicationGateway CreateGateway(SyntheticPlate plate) => new(
        new InMemoryProjectRunRepository(),
        new FixedTimeProvider(Start.AddMinutes(5)),
        importInspection: null,
        signalPreparation: new SyntheticSignalPort(plate),
        engine: new BrafAnalysisEngine());

    private static string Describe<T>(GatewayResult<T> result) =>
        result.IsSuccess ? "success" : string.Join(", ", result.Errors.Select(error => error.Code));

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class SyntheticSignalPort(SyntheticPlate plate) : ISignalPreparationPort
    {
        public Task<Result<PreparedRunSignals>> PrepareAsync(
            PreparedSignalRequest request,
            IProgress<OperationProgressDto>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<SignalSeries> series = plate.BuildSeries(request.Mapping.RunId);
            return Task.FromResult(Result<PreparedRunSignals>.Ok(new PreparedRunSignals(
                request.Source, request.Mapping, series, diagnostics: [])));
        }
    }
}
