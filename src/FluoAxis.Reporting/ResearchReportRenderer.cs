namespace FluoAxis.Reporting;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluoAxis.Contracts;
using FluoAxis.Contracts.Serialization;

/// <summary>Creates technical RUO drafts solely from stored evidence. No Analysis implementation is referenced.</summary>
public sealed partial class ResearchReportRenderer
{
    private readonly ReportJsonContext jsonContext = new(new JsonSerializerOptions(ReportJsonContext.WireOptions));

    public ReportSnapshotDto CreateSnapshot(RunEvidenceDto evidence, string softwareVersion, DateTimeOffset createdUtc,
        IReadOnlyList<ReportSignalFigureDto>? signalFigures = null, ReportPlotFigureDto? plotFigure = null)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (signalFigures?.Count > 12) throw new ArgumentException("REPORT_SIGNAL_FIGURE_LIMIT_EXCEEDED", nameof(signalFigures));
        ValidateEvidence(evidence, softwareVersion);
        ReportContentDto content = new(softwareVersion, createdUtc, evidence, signalFigures: signalFigures, plotFigure: plotFigure);
        byte[] canonical = CanonicalBytes(content);
        return new ReportSnapshotDto(content, Hash(canonical));
    }

    /// <summary>Canonical UTF-8 JSON uses ordinal property order; array order is frozen at creation and trace intermediates retain stored order.</summary>
    public string GetCanonicalJson(ReportSnapshotDto snapshot) => Encoding.UTF8.GetString(ValidateSnapshot(snapshot));

    private byte[] ValidateSnapshot(ReportSnapshotDto snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateEvidence(snapshot.Content.Evidence, snapshot.Content.SoftwareVersion);
        byte[] canonical = CanonicalBytes(snapshot.Content);
        if (!string.Equals(Hash(canonical), snapshot.SnapshotSha256, StringComparison.Ordinal))
            throw new ArgumentException("REPORT_SNAPSHOT_HASH_MISMATCH", nameof(snapshot));
        return canonical;
    }

    private byte[] CanonicalBytes(ReportContentDto content)
    {
        JsonElement element = JsonSerializer.SerializeToElement(content, jsonContext.ReportContentDto);
        ReportSafety.ValidateTextTree(element);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, element);
        if (stream.Length > ReportSafety.MaxCanonicalBytes) throw new ArgumentException("REPORT_SNAPSHOT_LIMIT_EXCEEDED");
        return stream.ToArray();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (JsonProperty property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (JsonElement value in element.EnumerateArray()) WriteCanonical(writer, value);
            writer.WriteEndArray();
        }
        else element.WriteTo(writer);
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void ValidateEvidence(RunEvidenceDto evidence, string softwareVersion)
    {
        if (evidence.Assignments.Count > 96 || evidence.Samples.Count > 96 || evidence.Traces.Count > 192
            || evidence.Snapshot.SourceFiles.Count > 32 || evidence.Snapshot.BlockingIssues.Count > 1024
            || evidence.Snapshot.WarningIssues.Count > 1024 || (evidence.Calibration?.PerWellDiagnostics.Count ?? 0) > 1024
            || (evidence.Control?.Issues.Count ?? 0) > 1024 || evidence.Samples.Sum(sample => sample.PerWell.Count) > 96
            || evidence.Traces.Sum(trace => trace.IntermediateValues.Count) > 24_576)
            throw new ArgumentException("REPORT_EVIDENCE_LIMIT_EXCEEDED", nameof(evidence));
        string methodHash = evidence.Snapshot.MethodSnapshotHash;
        if (evidence.Traces.Any(trace => !string.Equals(trace.MethodSnapshotHash, methodHash, StringComparison.Ordinal)))
            throw new ArgumentException("REPORT_METHOD_IDENTITY_MISMATCH", nameof(evidence));
        foreach (AnalysisInputIdentityDto identity in (evidence.ScientificEvidence?.Stages ?? []).Select(stage => stage.Identity)
            .Concat(evidence.Traces.Where(trace => trace.Provenance is not null).Select(trace => trace.Provenance!.Identity)))
        {
            if (identity.MethodSnapshotHash != methodHash || identity.InputRevision > evidence.Snapshot.Revision
                || !evidence.Snapshot.SourceFiles.Any(source => source.Sha256 == identity.SourceSha256))
                throw new ArgumentException("REPORT_INPUT_IDENTITY_MISMATCH", nameof(evidence));
        }
        // Check the DTO tree before cloning collections or allocating a JSON
        // document. Bounded field counts alone do not bound aggregate text.
        ReportSafety.ValidateTextBudget(EvidenceText(evidence, softwareVersion));
    }

    private static IEnumerable<string?> EvidenceText(RunEvidenceDto evidence, string softwareVersion)
    {
        yield return softwareVersion;
        yield return ReportContentDto.DraftApproval;
        yield return ReportContentDto.RequiredResearchNotice;
        yield return evidence.ProjectName;
        yield return evidence.ProvenanceCompleteness;
        RunSnapshotDto run = evidence.Snapshot;
        yield return run.DomainState;
        yield return run.MethodId;
        yield return run.MethodVersion;
        yield return run.MethodSnapshotHash;
        foreach (string action in run.AllowedActions) yield return action;
        foreach (string? text in IssuesText(run.BlockingIssues)) yield return text;
        foreach (string? text in IssuesText(run.WarningIssues)) yield return text;
        foreach (SourceFileDto source in run.SourceFiles)
        {
            yield return source.OriginalFileName;
            yield return source.RelativeResourcePath;
            yield return source.Sha256;
        }
        if (run.MappingSummary is { } summary)
            foreach (string sampleId in summary.SampleIds) yield return sampleId;
        yield return run.CalibrationSummary?.Status;
        yield return run.ControlSummary?.Status;
        yield return run.ControlSummary?.LqcCall;
        yield return run.AnalysisSummary?.Status;
        if (evidence.Mapping is { } mapping)
            foreach (ChannelRegionMappingDto region in new[] { mapping.Fam, mapping.Rox })
            {
                yield return region.Channel;
                yield return region.SheetName;
                foreach (WellColumnMappingDto column in region.WellColumns) yield return column.Well;
            }
        foreach (WellAssignmentDto assignment in evidence.Assignments)
        {
            yield return assignment.Well;
            yield return assignment.Role;
            yield return assignment.SampleId;
            yield return assignment.Group;
            yield return assignment.Target;
            yield return assignment.Notes;
        }
        if (evidence.Calibration is { } calibration)
        {
            yield return calibration.Status;
            foreach (WellDiagnosticDto diagnostic in calibration.PerWellDiagnostics)
            {
                yield return diagnostic.Well;
                yield return diagnostic.Channel;
                yield return diagnostic.Code;
                yield return diagnostic.Message;
            }
        }
        if (evidence.Control is { } control)
        {
            yield return control.Status;
            yield return control.LqcCall;
            foreach (string? text in IssuesText(control.Issues)) yield return text;
        }
        foreach (SampleAnalysisDto sample in evidence.Samples)
        {
            yield return sample.SampleId;
            yield return sample.Status;
            yield return sample.Call;
            foreach (PerWellResultDto well in sample.PerWell)
            {
                yield return well.Well;
                yield return well.Call;
                yield return well.TraceId;
            }
        }
        foreach (CalculationTraceDto trace in evidence.Traces)
        {
            yield return trace.TraceId;
            yield return trace.Algorithm;
            yield return trace.MethodSnapshotHash;
            yield return trace.Well;
            yield return trace.Channel;
            yield return trace.ComparisonOperator;
            yield return trace.FinalStatus;
            yield return trace.ProvenanceCompleteness;
            foreach (TraceValueDto value in trace.IntermediateValues) yield return value.Name;
            if (trace.Provenance is { } provenance)
            {
                foreach (string? text in IdentityText(provenance.Identity)) yield return text;
                foreach (SourcePointEvidenceDto point in provenance.SourcePoints)
                    foreach (string? text in SourcePointText(point)) yield return text;
                foreach (string code in provenance.ErrorCodes) yield return code;
            }
        }
        foreach (AnalysisStageEvidenceDto stage in evidence.ScientificEvidence?.Stages ?? [])
        {
            yield return stage.Stage;
            yield return stage.ProvenanceCompleteness;
            yield return stage.EvaluatedPointsSha256;
            foreach (string? text in IdentityText(stage.Identity)) yield return text;
            foreach (WellScientificEvidenceDto well in stage.Wells)
            {
                yield return well.Well; yield return well.Channel; yield return well.Role; yield return well.Status;
                foreach (string? text in SourcePointText(well.Endpoint)) yield return text;
                foreach (TraceValueDto value in well.Values) yield return value.Name;
                foreach (string code in well.ErrorCodes) yield return code;
            }
        }
    }

    private static IEnumerable<string?> SourcePointText(SourcePointEvidenceDto point)
    {
        yield return point.Well; yield return point.Channel; yield return point.SheetName;
    }

    private static IEnumerable<string?> IdentityText(AnalysisInputIdentityDto identity)
    {
        yield return identity.SourceSha256; yield return identity.MappingSha256; yield return identity.InputSnapshotSha256;
        yield return identity.MethodSnapshotHash; yield return identity.Algorithm;
    }

    private static IEnumerable<string?> IssuesText(IReadOnlyList<IssueDto> issues)
    {
        foreach (IssueDto issue in issues)
        {
            yield return issue.Code;
            yield return issue.UserMessage;
            yield return issue.RecommendedAction;
            yield return issue.WellId;
            yield return issue.Channel;
        }
    }
}
