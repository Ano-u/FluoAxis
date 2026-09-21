namespace FluoAxis.PlotProtocol;

using System.Text.Json;

public enum PlotMessageDirection
{
    HostToScript,
    ScriptToHost,
}

public static class PlotMessageTypes
{
    public const string Ready = "plot.ready";
    public const string Render = "plot.render";
    public const string Resize = "plot.resize";
    public const string Export = "plot.export";
    public const string ResetCamera = "plot.resetCamera";
    public const string SetSelection = "plot.setSelection";
    public const string Dispose = "plot.dispose";
    public const string Rendered = "plot.rendered";
    public const string Error = "plot.error";
    public const string PointClicked = "plot.pointClicked";
    public const string SelectionChanged = "plot.selectionChanged";
    public const string CameraChanged = "plot.cameraChanged";
    public const string ExportCompleted = "plot.exportCompleted";
}

/// <summary>Strict wire envelope. Payload validation is selected by Type and direction.</summary>
public sealed record PlotMessageEnvelope
{
    public required int SchemaVersion { get; init; }
    public required string Type { get; init; }
    public required Guid RequestId { get; init; }
    public required Guid PlotId { get; init; }
    public required long Generation { get; init; }
    public required JsonElement Payload { get; init; }
}

public sealed record PlotReadyPayload
{
    public required int ProtocolVersion { get; init; }
    public required string PlotlyVersion { get; init; }
    public required bool SupportsSharedBuffer { get; init; }
    public required bool SupportsWebGl2 { get; init; }
    public required bool SupportsToImage { get; init; }
}

public sealed record PlotRenderPayload
{
    public required PlotSpec Spec { get; init; }
    public required PlotDataSnapshot Data { get; init; }
    public required long DataRevision { get; init; }
}

public sealed record PlotSelectionPayload
{
    public required IReadOnlyList<string> PointIds { get; init; }
}

public sealed record PlotPointPayload
{
    public required string PointId { get; init; }
}

public sealed record PlotCameraPayload
{
    public required CameraSpec Camera { get; init; }
}

public enum PlotExportFormat
{
    Png,
    Svg,
}

public sealed record PlotExportPayload
{
    public required PlotExportFormat Format { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required double Scale { get; init; }
}

public sealed record PlotExportCompletedPayload
{
    public required PlotExportFormat Format { get; init; }
    public required string MediaType { get; init; }
    public required string DataUrl { get; init; }
}

public sealed record PlotErrorPayload
{
    public required string Code { get; init; }
    public required string Message { get; init; }
}

public sealed record EmptyPlotPayload;

public sealed record ParsedPlotMessage(PlotMessageEnvelope Envelope, object Payload);

public enum PlotProtocolError
{
    None,
    MessageTooLarge,
    InvalidJson,
    InvalidEnvelope,
    UnsupportedSchemaVersion,
    UnknownMessageType,
    WrongDirection,
    InvalidPayload,
}

public sealed record PlotParseResult(ParsedPlotMessage? Message, PlotProtocolError Error)
{
    public bool IsSuccess => Message is not null && Error == PlotProtocolError.None;
}
