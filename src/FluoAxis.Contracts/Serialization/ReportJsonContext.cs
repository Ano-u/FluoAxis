namespace FluoAxis.Contracts.Serialization;

using System.Text.Json;
using System.Text.Json.Serialization;

[JsonSerializable(typeof(ReportContentDto))]
[JsonSerializable(typeof(ReportSnapshotDto))]
[JsonSerializable(typeof(ReportExportDto))]
public sealed partial class ReportJsonContext : JsonSerializerContext
{
    public static readonly JsonSerializerOptions WireOptions = new(ContractJsonContext.WireOptions);
}
