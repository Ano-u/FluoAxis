namespace FluoAxis.Application.Tests;

using System.Text.Json;
using FluoAxis.Analysis;
using FluoAxis.Application;
using FluoAxis.Contracts;
using FluoAxis.Contracts.Serialization;
using FluoAxis.Domain;
using Xunit;

public sealed class RunEvidenceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task New_run_has_empty_evidence_without_loading_signals_or_running_analysis()
    {
        var repository = new EvidenceRepository();
        ApplicationGateway gateway = Gateway(repository, SyntheticPlate.Passing());
        Guid runId = await CreateAsync(gateway);
        repository.RefuseSignalReads = true;
        ApplicationGateway readOnlyGateway = new(repository, null, null, null, new NoAnalysisEngine());

        GatewayResult<RunEvidenceDto> result = await readOnlyGateway.GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), runId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Evidence project", result.Value!.ProjectName);
        Assert.Equal(0, result.Value.Snapshot.Revision);
        Assert.Null(result.Value.Mapping);
        Assert.Empty(result.Value.Assignments);
        Assert.Null(result.Value.Calibration);
        Assert.Null(result.Value.Control);
        Assert.Empty(result.Value.Samples);
        Assert.Empty(result.Value.Traces);
        Assert.Equal(ProvenanceCompletenessNames.Unavailable, result.Value.ProvenanceCompleteness);
    }

    [Fact]
    public async Task Evidence_tracks_each_persisted_stage_and_preserves_trace_values_without_recalculation()
    {
        var repository = new EvidenceRepository();
        SyntheticPlate plate = SyntheticPlate.Passing();
        ApplicationGateway gateway = Gateway(repository, plate);
        Guid runId = await CreateAsync(gateway);

        Assert.True((await gateway.ConfirmMappingAsync(plate.MappingCommand(runId, 0), CancellationToken.None)).IsSuccess);
        RunEvidenceDto imported = await EvidenceAsync(gateway, runId);
        Assert.Equal(1, imported.Snapshot.Revision);
        Assert.Equal("FAM", imported.Mapping!.Fam.SheetName);
        Assert.Equal("ROX", imported.Mapping.Rox.SheetName);
        Assert.Empty(imported.Assignments);

        Assert.True((await gateway.AssignWellsAsync(plate.AssignCommand(runId, 1), CancellationToken.None)).IsSuccess);
        RunEvidenceDto assigned = await EvidenceAsync(gateway, runId);
        Assert.Equal(2, assigned.Snapshot.Revision);
        Assert.Equal(8, assigned.Assignments.Count);
        Assert.Contains(assigned.Assignments, assignment => assignment.SampleId == "S-POS" && assignment.ReplicateIndex == 2);

        Assert.True((await gateway.RunCalibrationAsync(plate.CalibrationCommand(runId, 2), CancellationToken.None)).IsSuccess);
        RunEvidenceDto calibrated = await EvidenceAsync(gateway, runId);
        Assert.Equal(3, calibrated.Snapshot.Revision);
        Assert.Equal(StatusNames.Pass, calibrated.Calibration!.Status);
        Assert.Null(calibrated.Control);

        Assert.True((await gateway.RunControlQcAsync(plate.ControlCommand(runId, 3), CancellationToken.None)).IsSuccess);
        RunEvidenceDto controlled = await EvidenceAsync(gateway, runId);
        Assert.Equal(4, controlled.Snapshot.Revision);
        Assert.Equal(StatusNames.Pass, controlled.Control!.Status);
        Assert.NotEmpty(controlled.Traces);
        Assert.Empty(controlled.Samples);

        Assert.True((await gateway.AnalyzeSamplesAsync(plate.AnalysisCommand(runId, 4), CancellationToken.None)).IsSuccess);
        IReadOnlyList<PersistedTrace> stored = await repository.Inner.GetTracesAsync(runId, CancellationToken.None);
        repository.RefuseSignalReads = true;
        ApplicationGateway readOnlyGateway = new(repository, null, null, null, new NoAnalysisEngine());
        RunEvidenceDto analyzed = await EvidenceAsync(readOnlyGateway, runId);
        Assert.Equal(5, analyzed.Snapshot.Revision);
        Assert.Equal(RunStateNames.Analyzed, analyzed.Snapshot.DomainState);
        Assert.Equal(2, analyzed.Samples.Count);
        Assert.Equal(stored.Count, analyzed.Traces.Count);
        Assert.Equal(controlled.Traces.Count + analyzed.Samples.Sum(sample => sample.PerWell.Count), analyzed.Traces.Count);
        foreach (CalculationTraceDto lqcTrace in controlled.Traces)
        {
            CalculationTraceDto preserved = Assert.Single(analyzed.Traces, trace => trace.TraceId == lqcTrace.TraceId);
            Assert.Equal(JsonSerializer.Serialize(lqcTrace), JsonSerializer.Serialize(preserved));
        }
        Assert.Equal(ProvenanceCompletenessNames.StoredPartial, analyzed.ProvenanceCompleteness);
        Assert.All(analyzed.Traces, trace => Assert.Equal(ProvenanceCompletenessNames.StoredPartial, trace.ProvenanceCompleteness));
        foreach (PersistedTrace expected in stored)
        {
            CalculationTraceDto actual = Assert.Single(analyzed.Traces, trace => trace.TraceId == expected.TraceId);
            Assert.Equal(expected.Algorithm, actual.Algorithm);
            Assert.Equal(expected.MethodSnapshotHash, actual.MethodSnapshotHash);
            Assert.Equal(expected.SourcePointIndex, actual.SourcePointIndex);
            Assert.Equal(expected.ResultRaw, actual.ResultRaw);
            Assert.Equal(expected.IntermediateValues.Select(value => value.Name), actual.IntermediateValues.Select(value => value.Name));
            Assert.Equal(expected.IntermediateValues.Select(value => value.Value), actual.IntermediateValues.Select(value => value.Value));
        }
    }

    [Fact]
    public async Task In_memory_trace_upsert_replaces_the_same_id_without_removing_other_stage_evidence()
    {
        var repository = new EvidenceRepository();
        SyntheticPlate plate = SyntheticPlate.Passing();
        ApplicationGateway gateway = Gateway(repository, plate);
        Guid runId = await PrepareAsync(gateway, plate, 4);
        PersistedTrace original = Assert.Single(await repository.Inner.GetTracesAsync(runId, CancellationToken.None));
        PersistedTrace replacement = original with { SourcePointIndex = null };
        PersistedTrace additional = original with { TraceId = Guid.NewGuid().ToString("D"), Well = "B1" };

        RunMutationOutcome committed = await repository.Inner.MutateAsync(
            new RunMutationRequest(Guid.NewGuid(), runId, 4, RunState.ReadyForAnalysis, RunState.Analyzed,
                "researcher", Start, ContractVersion.Current, "run.analyzed"),
            new RunWorkflowPayload { Traces = [replacement, additional] }, CancellationToken.None);

        Assert.True(committed.IsSuccess);
        IReadOnlyList<PersistedTrace> traces = await repository.Inner.GetTracesAsync(runId, CancellationToken.None);
        Assert.Equal(2, traces.Count);
        Assert.Equal(replacement, Assert.Single(traces, trace => trace.TraceId == original.TraceId));
        Assert.Equal(additional, Assert.Single(traces, trace => trace.TraceId == additional.TraceId));
    }

    [Fact]
    public async Task Reopened_sample_conflict_exposes_stored_reason_and_action_without_recalculation()
    {
        var repository = new EvidenceRepository();
        SyntheticPlate plate = SyntheticPlate.Passing() with { MixedSampleReplicates = true };
        ApplicationGateway gateway = Gateway(repository, plate);
        Guid runId = await PrepareAsync(gateway, plate, 5);
        GatewayResult<RunSnapshotDto> snapshot = await gateway.GetRunAsync(new GetRunQuery(Guid.NewGuid(), runId), CancellationToken.None);
        Assert.True(snapshot.IsSuccess);
        IssueDto issue = Assert.Single(snapshot.Value!.BlockingIssues);
        Assert.Equal(ErrorCodes.SampleReplicateClassificationInconsistent, issue.Code);
        Assert.Contains("S-POS", issue.UserMessage, StringComparison.Ordinal);
        Assert.Contains("Repeat the experiment", issue.RecommendedAction, StringComparison.Ordinal);

        repository.RefuseSignalReads = true;
        ApplicationGateway reopened = new(repository, null, null, null, new NoAnalysisEngine());
        RunEvidenceDto evidence = await EvidenceAsync(reopened, runId);

        Assert.Equal(RunStateNames.RunInvalid, evidence.Snapshot.DomainState);
        Assert.Equal(JsonSerializer.Serialize(issue), JsonSerializer.Serialize(Assert.Single(evidence.Snapshot.BlockingIssues)));
        Assert.Null(Assert.Single(evidence.Samples, sample => sample.SampleId == "S-POS").Call);
    }

    [Theory]
    [InlineData("calibration")]
    [InlineData("control")]
    [InlineData("sample")]
    public async Task Invalid_run_retains_stored_failure_evidence(string stage)
    {
        var repository = new EvidenceRepository();
        SyntheticPlate plate = stage switch
        {
            "calibration" => SyntheticPlate.Passing() with { PositivePlateaus = false },
            "control" => SyntheticPlate.Passing() with { MixedLqcReplicates = true },
            _ => SyntheticPlate.Passing() with { MixedSampleReplicates = true },
        };
        ApplicationGateway gateway = Gateway(repository, plate);
        Guid runId = await PrepareAsync(gateway, plate, stage == "calibration" ? 3 : stage == "control" ? 4 : 5);

        RunEvidenceDto evidence = await EvidenceAsync(gateway, runId);

        Assert.Equal(RunStateNames.RunInvalid, evidence.Snapshot.DomainState);
        Assert.Equal([RunAction.ViewExplorer], evidence.Snapshot.AllowedActions);
        if (stage == "calibration")
        {
            Assert.Equal(StatusNames.Fail, evidence.Calibration!.Status);
            Assert.NotEmpty(evidence.Calibration.PerWellDiagnostics);
        }
        else if (stage == "control")
        {
            Assert.Equal(StatusNames.Invalid, evidence.Control!.Status);
            Assert.NotEmpty(evidence.Control.Issues);
        }
        else
        {
            SampleAnalysisDto invalid = Assert.Single(evidence.Samples, sample => sample.Status == StatusNames.Invalid);
            Assert.Null(invalid.Call);
            Assert.Null(invalid.MeanDrPrime);
        }
    }

    [Fact]
    public async Task Older_records_without_traces_remain_readable_without_fabricated_provenance()
    {
        var repository = new EvidenceRepository();
        SyntheticPlate plate = SyntheticPlate.Passing();
        ApplicationGateway gateway = Gateway(repository, plate);
        Guid runId = await PrepareAsync(gateway, plate, 5);
        repository.OmitTraces = true;
        repository.RefuseSignalReads = true;

        RunEvidenceDto evidence = await EvidenceAsync(gateway, runId);

        Assert.Equal(2, evidence.Samples.Count);
        Assert.Empty(evidence.Traces);
        Assert.Equal(ProvenanceCompletenessNames.Unavailable, evidence.ProvenanceCompleteness);
        Assert.NotEmpty(evidence.Samples.SelectMany(sample => sample.PerWell).Select(well => well.TraceId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Evidence_rejects_calibration_or_trace_from_a_different_method(bool corruptCalibration)
    {
        var repository = new EvidenceRepository();
        SyntheticPlate plate = SyntheticPlate.Passing();
        ApplicationGateway gateway = Gateway(repository, plate);
        Guid runId = await PrepareAsync(gateway, plate, 5);
        repository.CorruptCalibrationHash = corruptCalibration;
        repository.CorruptTraceHash = !corruptCalibration;

        GatewayResult<RunEvidenceDto> result = await gateway.GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), runId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(ErrorCodes.SystemInvalidRunState, Assert.Single(result.Errors).Code);
        Assert.Equal(5, (await repository.Inner.GetByRunIdAsync(runId, CancellationToken.None))!.RunRevision);
    }

    [Fact]
    public async Task Missing_individual_legacy_trace_stays_partial_without_reconstructing_it()
    {
        var repository = new EvidenceRepository();
        SyntheticPlate plate = SyntheticPlate.Passing();
        ApplicationGateway gateway = Gateway(repository, plate);
        Guid runId = await PrepareAsync(gateway, plate, 5);
        IReadOnlyList<PersistedTrace> stored = await repository.Inner.GetTracesAsync(runId, CancellationToken.None);
        repository.OmittedTraceId = stored.First(trace => trace.Well == "B1").TraceId;

        RunEvidenceDto evidence = await EvidenceAsync(gateway, runId);

        Assert.Equal(stored.Count - 1, evidence.Traces.Count);
        Assert.Equal(ProvenanceCompletenessNames.StoredPartial, evidence.ProvenanceCompleteness);
        Assert.DoesNotContain(evidence.Traces, trace => trace.TraceId == repository.OmittedTraceId);
        Assert.Contains(evidence.Samples.SelectMany(sample => sample.PerWell), well => well.TraceId == repository.OmittedTraceId);
    }

    [Fact]
    public async Task Revision_change_during_read_returns_stale_without_retry_or_mixed_evidence()
    {
        var repository = new EvidenceRepository();
        SyntheticPlate plate = SyntheticPlate.Passing();
        ApplicationGateway gateway = Gateway(repository, plate);
        Guid runId = await CreateAsync(gateway);
        repository.BeforeTraceReturn = async () =>
            Assert.True((await gateway.ConfirmMappingAsync(plate.MappingCommand(runId, 0), CancellationToken.None)).IsSuccess);

        GatewayResult<RunEvidenceDto> result = await gateway.GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), runId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(ErrorCodes.SystemStaleRunRevision, Assert.Single(result.Errors).Code);
        Assert.Equal(1, repository.TraceReads);
    }

    [Fact]
    public async Task Evidence_source_generated_json_preserves_order_and_contains_only_relative_source_identity()
    {
        var repository = new EvidenceRepository();
        SyntheticPlate plate = SyntheticPlate.Passing();
        ApplicationGateway gateway = Gateway(repository, plate);
        Guid runId = await PrepareAsync(gateway, plate, 5);
        RunEvidenceDto evidence = await EvidenceAsync(gateway, runId);
        var context = new ContractJsonContext(new JsonSerializerOptions(ContractJsonContext.WireOptions));

        string json = JsonSerializer.Serialize(evidence, context.RunEvidenceDto);
        RunEvidenceDto? roundTrip = JsonSerializer.Deserialize(json, context.RunEvidenceDto);

        Assert.NotNull(roundTrip);
        Assert.Equal(json, JsonSerializer.Serialize(roundTrip, context.RunEvidenceDto));
        Assert.Equal("data/raw/synthetic-run.xlsx", Assert.Single(roundTrip.Snapshot.SourceFiles).RelativeResourcePath);
        // Selected endpoints now carry exact source coordinates and raw RFU;
        // complete time-course arrays remain a separate bounded query.
        Assert.Contains("rawRfu", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"qcPoints\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"windowPoints\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signals", json, StringComparison.OrdinalIgnoreCase);
        Assert.All(roundTrip.Snapshot.SourceFiles, source => Assert.False(Path.IsPathFullyQualified(source.RelativeResourcePath)));
        Assert.Equal(evidence.Traces[0].IntermediateValues.Select(value => value.Name), roundTrip.Traces[0].IntermediateValues.Select(value => value.Name));
    }

    [Fact]
    public async Task Evidence_not_found_version_and_cancellation_use_gateway_contracts()
    {
        ApplicationGateway gateway = Gateway(new EvidenceRepository(), SyntheticPlate.Passing());
        GatewayResult<RunEvidenceDto> missing = await gateway.GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);
        Assert.Equal(ErrorCodes.ProjectRunNotFound, Assert.Single(missing.Errors).Code);
        GatewayResult<RunEvidenceDto> unsupported = await gateway.GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), Guid.NewGuid(), 999), CancellationToken.None);
        Assert.Equal(ErrorCodes.SystemUnsupportedContractVersion, Assert.Single(unsupported.Errors).Code);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gateway.GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), Guid.NewGuid()), canceled.Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Z9")]
    public async Task Scientific_point_page_rejects_invalid_wells_with_a_structured_error(string? well)
    {
        ApplicationGateway gateway = Gateway(new EvidenceRepository(), SyntheticPlate.Passing());
        GatewayResult<ScientificPointPageDto> result = await gateway.GetScientificPointPageAsync(
            Guid.NewGuid(), "calibration", well!, ChannelNames.Fam, 0, 250, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.PlateRoleCardinality, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void Trace_dto_keeps_missing_source_index_and_snapshots_ordered_values()
    {
        TraceValueDto[] values = [new("Y_FAM", 0.12345678901234566), new("Y_ROX", 0.25)];
        CalculationTraceDto trace = new(Guid.NewGuid().ToString("D"), "BRAF-ENGINE-1.0", new string('a', 64),
            "B1", ChannelNames.Fam, null, values, ">=", 0.4, 0.5, StatusNames.Pass);
        values[0] = new TraceValueDto("changed", 99);

        Assert.Null(trace.SourcePointIndex);
        Assert.Equal("Y_FAM", trace.IntermediateValues[0].Name);
        Assert.Equal(0.12345678901234566, trace.IntermediateValues[0].Value);
        Assert.Equal(ProvenanceCompletenessNames.StoredPartial, trace.ProvenanceCompleteness);
        Assert.Throws<ArgumentException>(() => new CalculationTraceDto(Guid.NewGuid().ToString("D"), "BRAF-ENGINE-1.0", new string('a', 64),
            "B1", ChannelNames.Fam, null, values, ">=", 0.4, 0.5, StatusNames.Pass, "complete"));
    }

    private static ApplicationGateway Gateway(EvidenceRepository repository, SyntheticPlate plate) =>
        new(repository, null, null, new SignalPort(plate), new BrafAnalysisEngine());

    private static async Task<Guid> CreateAsync(ApplicationGateway gateway)
    {
        GatewayResult<ProjectSnapshotDto> result = await gateway.CreateProjectAsync(
            new CreateProjectCommand(Guid.NewGuid(), "Evidence project", "researcher", Start), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!.RunId;
    }

    private static async Task<Guid> PrepareAsync(ApplicationGateway gateway, SyntheticPlate plate, int revision)
    {
        Guid runId = await CreateAsync(gateway);
        Assert.True((await gateway.ConfirmMappingAsync(plate.MappingCommand(runId, 0), CancellationToken.None)).IsSuccess);
        Assert.True((await gateway.AssignWellsAsync(plate.AssignCommand(runId, 1), CancellationToken.None)).IsSuccess);
        if (revision >= 3) Assert.True((await gateway.RunCalibrationAsync(plate.CalibrationCommand(runId, 2), CancellationToken.None)).IsSuccess);
        if (revision >= 4) Assert.True((await gateway.RunControlQcAsync(plate.ControlCommand(runId, 3), CancellationToken.None)).IsSuccess);
        if (revision >= 5) Assert.True((await gateway.AnalyzeSamplesAsync(plate.AnalysisCommand(runId, 4), CancellationToken.None)).IsSuccess);
        return runId;
    }

    private static async Task<RunEvidenceDto> EvidenceAsync(ApplicationGateway gateway, Guid runId)
    {
        GatewayResult<RunEvidenceDto> result = await gateway.GetRunEvidenceAsync(new GetRunQuery(Guid.NewGuid(), runId), CancellationToken.None);
        Assert.True(result.IsSuccess, string.Join(",", result.Errors.Select(error => error.Code)));
        return result.Value!;
    }

    private sealed class SignalPort(SyntheticPlate plate) : ISignalPreparationPort
    {
        public Task<Result<PreparedRunSignals>> PrepareAsync(PreparedSignalRequest request, IProgress<OperationProgressDto>? progress, CancellationToken cancellationToken) =>
            Task.FromResult(Result<PreparedRunSignals>.Ok(new PreparedRunSignals(request.Source, request.Mapping, plate.BuildSeries(request.Mapping.RunId), [])));
    }

    private sealed class NoAnalysisEngine : IAnalysisEngine
    {
        public Result<CalibrationResult> Calibrate(CalibrationInput input, BrafMethodSnapshot method) => throw new InvalidOperationException("Evidence reads must not calculate.");
        public Result<ControlQcResult> EvaluateControls(ControlQcInput input, CalibrationResult calibration, BrafMethodSnapshot method) => throw new InvalidOperationException("Evidence reads must not calculate.");
        public Result<SampleAnalysisResult> AnalyzeSamples(SampleAnalysisInput input, WtReference wtReference, CalibrationResult calibration, BrafMethodSnapshot method) => throw new InvalidOperationException("Evidence reads must not calculate.");
    }

    private sealed class EvidenceRepository : IProjectRunRepository
    {
        public InMemoryProjectRunRepository Inner { get; } = new();
        public bool RefuseSignalReads { get; set; }
        public bool OmitTraces { get; set; }
        public string? OmittedTraceId { get; set; }
        public bool CorruptCalibrationHash { get; set; }
        public bool CorruptTraceHash { get; set; }
        public Func<Task>? BeforeTraceReturn { get; set; }
        public int TraceReads { get; private set; }
        public ValueTask<CreateProjectStoreResult> CreateOrReplayAsync(CreateProjectIdentity identity, Func<ProjectRunAggregate> aggregateFactory, CancellationToken cancellationToken) => Inner.CreateOrReplayAsync(identity, aggregateFactory, cancellationToken);
        public ValueTask<ProjectRunAggregate?> GetByRunIdAsync(Guid runId, CancellationToken cancellationToken) => Inner.GetByRunIdAsync(runId, cancellationToken);
        public ValueTask<RunMutationOutcome> MutateAsync(RunMutationRequest request, RunWorkflowPayload payload, CancellationToken cancellationToken) => Inner.MutateAsync(request, payload, cancellationToken);
        public ValueTask<StoredRunMutation?> GetMutationAsync(Guid requestId, CancellationToken cancellationToken) => Inner.GetMutationAsync(requestId, cancellationToken);
        public ValueTask<PreparedSourceIdentity?> GetSourceAsync(Guid runId, CancellationToken cancellationToken) => Inner.GetSourceAsync(runId, cancellationToken);
        public ValueTask<IReadOnlyList<SignalSeries>> GetSignalsAsync(Guid runId, CancellationToken cancellationToken) => RefuseSignalReads ? throw new InvalidOperationException("Evidence reads must not load Raw RFU.") : Inner.GetSignalsAsync(runId, cancellationToken);
        public ValueTask<PreparedDataMapping?> GetMappingAsync(Guid runId, CancellationToken cancellationToken) => Inner.GetMappingAsync(runId, cancellationToken);
        public ValueTask<IReadOnlyList<WellAssignment>> GetAssignmentsAsync(Guid runId, CancellationToken cancellationToken) => Inner.GetAssignmentsAsync(runId, cancellationToken);
        public ValueTask<IReadOnlyList<AnalysisStageEvidenceDto>> GetScientificStagesAsync(Guid runId, CancellationToken cancellationToken) => Inner.GetScientificStagesAsync(runId, cancellationToken);
        public async ValueTask<PersistedCalibration?> GetCalibrationAsync(Guid runId, CancellationToken cancellationToken)
        {
            PersistedCalibration? calibration = await Inner.GetCalibrationAsync(runId, cancellationToken);
            return CorruptCalibrationHash && calibration is not null ? calibration with { MethodSnapshotHash = new string('0', 64) } : calibration;
        }
        public ValueTask<PersistedControl?> GetControlAsync(Guid runId, CancellationToken cancellationToken) => Inner.GetControlAsync(runId, cancellationToken);
        public ValueTask<IReadOnlyList<PersistedSample>> GetSamplesAsync(Guid runId, CancellationToken cancellationToken) => Inner.GetSamplesAsync(runId, cancellationToken);
        public async ValueTask<IReadOnlyList<PersistedTrace>> GetTracesAsync(Guid runId, CancellationToken cancellationToken)
        {
            TraceReads++;
            if (BeforeTraceReturn is { } mutation) await mutation();
            if (OmitTraces) return [];
            IReadOnlyList<PersistedTrace> traces = await Inner.GetTracesAsync(runId, cancellationToken);
            return [.. traces.Where(trace => trace.TraceId != OmittedTraceId)
                .Select(trace => CorruptTraceHash ? trace with { MethodSnapshotHash = new string('0', 64) } : trace)];
        }
    }
}
