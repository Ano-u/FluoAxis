namespace FluoAxis.PlotProtocol;

using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(PlotMessageEnvelope))]
[JsonSerializable(typeof(PlotReadyPayload))]
[JsonSerializable(typeof(PlotRenderPayload))]
[JsonSerializable(typeof(PlotSelectionPayload))]
[JsonSerializable(typeof(PlotPointPayload))]
[JsonSerializable(typeof(PlotCameraPayload))]
[JsonSerializable(typeof(PlotExportPayload))]
[JsonSerializable(typeof(PlotExportCompletedPayload))]
[JsonSerializable(typeof(PlotErrorPayload))]
[JsonSerializable(typeof(EmptyPlotPayload))]
[JsonSerializable(typeof(PlotSpec))]
[JsonSerializable(typeof(PlotDataSnapshot))]
[JsonSerializable(typeof(PlotDataPoint))]
[JsonSerializable(typeof(PlotDistanceMeasurement))]
[JsonSerializable(typeof(AxisSpec))]
[JsonSerializable(typeof(SeriesSpec))]
[JsonSerializable(typeof(CameraSpec))]
public sealed partial class PlotProtocolJsonContext : JsonSerializerContext;
