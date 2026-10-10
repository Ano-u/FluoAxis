namespace FluoAxis.Contracts.Serialization;

using System.Text.Json;
using System.Text.Json.Serialization;

[JsonSerializable(typeof(AnalysisStageEvidenceDto))]
[JsonSerializable(typeof(ScientificEvidenceDto))]
[JsonSerializable(typeof(TraceProvenanceDto))]
[JsonSerializable(typeof(ScientificPointPageDto))]
public sealed partial class ScientificEvidenceJsonContext : JsonSerializerContext
{
    public static readonly JsonSerializerOptions WireOptions = new(ContractJsonContext.WireOptions);
}
