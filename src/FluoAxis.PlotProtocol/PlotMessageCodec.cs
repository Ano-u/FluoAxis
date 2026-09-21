namespace FluoAxis.PlotProtocol;

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

/// <summary>Bounded, reflection-free, fail-closed parser for the WebMessage trust boundary.</summary>
public static class PlotMessageCodec
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumHostMessageBytes = 16 * 1024 * 1024;
    public const int MaximumScriptMessageBytes = 32 * 1024 * 1024;
    public const int MaximumExportDataUrlCharacters = 24 * 1024 * 1024;
    public const int MaximumPointIds = 10_000;

    private static readonly Dictionary<string, PlotMessageDirection> Directions =
        new Dictionary<string, PlotMessageDirection>(StringComparer.Ordinal)
        {
            [PlotMessageTypes.Ready] = PlotMessageDirection.ScriptToHost,
            [PlotMessageTypes.Render] = PlotMessageDirection.HostToScript,
            [PlotMessageTypes.Resize] = PlotMessageDirection.HostToScript,
            [PlotMessageTypes.Export] = PlotMessageDirection.HostToScript,
            [PlotMessageTypes.ResetCamera] = PlotMessageDirection.HostToScript,
            [PlotMessageTypes.SetSelection] = PlotMessageDirection.HostToScript,
            [PlotMessageTypes.Dispose] = PlotMessageDirection.HostToScript,
            [PlotMessageTypes.Rendered] = PlotMessageDirection.ScriptToHost,
            [PlotMessageTypes.Error] = PlotMessageDirection.ScriptToHost,
            [PlotMessageTypes.PointClicked] = PlotMessageDirection.ScriptToHost,
            [PlotMessageTypes.SelectionChanged] = PlotMessageDirection.ScriptToHost,
            [PlotMessageTypes.CameraChanged] = PlotMessageDirection.ScriptToHost,
            [PlotMessageTypes.ExportCompleted] = PlotMessageDirection.ScriptToHost,
        };

    public static PlotParseResult Parse(ReadOnlySpan<byte> utf8Json, PlotMessageDirection direction)
    {
        if (utf8Json.Length == 0) return Fail(PlotProtocolError.InvalidJson);
        int maximumBytes = direction == PlotMessageDirection.HostToScript
            ? MaximumHostMessageBytes
            : MaximumScriptMessageBytes;
        if (utf8Json.Length > maximumBytes) return Fail(PlotProtocolError.MessageTooLarge);

        PlotMessageEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize(utf8Json, PlotProtocolJsonContext.Default.PlotMessageEnvelope);
        }
        catch (JsonException)
        {
            return Fail(PlotProtocolError.InvalidJson);
        }

        if (envelope is null || envelope.RequestId == Guid.Empty || envelope.PlotId == Guid.Empty
            || envelope.Generation < 0 || string.IsNullOrWhiteSpace(envelope.Type)
            || envelope.Payload.ValueKind != JsonValueKind.Object)
        {
            return Fail(PlotProtocolError.InvalidEnvelope);
        }

        if (envelope.SchemaVersion != CurrentSchemaVersion) return Fail(PlotProtocolError.UnsupportedSchemaVersion);
        if (!Directions.TryGetValue(envelope.Type, out PlotMessageDirection expected)) return Fail(PlotProtocolError.UnknownMessageType);
        if (expected != direction) return Fail(PlotProtocolError.WrongDirection);

        object? payload;
        try
        {
            payload = DeserializePayload(envelope.Type, envelope.Payload);
            ValidatePayload(envelope, payload);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return Fail(PlotProtocolError.InvalidPayload);
        }

        return new PlotParseResult(new ParsedPlotMessage(envelope, payload), PlotProtocolError.None);
    }

    private static object DeserializePayload(string type, JsonElement payload) => type switch
    {
        PlotMessageTypes.Ready => Read(payload, PlotProtocolJsonContext.Default.PlotReadyPayload),
        PlotMessageTypes.Render => Read(payload, PlotProtocolJsonContext.Default.PlotRenderPayload),
        PlotMessageTypes.SetSelection or PlotMessageTypes.SelectionChanged => Read(payload, PlotProtocolJsonContext.Default.PlotSelectionPayload),
        PlotMessageTypes.PointClicked => Read(payload, PlotProtocolJsonContext.Default.PlotPointPayload),
        PlotMessageTypes.CameraChanged => Read(payload, PlotProtocolJsonContext.Default.PlotCameraPayload),
        PlotMessageTypes.Export => Read(payload, PlotProtocolJsonContext.Default.PlotExportPayload),
        PlotMessageTypes.ExportCompleted => Read(payload, PlotProtocolJsonContext.Default.PlotExportCompletedPayload),
        PlotMessageTypes.Error => Read(payload, PlotProtocolJsonContext.Default.PlotErrorPayload),
        _ => Read(payload, PlotProtocolJsonContext.Default.EmptyPlotPayload),
    };

    private static T Read<T>(JsonElement element, JsonTypeInfo<T> typeInfo) where T : class =>
        element.Deserialize(typeInfo) ?? throw new JsonException("Payload is null.");

    private static void ValidatePayload(PlotMessageEnvelope envelope, object payload)
    {
        switch (payload)
        {
            case PlotReadyPayload ready:
                if (ready.ProtocolVersion <= 0 || string.IsNullOrWhiteSpace(ready.PlotlyVersion))
                {
                    throw new InvalidOperationException("Ready capabilities are invalid.");
                }
                break;
            case PlotRenderPayload render:
                ArgumentNullException.ThrowIfNull(render.Spec);
                ArgumentNullException.ThrowIfNull(render.Data);
                bool specIs3D = render.Spec.Kind is PlotKind.Scatter3D or PlotKind.Line3D;
                bool dataIs3D = render.Data.Dimensions == PlotDimensions.Three;
                var declaredSeries = new HashSet<string>(render.Spec.Series.Select(series => series.SeriesId), StringComparer.Ordinal);
                if (render.Spec.PlotId != envelope.PlotId
                    || render.Data.PlotId != envelope.PlotId
                    || render.DataRevision < 0
                    || specIs3D != dataIs3D
                    || render.Data.Points.Any(point => !declaredSeries.Contains(point.SeriesId)))
                {
                    throw new InvalidOperationException("Render specification, data, and envelope are inconsistent.");
                }
                break;
            case PlotSelectionPayload selection:
                ValidatePointIds(selection.PointIds);
                break;
            case PlotPointPayload point:
                Guard.Identifier(point.PointId, nameof(point.PointId));
                break;
            case PlotCameraPayload camera:
                ArgumentNullException.ThrowIfNull(camera.Camera);
                break;
            case PlotExportPayload export:
                Guard.Defined(export.Format, nameof(export.Format));
                if (export.Width is < 256 or > 4096 || export.Height is < 256 or > 4096
                    || !double.IsFinite(export.Scale) || export.Scale is < 1 or > 4)
                {
                    throw new InvalidOperationException("Export dimensions or scale are outside the supported bounds.");
                }
                break;
            case PlotExportCompletedPayload completed:
                ValidateExportCompleted(completed);
                break;
            case PlotErrorPayload error:
                Guard.Identifier(error.Code, nameof(error.Code));
                Guard.NonBlank(error.Message, nameof(error.Message));
                break;
        }
    }

    private static void ValidateExportCompleted(PlotExportCompletedPayload completed)
    {
        Guard.Defined(completed.Format, nameof(completed.Format));
        string expectedMediaType = completed.Format == PlotExportFormat.Png ? "image/png" : "image/svg+xml";
        string expectedPrefix = $"data:{expectedMediaType};base64,";
        if (!string.Equals(completed.MediaType, expectedMediaType, StringComparison.Ordinal)
            || string.IsNullOrEmpty(completed.DataUrl)
            || completed.DataUrl.Length > MaximumExportDataUrlCharacters
            || !completed.DataUrl.StartsWith(expectedPrefix, StringComparison.Ordinal)
            || !IsBase64(completed.DataUrl.AsSpan(expectedPrefix.Length)))
        {
            throw new InvalidOperationException("Export response is invalid.");
        }
    }

    private static bool IsBase64(ReadOnlySpan<char> value)
    {
        if (value.Length == 0 || value.Length % 4 != 0) return false;
        byte[] buffer = new byte[(value.Length / 4) * 3];
        return Convert.TryFromBase64Chars(value, buffer, out _);
    }

    private static void ValidatePointIds(IReadOnlyList<string>? pointIds)
    {
        ArgumentNullException.ThrowIfNull(pointIds);
        if (pointIds.Count > MaximumPointIds)
        {
            throw new InvalidOperationException("Selection exceeds the protocol point limit.");
        }
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (string pointId in pointIds)
        {
            Guard.Identifier(pointId, nameof(pointIds));
            if (!unique.Add(pointId))
            {
                throw new InvalidOperationException("Selection contains a duplicate point identifier.");
            }
        }
    }

    private static PlotParseResult Fail(PlotProtocolError error) => new(null, error);
}
