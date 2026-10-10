namespace FluoAxis.Application.Tests;

using FluoAxis.Analysis;
using FluoAxis.Application;
using FluoAxis.Contracts;
using FluoAxis.Domain;
using Xunit;

/// <summary>Persisted inputs must be complete and belong to the immutable Run Method before the engine consumes them.</summary>
public sealed class WorkflowInputIntegrityTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [MemberData(nameof(InvalidCalibrationCases))]
    public async Task Downstream_stage_refuses_invalid_persisted_calibration_without_mutation(bool afterControl, string corruption)
    {
        var repository = new ReadOverrideRepository();
        SyntheticPlate plate = SyntheticPlate.Passing();
        ApplicationGateway gateway = CreateGateway(repository, plate);
        Guid runId = await PrepareAsync(gateway, plate, afterControl ? 2 : 1);
        ProjectRunAggregate before = (await repository.Inner.GetByRunIdAsync(runId, CancellationToken.None))!;
        repository.CalibrationTransform = calibration => Corrupt(calibration, corruption);

        IReadOnlyList<AppErrorDto> errors;
        if (afterControl)
        {
            GatewayResult<IReadOnlyList<SampleAnalysisDto>> result = await gateway.AnalyzeSamplesAsync(
                plate.AnalysisCommand(runId, before.RunRevision), CancellationToken.None);
            Assert.False(result.IsSuccess);
            errors = result.Errors;
        }
        else
        {
            GatewayResult<ControlQcResultDto> result = await gateway.RunControlQcAsync(
                plate.ControlCommand(runId, before.RunRevision), CancellationToken.None);
            Assert.False(result.IsSuccess);
            errors = result.Errors;
        }

        Assert.Equal(ErrorCodes.SystemInvalidRunState, Assert.Single(errors).Code);
        ProjectRunAggregate after = (await repository.Inner.GetByRunIdAsync(runId, CancellationToken.None))!;
        Assert.Equal(before, after);
        Assert.Empty(await repository.Inner.GetSamplesAsync(runId, CancellationToken.None));
        if (!afterControl)
        {
            Assert.Null(await repository.Inner.GetControlAsync(runId, CancellationToken.None));
        }
    }

    public static TheoryData<bool, string> InvalidCalibrationCases()
    {
        TheoryData<bool, string> cases = new();
        foreach (bool afterControl in new[] { false, true })
        {
            foreach (string corruption in new[] { "method", "fmFamMissing", "fmRoxMissing", "fbFamMissing", "fbRoxMissing", "nonFinite" })
            {
                cases.Add(afterControl, corruption);
            }
        }

        return cases;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Every_analysis_stage_refuses_an_assigned_sample_missing_one_channel(int completedStages)
    {
        var repository = new ReadOverrideRepository();
        SyntheticPlate plate = SyntheticPlate.Passing();
        ApplicationGateway gateway = CreateGateway(repository, plate);
        Guid runId = await PrepareAsync(gateway, plate, completedStages);
        ProjectRunAggregate before = (await repository.Inner.GetByRunIdAsync(runId, CancellationToken.None))!;
        repository.MissingRoxWell = WellId.Parse("B2");

        IReadOnlyList<AppErrorDto> errors;
        if (completedStages == 0)
        {
            GatewayResult<CalibrationResultDto> result = await gateway.RunCalibrationAsync(
                plate.CalibrationCommand(runId, before.RunRevision), CancellationToken.None);
            Assert.False(result.IsSuccess);
            errors = result.Errors;
        }
        else if (completedStages == 1)
        {
            GatewayResult<ControlQcResultDto> result = await gateway.RunControlQcAsync(
                plate.ControlCommand(runId, before.RunRevision), CancellationToken.None);
            Assert.False(result.IsSuccess);
            errors = result.Errors;
        }
        else
        {
            GatewayResult<IReadOnlyList<SampleAnalysisDto>> result = await gateway.AnalyzeSamplesAsync(
                plate.AnalysisCommand(runId, before.RunRevision), CancellationToken.None);
            Assert.False(result.IsSuccess);
            errors = result.Errors;
        }

        Assert.Equal(ErrorCodes.PlateRoleCardinality, Assert.Single(errors).Code);
        Assert.Contains("B2", errors[0].UserMessage, StringComparison.Ordinal);
        Assert.Equal(before, await repository.Inner.GetByRunIdAsync(runId, CancellationToken.None));
        Assert.Empty(await repository.Inner.GetSamplesAsync(runId, CancellationToken.None));
    }

    [Fact]
    public async Task Null_per_well_call_is_not_determined_in_response_and_persistence()
    {
        var repository = new ReadOverrideRepository();
        SyntheticPlate plate = SyntheticPlate.Passing();
        ApplicationGateway gateway = CreateGateway(repository, plate, new NullCallAnalysisEngine());
        Guid runId = await PrepareAsync(gateway, plate, 2);
        ProjectRunAggregate before = (await repository.Inner.GetByRunIdAsync(runId, CancellationToken.None))!;

        GatewayResult<IReadOnlyList<SampleAnalysisDto>> result = await gateway.AnalyzeSamplesAsync(
            plate.AnalysisCommand(runId, before.RunRevision), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.All(result.Value!, sample => Assert.All(sample.PerWell, well => Assert.Equal(CallNames.NotDetermined, well.Call)));
        Assert.All(await repository.Inner.GetSamplesAsync(runId, CancellationToken.None),
            sample => Assert.All(sample.Wells, well => Assert.Equal(CallNames.NotDetermined, well.Call)));
        Assert.Equal(RunState.RunInvalid, (await repository.Inner.GetByRunIdAsync(runId, CancellationToken.None))!.RunState);
    }

    [Theory]
    [InlineData(WellRoleNames.Ntc)]
    [InlineData(WellRoleNames.Positive100)]
    [InlineData(WellRoleNames.WildType)]
    [InlineData(WellRoleNames.Lqc01Percent)]
    public async Task Missing_required_control_is_rejected_while_plate_remains_editable(string missingRole)
    {
        var repository = new ReadOverrideRepository();
        SyntheticPlate plate = SyntheticPlate.Passing();
        ApplicationGateway gateway = CreateGateway(repository, plate);
        Guid runId = await ImportAsync(gateway, plate);
        AssignWellsCommand valid = plate.AssignCommand(runId, 1);
        WellAssignmentDto[] invalid = [.. valid.Assignments.Select(assignment => assignment.Role == missingRole
            ? new WellAssignmentDto(assignment.Well, WellRoleNames.Ignore)
            : assignment)];

        await AssertAssignmentRejectedAsync(gateway, repository, runId, invalid);
        Assert.True((await gateway.AssignWellsAsync(valid, CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task Configured_control_cardinality_is_checked_before_mapping()
    {
        var repository = new ReadOverrideRepository
        {
            MethodOverride = new BrafMethodSnapshot(roles: new RoleCardinalitySpec(ntcMinimumWells: 2)),
        };
        SyntheticPlate plate = SyntheticPlate.Passing();
        ApplicationGateway gateway = CreateGateway(repository, plate);
        Guid runId = await ImportAsync(gateway, plate);

        await AssertAssignmentRejectedAsync(gateway, repository, runId, plate.AssignCommand(runId, 1).Assignments);
    }

    [Fact]
    public async Task Omitted_imported_well_requires_an_explicit_ignore_assignment()
    {
        var repository = new ReadOverrideRepository();
        SyntheticPlate plate = SyntheticPlate.Passing();
        ApplicationGateway gateway = CreateGateway(repository, plate);
        Guid runId = await ImportAsync(gateway, plate);
        WellAssignmentDto[] incomplete = [.. plate.AssignCommand(runId, 1).Assignments.Where(assignment => assignment.Well != "B2")];

        await AssertAssignmentRejectedAsync(gateway, repository, runId, incomplete);
        GatewayResult<RunSnapshotDto> corrected = await gateway.AssignWellsAsync(Assignment(runId,
            [.. incomplete, new WellAssignmentDto("B2", WellRoleNames.Ignore)]), CancellationToken.None);
        Assert.True(corrected.IsSuccess);
    }

    [Fact]
    public async Task Duplicate_sample_replicate_indices_are_rejected_but_gaps_are_allowed()
    {
        var repository = new ReadOverrideRepository();
        SyntheticPlate plate = SyntheticPlate.Passing();
        ApplicationGateway gateway = CreateGateway(repository, plate);
        Guid runId = await ImportAsync(gateway, plate);
        WellAssignmentDto[] duplicate = [.. plate.AssignCommand(runId, 1).Assignments.Select(assignment => assignment.Well == "B2"
            ? new WellAssignmentDto("B2", WellRoleNames.Sample, sampleId: "S-POS", replicateIndex: 1)
            : assignment)];

        await AssertAssignmentRejectedAsync(gateway, repository, runId, duplicate);
        WellAssignmentDto[] corrected = [.. duplicate.Select(assignment => assignment.Well == "B2"
            ? new WellAssignmentDto("B2", WellRoleNames.Sample, sampleId: "S-POS", replicateIndex: 3)
            : assignment)];
        Assert.True((await gateway.AssignWellsAsync(Assignment(runId, corrected), CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task Control_only_plate_with_explicit_ignored_wells_is_allowed()
    {
        var repository = new ReadOverrideRepository();
        SyntheticPlate plate = SyntheticPlate.Passing();
        ApplicationGateway gateway = CreateGateway(repository, plate);
        Guid runId = await ImportAsync(gateway, plate);
        WellAssignmentDto[] controls = [.. plate.AssignCommand(runId, 1).Assignments.Select(assignment => assignment.Role == WellRoleNames.Sample
            ? new WellAssignmentDto(assignment.Well, WellRoleNames.Ignore)
            : assignment)];

        GatewayResult<RunSnapshotDto> result = await gateway.AssignWellsAsync(Assignment(runId, controls), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RunStateNames.Mapped, result.Value!.DomainState);
        Assert.Empty(result.Value.MappingSummary!.SampleIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inconsistent_channel_identity_or_time_axis_is_a_structured_refusal(bool mismatchedRun)
    {
        var repository = new ReadOverrideRepository();
        SyntheticPlate plate = SyntheticPlate.Passing();
        ApplicationGateway gateway = CreateGateway(repository, plate);
        Guid runId = await PrepareAsync(gateway, plate, 2);
        ProjectRunAggregate before = (await repository.Inner.GetByRunIdAsync(runId, CancellationToken.None))!;
        repository.SignalsTransform = signals => [.. signals.Select(series => series.Well == WellId.Parse("B2") && series.Channel == FluorescenceChannel.Rox
            ? SignalSeries.Create(mismatchedRun ? Guid.NewGuid() : series.RunId, series.Well, series.Channel,
                mismatchedRun ? series.Points : series.Points.Select(point => new SignalPoint(point.Elapsed + TimeSpan.FromSeconds(1), point.Rfu)))
            : series)];

        GatewayResult<IReadOnlyList<SampleAnalysisDto>> result = await gateway.AnalyzeSamplesAsync(
            plate.AnalysisCommand(runId, before.RunRevision), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.PlateRoleCardinality, Assert.Single(result.Errors).Code);
        Assert.Contains("B2", result.Errors[0].UserMessage, StringComparison.Ordinal);
        Assert.Equal(before, await repository.Inner.GetByRunIdAsync(runId, CancellationToken.None));
        Assert.Empty(await repository.Inner.GetSamplesAsync(runId, CancellationToken.None));
    }

    [Fact]
    public async Task MissingInteriorQcSourceCoordinateKeepsStageProvenancePartial()
    {
        var repository = new ReadOverrideRepository();
        SyntheticPlate plate = SyntheticPlate.Passing();
        string[] wells = ["A1", "A2", "A3", "A4", "B1", "B2", "C1", "C2"];
        repository.SignalsTransform = signals => [.. signals.Select(series => SignalSeries.Create(series.RunId, series.Well, series.Channel,
            series.Points.Select((point, index) => new SignalPoint(point.Elapsed, point.Rfu,
                series.Well.ToString() == "A1" && series.Channel == FluorescenceChannel.Fam && index == 0 ? null : index + 1,
                Array.IndexOf(wells, series.Well.ToString()) + 1))))];
        ApplicationGateway gateway = CreateGateway(repository, plate);
        Guid runId = await PrepareAsync(gateway, plate, 1);

        AnalysisStageEvidenceDto stage = Assert.Single(await repository.Inner.GetScientificStagesAsync(runId, CancellationToken.None));

        Assert.All(stage.Wells, well => Assert.NotNull(well.Endpoint.SourceRowIndex));
        Assert.Equal(ProvenanceCompletenessNames.StoredPartial, stage.ProvenanceCompleteness);
        PersistedScientificPointPage points = await repository.Inner.GetScientificPointsAsync(runId, "calibration", "A1", ChannelNames.Fam, 0, 1, CancellationToken.None);
        Assert.Null(Assert.Single(points.Points).SourceRowIndex);
    }

    [Theory]
    [InlineData(0.777)]
    [InlineData(null)]
    public async Task Control_mean_is_forwarded_from_analysis_without_recomputation_or_backfill(double? engineMean)
    {
        var repository = new ReadOverrideRepository();
        SyntheticPlate plate = SyntheticPlate.Passing();
        ApplicationGateway gateway = CreateGateway(repository, plate, new AuthoritativeControlMeanEngine(engineMean));
        Guid runId = await PrepareAsync(gateway, plate, 1);
        ProjectRunAggregate before = (await repository.Inner.GetByRunIdAsync(runId, CancellationToken.None))!;

        GatewayResult<ControlQcResultDto> response = await gateway.RunControlQcAsync(plate.ControlCommand(runId, before.RunRevision), CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.Equal(engineMean, response.Value!.LqcMeanDrPrime);
        Assert.Equal(engineMean, (await repository.Inner.GetControlAsync(runId, CancellationToken.None))!.LqcMeanDrPrime);
        Assert.Equal(engineMean, (await gateway.GetRunAsync(new GetRunQuery(Guid.NewGuid(), runId), CancellationToken.None)).Value!.ControlSummary!.LqcMeanDrPrime);
    }

    private static AssignWellsCommand Assignment(Guid runId, IReadOnlyList<WellAssignmentDto> assignments) =>
        new(Guid.NewGuid(), runId, 1, "researcher", Start, assignments);

    private static async Task AssertAssignmentRejectedAsync(ApplicationGateway gateway, ReadOverrideRepository repository,
        Guid runId, IReadOnlyList<WellAssignmentDto> assignments)
    {
        GatewayResult<RunSnapshotDto> result = await gateway.AssignWellsAsync(Assignment(runId, assignments), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.PlateRoleCardinality, Assert.Single(result.Errors).Code);
        ProjectRunAggregate after = (await repository.Inner.GetByRunIdAsync(runId, CancellationToken.None))!;
        Assert.Equal(RunState.Imported, after.RunState);
        Assert.Equal(1, after.RunRevision);
        Assert.Empty(await repository.Inner.GetAssignmentsAsync(runId, CancellationToken.None));
        Assert.Null(await repository.Inner.GetCalibrationAsync(runId, CancellationToken.None));
    }

    private static async Task<Guid> ImportAsync(ApplicationGateway gateway, SyntheticPlate plate)
    {
        GatewayResult<ProjectSnapshotDto> created = await gateway.CreateProjectAsync(
            new CreateProjectCommand(Guid.NewGuid(), "Plate validation", "researcher", Start), CancellationToken.None);
        Assert.True(created.IsSuccess);
        Guid runId = created.Value!.RunId;
        Assert.True((await gateway.ConfirmMappingAsync(plate.MappingCommand(runId, 0), CancellationToken.None)).IsSuccess);
        return runId;
    }

    private static PersistedCalibration Corrupt(PersistedCalibration calibration, string corruption) => corruption switch
    {
        "method" => calibration with { MethodSnapshotHash = new string('0', 64) },
        "fmFamMissing" => calibration with { RunFmFam = null },
        "fmRoxMissing" => calibration with { RunFmRox = null },
        "fbFamMissing" => calibration with { RunFbFam = null },
        "fbRoxMissing" => calibration with { RunFbRox = null },
        "nonFinite" => calibration with { RunFmFam = double.PositiveInfinity },
        _ => throw new ArgumentException("Unknown test corruption.", nameof(corruption)),
    };

    private static ApplicationGateway CreateGateway(ReadOverrideRepository repository, SyntheticPlate plate, IAnalysisEngine? engine = null) => new(
        repository, timeProvider: null, importInspection: null, signalPreparation: new SyntheticSignalPort(plate),
        engine: engine ?? new BrafAnalysisEngine());

    private static async Task<Guid> PrepareAsync(ApplicationGateway gateway, SyntheticPlate plate, int completedStages)
    {
        GatewayResult<ProjectSnapshotDto> created = await gateway.CreateProjectAsync(
            new CreateProjectCommand(Guid.NewGuid(), "Input integrity", "researcher", Start), CancellationToken.None);
        Assert.True(created.IsSuccess);
        Guid runId = created.Value!.RunId;
        GatewayResult<RunSnapshotDto> imported = await gateway.ConfirmMappingAsync(plate.MappingCommand(runId, 0), CancellationToken.None);
        Assert.True(imported.IsSuccess);
        GatewayResult<RunSnapshotDto> mapped = await gateway.AssignWellsAsync(plate.AssignCommand(runId, imported.Value!.Revision), CancellationToken.None);
        Assert.True(mapped.IsSuccess);
        int revision = mapped.Value!.Revision;
        if (completedStages >= 1)
        {
            GatewayResult<CalibrationResultDto> calibration = await gateway.RunCalibrationAsync(plate.CalibrationCommand(runId, revision++), CancellationToken.None);
            Assert.True(calibration.IsSuccess);
            Assert.Equal(StatusNames.Pass, calibration.Value!.Status);
        }

        if (completedStages >= 2)
        {
            GatewayResult<ControlQcResultDto> control = await gateway.RunControlQcAsync(plate.ControlCommand(runId, revision), CancellationToken.None);
            Assert.True(control.IsSuccess);
            Assert.Equal(StatusNames.Pass, control.Value!.Status);
        }

        return runId;
    }

    private sealed class AuthoritativeControlMeanEngine(double? reportedMean) : IAnalysisEngine
    {
        private readonly BrafAnalysisEngine inner = new();
        public Result<CalibrationResult> Calibrate(CalibrationInput input, BrafMethodSnapshot method) => inner.Calibrate(input, method);
        public Result<ControlQcResult> EvaluateControls(ControlQcInput input, CalibrationResult calibration, BrafMethodSnapshot method)
        {
            ControlQcResult actual = inner.EvaluateControls(input, calibration, method).Value!;
            // Deliberately different from the per-well mean: tests ownership of
            // the returned result, not scientific acceptance of synthetic data.
            return Result<ControlQcResult>.Ok(new(actual.WtWells, actual.LqcWells, actual.WtReference,
                actual.QcStatus, actual.Diagnostics, actual.LqcDirection, actual.EvaluatedWtReference, reportedMean));
        }
        public Result<SampleAnalysisResult> AnalyzeSamples(SampleAnalysisInput input, WtReference wtReference, CalibrationResult calibration, BrafMethodSnapshot method) => inner.AnalyzeSamples(input, wtReference, calibration, method);
    }

    private sealed class SyntheticSignalPort(SyntheticPlate plate) : ISignalPreparationPort
    {
        public Task<Result<PreparedRunSignals>> PrepareAsync(PreparedSignalRequest request, IProgress<OperationProgressDto>? progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Result<PreparedRunSignals>.Ok(new PreparedRunSignals(
                request.Source, request.Mapping, plate.BuildSeries(request.Mapping.RunId), [])));
        }
    }

    private sealed class ReadOverrideRepository : IProjectRunRepository
    {
        public InMemoryProjectRunRepository Inner { get; } = new();
        public Func<PersistedCalibration, PersistedCalibration>? CalibrationTransform { get; set; }
        public WellId? MissingRoxWell { get; set; }
        public Func<IReadOnlyList<SignalSeries>, IReadOnlyList<SignalSeries>>? SignalsTransform { get; set; }
        public BrafMethodSnapshot? MethodOverride { get; init; }

        public ValueTask<CreateProjectStoreResult> CreateOrReplayAsync(CreateProjectIdentity identity, Func<ProjectRunAggregate> aggregateFactory, CancellationToken cancellationToken) =>
            Inner.CreateOrReplayAsync(identity, aggregateFactory, cancellationToken);
        public async ValueTask<ProjectRunAggregate?> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken)
        {
            ProjectRunAggregate? aggregate = await Inner.GetByRunIdAsync(runId, cancellationToken);
            return aggregate is not null && MethodOverride is { } method ? aggregate with { Method = method } : aggregate;
        }
        public ValueTask<RunMutationOutcome> MutateAsync(RunMutationRequest request, RunWorkflowPayload payload, CancellationToken cancellationToken) => Inner.MutateAsync(request, payload, cancellationToken);
        public ValueTask<StoredRunMutation?> GetMutationAsync(Guid requestId, CancellationToken cancellationToken) => Inner.GetMutationAsync(requestId, cancellationToken);
        public ValueTask<PreparedSourceIdentity?> GetSourceAsync(Guid runId, CancellationToken cancellationToken) => Inner.GetSourceAsync(runId, cancellationToken);
        public ValueTask<PreparedDataMapping?> GetMappingAsync(Guid runId, CancellationToken cancellationToken) => Inner.GetMappingAsync(runId, cancellationToken);
        public ValueTask<IReadOnlyList<WellAssignment>> GetAssignmentsAsync(Guid runId, CancellationToken cancellationToken) => Inner.GetAssignmentsAsync(runId, cancellationToken);
        public ValueTask<PersistedControl?> GetControlAsync(Guid runId, CancellationToken cancellationToken) => Inner.GetControlAsync(runId, cancellationToken);
        public ValueTask<IReadOnlyList<PersistedSample>> GetSamplesAsync(Guid runId, CancellationToken cancellationToken) => Inner.GetSamplesAsync(runId, cancellationToken);
        public ValueTask<IReadOnlyList<PersistedTrace>> GetTracesAsync(Guid runId, CancellationToken cancellationToken) => Inner.GetTracesAsync(runId, cancellationToken);

        public async ValueTask<IReadOnlyList<SignalSeries>> GetSignalsAsync(Guid runId, CancellationToken cancellationToken)
        {
            IReadOnlyList<SignalSeries> signals = await Inner.GetSignalsAsync(runId, cancellationToken);
            if (SignalsTransform is { } transform) return transform(signals);
            return MissingRoxWell is { } missing
                ? [.. signals.Where(series => series.Well != missing || series.Channel != FluorescenceChannel.Rox)]
                : signals;
        }

        public async ValueTask<PersistedCalibration?> GetCalibrationAsync(Guid runId, CancellationToken cancellationToken)
        {
            PersistedCalibration? calibration = await Inner.GetCalibrationAsync(runId, cancellationToken);
            return calibration is not null && CalibrationTransform is { } transform ? transform(calibration) : calibration;
        }
    }

    private sealed class NullCallAnalysisEngine : IAnalysisEngine
    {
        private readonly BrafAnalysisEngine inner = new();

        public Result<CalibrationResult> Calibrate(CalibrationInput input, BrafMethodSnapshot method) => inner.Calibrate(input, method);
        public Result<ControlQcResult> EvaluateControls(ControlQcInput input, CalibrationResult calibration, BrafMethodSnapshot method) => inner.EvaluateControls(input, calibration, method);
        public Result<SampleAnalysisResult> AnalyzeSamples(SampleAnalysisInput input, WtReference wtReference, CalibrationResult calibration, BrafMethodSnapshot method)
        {
            Result<SampleAnalysisResult> result = inner.AnalyzeSamples(input, wtReference, calibration, method);
            return Result<SampleAnalysisResult>.Ok(new SampleAnalysisResult([.. result.Value!.Results.Select(sample => new SampleResult(
                sample.SampleId, QcStatus.Invalid, call: null, meanDrPrime: null,
                [.. sample.Wells.Select(well => new WellDrPrimeResult(well.Well, well.DrPrime, call: null, well.Trace))],
                ErrorCodes.SampleReplicateClassificationInconsistent))]));
        }
    }
}
