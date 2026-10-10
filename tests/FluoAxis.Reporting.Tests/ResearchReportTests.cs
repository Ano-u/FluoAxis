namespace FluoAxis.Reporting.Tests;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using FluoAxis.Contracts;
using FluoAxis.Contracts.Serialization;
using Xunit;

public sealed class ResearchReportTests
{
    private readonly ResearchReportRenderer renderer = new();

    [Fact]
    public void Snapshot_hash_matches_canonical_json_and_is_stable_for_same_frozen_input()
    {
        ReportSnapshotDto first = Snapshot(ReportFixture.Evidence());
        ReportSnapshotDto second = Snapshot(ReportFixture.Evidence());
        string canonical = renderer.GetCanonicalJson(first);

        Assert.Equal(first.SnapshotSha256, second.SnapshotSha256);
        Assert.Equal(first.ReportId, second.ReportId);
        Assert.Equal(first.SnapshotSha256, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
        Assert.DoesNotContain("snapshotSha256", canonical, StringComparison.Ordinal);
        Assert.Equal(ReportContentDto.DraftApproval, first.Content.ApprovalStatus);
        Assert.NotEqual(first.SnapshotSha256, renderer.CreateSnapshot(ReportFixture.Evidence(), "2.0", ReportFixture.Created).SnapshotSha256);
        Assert.NotEqual(first.SnapshotSha256, renderer.CreateSnapshot(ReportFixture.Evidence(), "1.0", ReportFixture.Created.AddSeconds(1)).SnapshotSha256);
    }

    [Fact]
    public void Snapshot_defensively_freezes_nested_evidence_collections()
    {
        RunEvidenceDto baseline = ReportFixture.Evidence();
        PerWellResultDto[] wells = [new("A1", 0.4, 0, 0.4, CallNames.Positive, "trace-1")];
        var sample = new SampleAnalysisDto("S-1", StatusNames.Pass, CallNames.Positive, 0.4, wells);
        IssueDto[] diagnostics = [new("SYSTEM_STORED_WARNING", "first", "review")];
        var control = new ControlQcResultDto(StatusNames.Pass, 0, 0, CallNames.Positive, 0.6, diagnostics);
        var evidence = new RunEvidenceDto(baseline.ProjectName, baseline.Snapshot, baseline.Mapping, baseline.Assignments,
            baseline.Calibration, control, [sample], baseline.Traces, baseline.ProvenanceCompleteness);
        ReportSnapshotDto snapshot = Snapshot(evidence);
        string before = renderer.GetCanonicalJson(snapshot);

        wells[0] = new PerWellResultDto("A1", 9, 9, 9, CallNames.Negative, "changed");
        diagnostics[0] = new IssueDto("SYSTEM_STORED_WARNING", "changed", "changed");

        Assert.Equal(before, renderer.GetCanonicalJson(snapshot));
        Assert.Equal(0.4, snapshot.Content.Evidence.Samples[0].PerWell[0].DrPrime);
        Assert.Equal("first", snapshot.Content.Evidence.Control!.Issues[0].UserMessage);
    }

    [Fact]
    public void Render_rejects_tampered_snapshot_before_output()
    {
        ReportSnapshotDto valid = Snapshot(ReportFixture.Evidence());
        ReportSnapshotDto tampered = new(valid.Content, new string('0', 64));

        ArgumentException error = Assert.Throws<ArgumentException>(() => renderer.RenderHtml(tampered));
        Assert.Contains("REPORT_SNAPSHOT_HASH_MISMATCH", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Xlsx_rejects_tampered_snapshot_without_writing_destination()
    {
        ReportSnapshotDto valid = Snapshot(ReportFixture.Evidence());
        using var stream = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentException>(() => renderer.RenderXlsxAsync(new(valid.Content, new string('0', 64)), stream, CancellationToken.None));

        Assert.Equal(0, stream.Length);
        Assert.True(stream.CanWrite);
    }

    [Fact]
    public void Html_encodes_all_user_text_and_has_no_executable_user_markup()
    {
        const string hostile = "<img src=x onerror=alert(1)> & \"quotes\"";
        string html = renderer.RenderHtml(Snapshot(ReportFixture.Evidence(project: hostile, sampleId: "<script>alert(2)</script>", notes: hostile)));

        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img src=x", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&lt;img", html, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", html, StringComparison.Ordinal);
        Assert.Contains("RESEARCH DRAFT", html, StringComparison.Ordinal);
        Assert.Contains("storedPartial", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_displays_stored_call_without_recomputing_threshold_or_rounding()
    {
        // Deliberately choose a distance inconsistent with the stored call.
        // A renderer must preserve a supplied backend verdict, never classify.
        string html = renderer.RenderHtml(Snapshot(ReportFixture.Evidence(sampleCall: CallNames.Positive, distance: 0.3999999999999999)));

        Assert.Contains("BRAF V600E 阳性", html, StringComparison.Ordinal);
        Assert.Contains("0.39999999999999991", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_run_hides_formal_sample_calls_and_preserves_reason_and_partial_evidence()
    {
        string html = renderer.RenderHtml(Snapshot(ReportFixture.Evidence(invalid: true)));

        Assert.DoesNotContain("BRAF V600E 阳性", html, StringComparison.Ordinal);
        Assert.DoesNotContain("BRAF V600E 阴性", html, StringComparison.Ordinal);
        Assert.Contains("SAMPLE_REPLICATE_CLASSIFICATION_INCONSISTENCY", html, StringComparison.Ordinal);
        Assert.Contains("重复实验", html, StringComparison.Ordinal);
        Assert.Contains("INVALID", html, StringComparison.Ordinal);
        Assert.Contains("部分证据", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Legacy_missing_traces_remain_unavailable_without_fabricated_values()
    {
        ReportSnapshotDto snapshot = Snapshot(ReportFixture.Evidence(tracesAvailable: false));
        string html = renderer.RenderHtml(snapshot);

        Assert.Empty(snapshot.Content.Evidence.Traces);
        Assert.Contains("unavailable", html, StringComparison.Ordinal);
        Assert.Contains("未保存此阶段", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"bad\")")]
    [InlineData("+SUM(1,2)")]
    [InlineData("-1+2")]
    [InlineData("@SUM(A1)")]
    [InlineData(" \t=1+1")]
    public async Task Xlsx_text_formula_injection_is_neutralized_and_xml_is_valid(string input)
    {
        using var output = new MemoryStream();
        await renderer.RenderXlsxAsync(Snapshot(ReportFixture.Evidence(project: input, sampleId: "sample<&>", notes: input)), output, CancellationToken.None);
        Assert.True(output.CanWrite);
        output.Position = 0;
        using SpreadsheetDocument workbook = SpreadsheetDocument.Open(output, false);

        Assert.Empty(new OpenXmlValidator().Validate(workbook, TestContext.Current.CancellationToken));
        IEnumerable<Cell> cells = workbook.WorkbookPart!.WorksheetParts.SelectMany(part => part.Worksheet!.Descendants<Cell>());
        Assert.All(cells, cell => Assert.Null(cell.CellFormula));
        Assert.Contains(cells, cell => cell.DataType?.Value == CellValues.InlineString && cell.InlineString?.InnerText == "'" + input);
        Assert.Contains(cells, cell => cell.InlineString?.InnerText == "sample<&>");
        Assert.DoesNotContain(workbook.Parts, part => part.OpenXmlPart.ExternalRelationships.Any());
    }

    [Fact]
    public async Task Same_snapshot_produces_identical_html_and_xlsx_bytes()
    {
        ReportSnapshotDto snapshot = Snapshot(ReportFixture.Evidence());
        string firstHtml = renderer.RenderHtml(snapshot);
        using var first = new MemoryStream();
        using var second = new MemoryStream();
        await renderer.RenderXlsxAsync(snapshot, first, CancellationToken.None);
        await renderer.RenderXlsxAsync(snapshot, second, CancellationToken.None);

        Assert.Equal(firstHtml, renderer.RenderHtml(snapshot));
        Assert.Equal(first.ToArray(), second.ToArray());
        Assert.Contains(snapshot.SnapshotSha256, firstHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Canceled_export_leaves_destination_empty_and_owned_by_caller()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        using var output = new MemoryStream();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => renderer.RenderXlsxAsync(Snapshot(ReportFixture.Evidence()), output, canceled.Token));

        Assert.Equal(0, output.Length);
        Assert.True(output.CanWrite);
    }

    [Fact]
    public void Snapshot_roundtrips_with_source_generated_json_without_machine_paths()
    {
        ReportSnapshotDto snapshot = Snapshot(ReportFixture.Evidence());
        var context = new ReportJsonContext(new JsonSerializerOptions(ReportJsonContext.WireOptions));
        string json = JsonSerializer.Serialize(snapshot, context.ReportSnapshotDto);
        ReportSnapshotDto restored = JsonSerializer.Deserialize(json, context.ReportSnapshotDto)!;

        Assert.Equal(renderer.GetCanonicalJson(snapshot), renderer.GetCanonicalJson(restored));
        Assert.Contains("data/raw/instrument.csv", json, StringComparison.Ordinal);
        Assert.DoesNotContain("rawRfu", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(MachinePaths))]
    public void Free_text_machine_paths_are_rejected_without_echoing_the_path(string path)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => Snapshot(ReportFixture.Evidence(notes: $"source {path}")));

        Assert.Contains("REPORT_MACHINE_PATH_REJECTED", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(path, error.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string> MachinePaths() => new()
    {
        string.Concat("C", ":", "\\", "data", "\\", "source.xlsx"),
        string.Concat("\\", "\\", "server", "\\", "share", "\\", "data.csv"),
        string.Concat("/", "home", "/", "researcher", "/", "source.csv"),
        string.Concat("fi", "le:", "/", "/", "/", "source.csv"),
    };

    [Fact]
    public void Https_reference_is_safe_text_and_is_not_a_live_link()
    {
        string html = renderer.RenderHtml(Snapshot(ReportFixture.Evidence(notes: "https://example.invalid/research/source")));
        Assert.Contains("https://example.invalid/research/source", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Export_receipt_contains_only_safe_display_identity()
    {
        ReportSnapshotDto snapshot = Snapshot(ReportFixture.Evidence());
        var receipt = new ReportExportDto("research-report.xlsx", "xlsx", new string('c', 64), 42, snapshot.SnapshotSha256);
        Assert.Equal("research-report.xlsx", receipt.DisplayName);
        Assert.Throws<ArgumentException>(() => new ReportExportDto("folder/report.xlsx", "xlsx", new string('c', 64), 42, snapshot.SnapshotSha256));
        Assert.Throws<ArgumentException>(() => new ReportExportDto("report.pdf", "xlsx", new string('c', 64), 42, snapshot.SnapshotSha256));
    }

    [Fact]
    public void Report_limits_reject_oversized_text_and_mismatched_trace_method_without_truncation()
    {
        Assert.Throws<ArgumentException>(() => Snapshot(ReportFixture.Evidence(notes: new string('x', 16_385))));
        RunEvidenceDto evidence = ReportFixture.Evidence();
        CalculationTraceDto trace = evidence.Traces[0];
        var wrongMethod = new CalculationTraceDto(trace.TraceId, trace.Algorithm, new string('c', 64), trace.Well, trace.Channel,
            trace.SourcePointIndex, trace.IntermediateValues, trace.ComparisonOperator, trace.ThresholdRaw, trace.ResultRaw, trace.FinalStatus);
        var mismatched = new RunEvidenceDto(evidence.ProjectName, evidence.Snapshot, evidence.Mapping, evidence.Assignments,
            evidence.Calibration, evidence.Control, evidence.Samples, [wrongMethod], evidence.ProvenanceCompleteness);

        ArgumentException error = Assert.Throws<ArgumentException>(() => Snapshot(mismatched));

        Assert.Contains("REPORT_METHOD_IDENTITY_MISMATCH", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Aggregate_text_budget_is_rejected_before_snapshot_clone_or_json_materialization()
    {
        RunEvidenceDto evidence = ReportFixture.Evidence();
        string individuallyAllowedName = new('x', 16_384);
        TraceValueDto[] values = [.. Enumerable.Range(0, 65).Select(_ => new TraceValueDto(individuallyAllowedName, 1))];
        CalculationTraceDto baseline = evidence.Traces[0];
        var oversized = new CalculationTraceDto(baseline.TraceId, baseline.Algorithm, baseline.MethodSnapshotHash,
            baseline.Well, baseline.Channel, baseline.SourcePointIndex, values, baseline.ComparisonOperator,
            baseline.ThresholdRaw, baseline.ResultRaw, baseline.FinalStatus);
        var boundedCounts = new RunEvidenceDto(evidence.ProjectName, evidence.Snapshot, evidence.Mapping,
            evidence.Assignments, evidence.Calibration, evidence.Control, evidence.Samples, [oversized], evidence.ProvenanceCompleteness);

        ArgumentException error = Assert.Throws<ArgumentException>(() => Snapshot(boundedCounts));

        Assert.Equal("REPORT_TEXT_BUDGET_EXCEEDED", error.Message);
        Assert.Equal(65, boundedCounts.Traces[0].IntermediateValues.Count);
        Assert.All(boundedCounts.Traces[0].IntermediateValues, value => Assert.Equal(16_384, value.Name.Length));
    }

    [Fact]
    public async Task Report_generation_records_a_bounded_in_memory_measurement()
    {
        ReportSnapshotDto snapshot = Snapshot(ReportFixture.Evidence());
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        string html = renderer.RenderHtml(snapshot);
        using var xlsx = new MemoryStream();
        await renderer.RenderXlsxAsync(snapshot, xlsx, CancellationToken.None);
        timer.Stop();

        Assert.True(Encoding.UTF8.GetByteCount(html) < 16 * 1024 * 1024);
        Assert.True(xlsx.Length < 16 * 1024 * 1024);
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Report render: {timer.Elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture)} ms; HTML {Encoding.UTF8.GetByteCount(html)} bytes; XLSX {xlsx.Length} bytes; process-wide allocation delta {GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore} (includes concurrent tests); .NET {Environment.Version}; logical CPUs {Environment.ProcessorCount}; snapshot {snapshot.SnapshotSha256}; no SLA.");
    }

    private ReportSnapshotDto Snapshot(RunEvidenceDto evidence) => renderer.CreateSnapshot(evidence, "1.0", ReportFixture.Created);
}
