namespace FluoAxis.Application;

using System.Security.Cryptography;
using System.Text.Json;
using FluoAxis.Analysis;
using FluoAxis.Contracts;
using FluoAxis.Domain;

/// <summary>Captures values already computed by Analysis and exact input identities, never a second scientific calculator.</summary>
internal sealed class ScientificEvidenceCapture
{
    private readonly PreparedDataMapping mapping;
    internal AnalysisInputIdentityDto Identity { get; }

    internal ScientificEvidenceCapture(ProjectRunAggregate run, PreparedSourceIdentity source, PreparedDataMapping mapping,
        IReadOnlyList<WellAssignment> assignments, IReadOnlyList<SignalSeries> signals)
    {
        this.mapping = mapping;
        string mappingHash = MappingHash(mapping);
        string inputHash = Hash(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("method", run.Method.ComputeCanonicalHash()); writer.WriteString("source", source.Sha256); writer.WriteString("mapping", mappingHash);
            writer.WriteStartArray("assignments");
            foreach (WellAssignment assignment in assignments.OrderBy(item => item.Well.Row).ThenBy(item => item.Well.Column))
            {
                writer.WriteStartArray(); writer.WriteStringValue(assignment.Well.ToString()); writer.WriteStringValue(assignment.Role.ToString());
                writer.WriteStringValue(assignment.Sample?.SampleId); writer.WriteNumberValue(assignment.Sample?.ReplicateIndex ?? 0);
                writer.WriteStringValue(assignment.Sample?.Group); writer.WriteStringValue(assignment.Sample?.Target); writer.WriteStringValue(assignment.Sample?.Notes); writer.WriteEndArray();
            }
            writer.WriteEndArray(); writer.WriteStartArray("signals");
            foreach (SignalSeries series in signals.OrderBy(item => item.Well.Row).ThenBy(item => item.Well.Column).ThenBy(item => item.Channel))
            {
                writer.WriteStartArray(); writer.WriteStringValue(series.Well.ToString()); writer.WriteNumberValue((int)series.Channel);
                foreach (SignalPoint point in series.Points)
                {
                    writer.WriteStartArray(); writer.WriteNumberValue(point.Elapsed.Ticks); writer.WriteNumberValue(point.Rfu);
                    Number(writer, point.SourceRowIndex); Number(writer, point.SourceColumnIndex); writer.WriteEndArray();
                }
                writer.WriteEndArray();
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        });
        Identity = new(source.Sha256, mappingHash, inputHash, run.Method.ComputeCanonicalHash(), CalculationTrace.AlgorithmVersion, run.RunRevision);
    }

    internal static string MappingHash(PreparedDataMapping mapping) => Hash(writer =>
    {
        writer.WriteStartObject(); writer.WriteStartArray("regions");
        foreach (PreparedChannelRegion region in new[] { mapping.Fam, mapping.Rox })
        {
            writer.WriteStartObject(); writer.WriteNumber("channel", (int)region.Channel); writer.WriteString("sheet", region.SheetName);
            writer.WriteNumber("header", region.HeaderRowIndex); writer.WriteNumber("time", region.TimeColumnIndex);
            writer.WritePropertyName("end"); Number(writer, region.EndRowIndex); writer.WriteStartArray("wells");
            foreach (var pair in region.WellColumns.OrderBy(pair => pair.Key))
            { writer.WriteStartArray(); writer.WriteNumberValue(pair.Key); writer.WriteStringValue(pair.Value.ToString()); writer.WriteEndArray(); }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        writer.WriteEndArray(); writer.WriteStartArray("options");
        writer.WriteStringValue(mapping.Options?.EncodingName); writer.WriteStringValue(mapping.Options?.Delimiter);
        writer.WriteStringValue(mapping.Options?.DecimalCulture); writer.WriteStringValue(mapping.Options?.TimeUnit);
        writer.WriteEndArray(); writer.WriteEndObject();
    });

    internal (AnalysisStageEvidenceDto Stage, IReadOnlyList<PersistedScientificPoint> Points) Calibration(CalibrationResult result, BrafMethodSnapshot method)
    {
        List<WellScientificEvidenceDto> wells = [];
        List<PersistedScientificPoint> points = [];
        foreach (PositiveWellResult positive in result.PositiveWells)
        {
            if (positive.Endpoint is not { } endpoint || positive.EndpointPointIndex is not { } index) continue;
            List<TraceValueDto> values = [];
            Add(values, "minimumRfu", positive.Plateau.Min); Add(values, "maximumRfu", positive.Plateau.Max);
            Add(values, "meanRfu", positive.Plateau.Mean); Add(values, "relativeRange", positive.Plateau.RelativeRange);
            Add(values, "Fm", positive.Fm); Add(values, "relativeRangeMaxInclusive", method.Plateau.RelativeRangeMax);
            Add(values, "minimumWindowPoints", method.Plateau.MinWindowPoints);
            wells.Add(new(positive.Well.ToString(), Channel(positive.Channel), WellRoleNames.Positive100, Status(positive.Plateau.QcStatus),
                Source(positive.Well, positive.Channel, index, endpoint), Window(positive.WindowPoints, method.Plateau.StartMinutes, method.Plateau.EndMinutes),
                values, result.Diagnostics.Where(item => item.Well == positive.Well && item.Channel == positive.Channel).Select(item => item.Code).ToArray()));
            points.AddRange(positive.WindowPoints.Select(point => Stored("calibration", positive.Well, positive.Channel, point)));
        }
        foreach (NtcWellResult ntc in result.NtcWells)
        {
            if (ntc.Endpoint is not { } endpoint) continue;
            List<TraceValueDto> values = [];
            Add(values, "FbOwn", ntc.Fb); Add(values, "FmRun", result.RunFm.GetValueOrDefault(ntc.Channel)); Add(values, "denominator", ntc.Denominator);
            Add(values, "minimumY", ntc.MinimumY); Add(values, "maximumY", ntc.MaximumY); Add(values, "checkedPoints", ntc.QcPointCount);
            Add(values, "outOfBandCount", ntc.OutOfBandPoints.Count); Add(values, "normalizedMinInclusive", method.Ntc.NormalizedMin); Add(values, "normalizedMaxInclusive", method.Ntc.NormalizedMax);
            wells.Add(new(ntc.Well.ToString(), Channel(ntc.Channel), WellRoleNames.Ntc, ntc.OutOfBandPoints.Count == 0 ? StatusNames.Pass : StatusNames.Fail,
                Source(ntc.Well, ntc.Channel, ntc.EndpointPointIndex, endpoint), Window(ntc.QcPoints, 0, method.EndpointMinutes), values,
                result.Diagnostics.Where(item => item.Well == ntc.Well && item.Channel == ntc.Channel).Select(item => item.Code).ToArray()));
            points.AddRange(ntc.QcPoints.Select(point => Stored("calibration", ntc.Well, ntc.Channel, point)));
        }
        using var digest = new ScientificPointDigest();
        foreach (PersistedScientificPoint point in points.OrderBy(point => point.Well, StringComparer.Ordinal).ThenBy(point => point.Channel, StringComparer.Ordinal).ThenBy(point => point.PointIndex)) digest.Append(point);
        AnalysisStageEvidenceDto stage = Stage("calibration", wells);
        bool allPointSourcesPresent = points.All(point => CoordinatesMatch(point.Well, point.Channel, point.SourceRowIndex, point.SourceColumnIndex))
            && result.PositiveWells.All(well => well.WindowPoints.Count == well.Plateau.PointCount)
            && result.NtcWells.All(well => well.QcPoints.Count == well.QcPointCount);
        return (new(stage.Stage, stage.Identity, stage.Wells,
            allPointSourcesPresent ? stage.ProvenanceCompleteness : ProvenanceCompletenessNames.StoredPartial, digest.Complete()), points.AsReadOnly());
    }

    internal AnalysisStageEvidenceDto Controls(ControlQcResult result, CalibrationResult calibration, BrafMethodSnapshot method)
    {
        List<WellScientificEvidenceDto> wells = [];
        foreach (WellEndpointY wt in result.WtWells)
        {
            foreach (FluorescenceChannel channel in new[] { FluorescenceChannel.Fam, FluorescenceChannel.Rox })
            {
                SignalPoint? endpoint = channel == FluorescenceChannel.Fam ? wt.FamEndpoint : wt.RoxEndpoint;
                int? index = channel == FluorescenceChannel.Fam ? wt.FamEndpointPointIndex : wt.RoxEndpointPointIndex;
                if (endpoint is null || index is null) continue;
                List<TraceValueDto> values = [];
                Add(values, "Y", channel == FluorescenceChannel.Fam ? wt.YFam : wt.YRox);
                Add(values, "FmRun", calibration.RunFm[channel]); Add(values, "FbRun", calibration.RunFb[channel]);
                Add(values, "wtReferenceY", channel == FluorescenceChannel.Fam ? result.EvaluatedWtReference?.YFamMean : result.EvaluatedWtReference?.YRoxMean);
                Add(values, "meanMaxExclusive", channel == FluorescenceChannel.Fam ? method.Wt.FamMeanMaxExclusive : method.Wt.RoxMeanMaxExclusive);
                wells.Add(new(wt.Well.ToString(), Channel(channel), WellRoleNames.WildType, result.WtReference is null ? StatusNames.Fail : StatusNames.Pass,
                    Source(wt.Well, channel, index.Value, endpoint.Value), null, values,
                    result.Diagnostics.Where(item => item.Well == wt.Well && item.Channel == channel).Select(item => item.Code).ToArray()));
            }
        }
        wells.AddRange(TraceWells(result.LqcWells, WellRoleNames.Lqc01Percent, result.QcStatus));
        return Stage("control", wells);
    }

    internal AnalysisStageEvidenceDto Samples(SampleAnalysisResult result) => Stage("sample", result.Results.SelectMany(sample => TraceWells(sample.Wells, WellRoleNames.Sample, sample.Status)).ToList());

    internal TraceProvenanceDto? Provenance(CalculationTrace trace) => trace.SourcePoints.Count == 2
        ? new(Identity, trace.SourcePoints.Select(point => Source(trace.SourceWell, point.Channel, point.PointIndex, point.Point)).ToArray(), trace.ErrorCodes) : null;

    private IEnumerable<WellScientificEvidenceDto> TraceWells(IReadOnlyList<WellDrPrimeResult> results, string role, QcStatus groupStatus)
    {
        foreach (WellDrPrimeResult result in results)
        {
            if (result.Trace is not { } trace) continue;
            foreach (ChannelEndpoint point in trace.SourcePoints)
            {
                List<TraceValueDto> values = [];
                Add(values, "Y", trace.GetIntermediate(point.Channel == FluorescenceChannel.Fam ? "Y_FAM" : "Y_ROX"));
                Add(values, "drPrime", result.DrPrime); Add(values, "thresholdInclusive", trace.ThresholdRaw);
                yield return new(result.Well.ToString(), Channel(point.Channel), role, Status(groupStatus),
                    Source(result.Well, point.Channel, point.PointIndex, point.Point), null, values, trace.ErrorCodes);
            }
        }
    }

    private AnalysisStageEvidenceDto Stage(string name, List<WellScientificEvidenceDto> wells) => new(name, Identity, wells,
        wells.All(well => CoordinatesMatch(well.Well, well.Channel, well.Endpoint.SourceRowIndex, well.Endpoint.SourceColumnIndex))
            ? ProvenanceCompletenessNames.StoredComplete : ProvenanceCompletenessNames.StoredPartial);

    private bool CoordinatesMatch(string well, string channel, int? row, int? column)
    {
        PreparedChannelRegion region = channel == ChannelNames.Fam ? mapping.Fam : mapping.Rox;
        return row is { } sourceRow && column is { } sourceColumn && sourceRow > region.HeaderRowIndex
            && (region.EndRowIndex is null || sourceRow <= region.EndRowIndex)
            && region.WellColumns.TryGetValue(sourceColumn, out WellId mappedWell) && mappedWell.ToString() == well;
    }

    private SourcePointEvidenceDto Source(WellId well, FluorescenceChannel channel, int index, SignalPoint point) => new(
        well.ToString(), Channel(channel), index, point.Elapsed.Ticks, point.Rfu,
        channel == FluorescenceChannel.Fam ? mapping.Fam.SheetName : mapping.Rox.SheetName, point.SourceRowIndex, point.SourceColumnIndex);

    private static SourceWindowEvidenceDto? Window(IReadOnlyList<EvaluatedPoint> points, double start, double end) => points.Count == 0 ? null
        : new(start, end, points[0].PointIndex, points[^1].PointIndex, points.Count);
    private static PersistedScientificPoint Stored(string stage, WellId well, FluorescenceChannel channel, EvaluatedPoint point) => new(
        stage, well.ToString(), Channel(channel), point.PointIndex, point.Point.Elapsed.Ticks, point.Point.Rfu, point.Normalized, point.Point.SourceRowIndex, point.Point.SourceColumnIndex, point.WithinBand);
    private static string Channel(FluorescenceChannel channel) => channel == FluorescenceChannel.Fam ? ChannelNames.Fam : ChannelNames.Rox;
    private static string Status(QcStatus status) => AnalysisInputBinding.ToWireStatus(status);
    private static void Add(List<TraceValueDto> values, string key, double? value) { if (value is { } number) values.Add(new(key, number)); }
    private static void Number(Utf8JsonWriter writer, int? number) { if (number is { } value) writer.WriteNumberValue(value); else writer.WriteNullValue(); }
    private static string Hash(Action<Utf8JsonWriter> write)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stream = new HashStream(hash);
        using (var writer = new Utf8JsonWriter(stream)) write(writer);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private sealed class HashStream(IncrementalHash hash) : Stream
    {
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => hash.AppendData(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => hash.AppendData(buffer);
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

/// <summary>Verifies stored input identities without re-running a scientific calculation.</summary>
public static class ScientificInputIdentity
{
    public static AnalysisInputIdentityDto Capture(ProjectRunAggregate run, PreparedSourceIdentity source, PreparedDataMapping mapping,
        IReadOnlyList<WellAssignment> assignments, IReadOnlyList<SignalSeries> signals) =>
        new ScientificEvidenceCapture(run, source, mapping, assignments, signals).Identity;
}

/// <summary>Streaming hash for actual evaluated points in ordinal well/channel/index order; no scientific recomputation.</summary>
public sealed class ScientificPointDigest : IDisposable
{
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly MemoryStream buffer = new();
    public void Append(PersistedScientificPoint point)
    {
        buffer.SetLength(0);
        buffer.Position = 0;
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray(); writer.WriteStringValue(point.Well); writer.WriteStringValue(point.Channel);
            writer.WriteNumberValue(point.PointIndex); writer.WriteNumberValue(point.ElapsedTicks); writer.WriteNumberValue(point.RawRfu);
            if (point.Normalized is { } normalized) writer.WriteNumberValue(normalized); else writer.WriteNullValue();
            if (point.SourceRowIndex is { } row) writer.WriteNumberValue(row); else writer.WriteNullValue();
            if (point.SourceColumnIndex is { } column) writer.WriteNumberValue(column); else writer.WriteNullValue();
            if (point.WithinBand is { } within) writer.WriteBooleanValue(within); else writer.WriteNullValue();
            writer.WriteEndArray();
        }
        hash.AppendData(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)));
    }
    public string Complete() => Convert.ToHexStringLower(hash.GetHashAndReset());
    public void Dispose() { buffer.Dispose(); hash.Dispose(); GC.SuppressFinalize(this); }
}
