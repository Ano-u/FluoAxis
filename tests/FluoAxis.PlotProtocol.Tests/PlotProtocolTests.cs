namespace FluoAxis.PlotProtocol.Tests;

using System.Text;
using System.Text.Json;
using FluoAxis.PlotProtocol;
using Xunit;

public sealed class PlotProtocolTests
{
    private static readonly Guid PlotId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid RequestId = Guid.Parse("20000000-0000-0000-0000-000000000002");

    [Theory]
    [InlineData(PlotKind.Scatter2D)]
    [InlineData(PlotKind.Line2D)]
    [InlineData(PlotKind.ScatterLine2D)]
    [InlineData(PlotKind.Scatter3D)]
    [InlineData(PlotKind.Line3D)]
    public void PlotSpec_roundtrips_enabled_kinds(PlotKind kind)
    {
        PlotSpec spec = Spec(kind);
        string json = JsonSerializer.Serialize(spec, PlotProtocolJsonContext.Default.PlotSpec);
        PlotSpec? result = JsonSerializer.Deserialize(json, PlotProtocolJsonContext.Default.PlotSpec);

        Assert.NotNull(result);
        Assert.Equal(PlotProtocolVersions.Current, result.SchemaVersion);
        Assert.Equal(kind, result.Kind);
        Assert.Equal(PlotId, result.PlotId);
        Assert.Single(result.Series);
    }

    [Fact]
    public void PlotSpec_serializes_required_schema_version_and_rejects_other_versions()
    {
        string json = JsonSerializer.Serialize(Spec(PlotKind.Scatter2D), PlotProtocolJsonContext.Default.PlotSpec);
        Assert.Contains("\"schemaVersion\":1", json, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlotSpec(PlotId, PlotKind.Scatter2D,
            new("x"), new("y"), null, [new("s1", "Series")], null, true, "Plot", schemaVersion: 2));
        Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize(
            json.Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal),
            PlotProtocolJsonContext.Default.PlotSpec));
    }

    [Fact]
    public void PlotSpec_enforces_dimension_and_series_invariants()
    {
        Assert.Throws<ArgumentException>(() => new PlotSpec(PlotId, PlotKind.Scatter3D, new("x"), new("y"), null,
            [new("s1", "Series")], null, true, "Invalid"));
        Assert.Throws<ArgumentException>(() => new PlotSpec(PlotId, PlotKind.Scatter2D, new("x"), new("y"), new("z"),
            [new("s1", "Series")], new(1, 1, 1), true, "Invalid"));
        Assert.Throws<ArgumentException>(() => new PlotSpec(PlotId, PlotKind.Line2D, new("x"), new("y"), null,
            [new("same", "A"), new("same", "B")], null, true, "Invalid"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlotSpec(PlotId, (PlotKind)99, new("x"), new("y"), null,
            [new("s1", "A")], null, true, "Invalid"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AxisSpec("x", (AxisScale)99));
    }

    [Fact]
    public void Codec_parses_render_with_source_generated_payload()
    {
        PlotMessageEnvelope envelope = Envelope(PlotMessageTypes.Render, JsonSerializer.SerializeToElement(
            new PlotRenderPayload
            {
                Spec = Spec(PlotKind.Scatter2D),
                Data = new PlotDataSnapshot(PlotId, PlotDimensions.Two, 1, [new PlotDataPoint("point-1", 1, 2, null, "series-1")]),
                DataRevision = 4,
            },
            PlotProtocolJsonContext.Default.PlotRenderPayload));

        PlotParseResult result = Parse(envelope, PlotMessageDirection.HostToScript);

        Assert.True(result.IsSuccess);
        Assert.IsType<PlotRenderPayload>(result.Message!.Payload);
    }

    [Fact]
    public void Codec_rejects_render_data_with_wrong_plot_dimension_or_series()
    {
        PlotRenderPayload Wrong(PlotDataSnapshot data) => new()
        {
            Spec = Spec(PlotKind.Scatter2D),
            Data = data,
            DataRevision = 1,
        };

        PlotMessageEnvelope wrongPlot = Envelope(PlotMessageTypes.Render, JsonSerializer.SerializeToElement(
            Wrong(new PlotDataSnapshot(Guid.NewGuid(), PlotDimensions.Two, 1, [new("p", 0, 0, null, "series-1")])),
            PlotProtocolJsonContext.Default.PlotRenderPayload));
        Assert.Equal(PlotProtocolError.InvalidPayload, Parse(wrongPlot, PlotMessageDirection.HostToScript).Error);

        PlotMessageEnvelope wrongDimension = Envelope(PlotMessageTypes.Render, JsonSerializer.SerializeToElement(
            Wrong(new PlotDataSnapshot(PlotId, PlotDimensions.Three, 1, [new("p", 0, 0, 0, "series-1")])),
            PlotProtocolJsonContext.Default.PlotRenderPayload));
        Assert.Equal(PlotProtocolError.InvalidPayload, Parse(wrongDimension, PlotMessageDirection.HostToScript).Error);

        PlotMessageEnvelope unknownSeries = Envelope(PlotMessageTypes.Render, JsonSerializer.SerializeToElement(
            Wrong(new PlotDataSnapshot(PlotId, PlotDimensions.Two, 1, [new("p", 0, 0, null, "other-series")])),
            PlotProtocolJsonContext.Default.PlotRenderPayload));
        Assert.Equal(PlotProtocolError.InvalidPayload, Parse(unknownSeries, PlotMessageDirection.HostToScript).Error);
    }

    [Fact]
    public void Codec_parses_ready_capabilities()
    {
        PlotMessageEnvelope envelope = Envelope(PlotMessageTypes.Ready, JsonSerializer.SerializeToElement(
            new PlotReadyPayload { ProtocolVersion = 1, PlotlyVersion = "pinned-local", SupportsSharedBuffer = true, SupportsWebGl2 = true, SupportsToImage = true },
            PlotProtocolJsonContext.Default.PlotReadyPayload));

        PlotParseResult result = Parse(envelope, PlotMessageDirection.ScriptToHost);

        PlotReadyPayload ready = Assert.IsType<PlotReadyPayload>(result.Message!.Payload);
        Assert.True(ready.SupportsSharedBuffer);
    }

    [Theory]
    [InlineData(PlotMessageTypes.Render, PlotMessageDirection.ScriptToHost)]
    [InlineData(PlotMessageTypes.PointClicked, PlotMessageDirection.HostToScript)]
    public void Codec_rejects_wrong_direction(string type, PlotMessageDirection direction)
    {
        PlotMessageEnvelope envelope = Envelope(type, JsonSerializer.SerializeToElement(new EmptyPlotPayload(), PlotProtocolJsonContext.Default.EmptyPlotPayload));
        Assert.Equal(PlotProtocolError.WrongDirection, Parse(envelope, direction).Error);
    }

    [Fact]
    public void Codec_rejects_unknown_type_schema_and_oversize()
    {
        PlotMessageEnvelope unknown = Envelope("plot.executeScript", JsonSerializer.SerializeToElement(new EmptyPlotPayload(), PlotProtocolJsonContext.Default.EmptyPlotPayload));
        Assert.Equal(PlotProtocolError.UnknownMessageType, Parse(unknown, PlotMessageDirection.ScriptToHost).Error);

        PlotMessageEnvelope schema = unknown with { SchemaVersion = 2, Type = PlotMessageTypes.Rendered };
        Assert.Equal(PlotProtocolError.UnsupportedSchemaVersion, Parse(schema, PlotMessageDirection.ScriptToHost).Error);

        byte[] oversized = new byte[PlotMessageCodec.MaximumScriptMessageBytes + 1];
        Assert.Equal(PlotProtocolError.MessageTooLarge, PlotMessageCodec.Parse(oversized, PlotMessageDirection.ScriptToHost).Error);
    }

    [Fact]
    public void Codec_accepts_bounded_export_request_and_matching_data_url_response()
    {
        PlotMessageEnvelope request = Envelope(PlotMessageTypes.Export, JsonSerializer.SerializeToElement(
            new PlotExportPayload { Format = PlotExportFormat.Png, Width = 1600, Height = 900, Scale = 2 },
            PlotProtocolJsonContext.Default.PlotExportPayload));
        Assert.True(Parse(request, PlotMessageDirection.HostToScript).IsSuccess);

        PlotMessageEnvelope response = Envelope(PlotMessageTypes.ExportCompleted, JsonSerializer.SerializeToElement(
            new PlotExportCompletedPayload
            {
                Format = PlotExportFormat.Png,
                MediaType = "image/png",
                DataUrl = "data:image/png;base64,iVBORw==",
            }, PlotProtocolJsonContext.Default.PlotExportCompletedPayload));
        Assert.True(Parse(response, PlotMessageDirection.ScriptToHost).IsSuccess);
    }

    [Theory]
    [InlineData(255, 900, 1)]
    [InlineData(1600, 4097, 1)]
    [InlineData(1600, 900, 0.5)]
    [InlineData(1600, 900, 5)]
    public void Codec_rejects_export_requests_outside_resource_bounds(int width, int height, double scale)
    {
        PlotMessageEnvelope request = Envelope(PlotMessageTypes.Export, JsonSerializer.SerializeToElement(
            new PlotExportPayload { Format = PlotExportFormat.Png, Width = width, Height = height, Scale = scale },
            PlotProtocolJsonContext.Default.PlotExportPayload));
        Assert.Equal(PlotProtocolError.InvalidPayload, Parse(request, PlotMessageDirection.HostToScript).Error);
    }

    [Theory]
    [InlineData(PlotExportFormat.Png, "image/svg+xml", "data:image/svg+xml;base64,PHN2Zz4=")]
    [InlineData(PlotExportFormat.Svg, "image/svg+xml", "data:image/png;base64,iVBORw==")]
    [InlineData(PlotExportFormat.Png, "image/png", "data:image/png;base64,not-base64")]
    public void Codec_rejects_mismatched_or_malformed_export_responses(PlotExportFormat format, string mediaType, string dataUrl)
    {
        PlotMessageEnvelope response = Envelope(PlotMessageTypes.ExportCompleted, JsonSerializer.SerializeToElement(
            new PlotExportCompletedPayload { Format = format, MediaType = mediaType, DataUrl = dataUrl },
            PlotProtocolJsonContext.Default.PlotExportCompletedPayload));
        Assert.Equal(PlotProtocolError.InvalidPayload, Parse(response, PlotMessageDirection.ScriptToHost).Error);
    }

    [Fact]
    public void Codec_rejects_unknown_envelope_and_payload_members()
    {
        string envelopeJson = $"{{\"schemaVersion\":1,\"type\":\"plot.rendered\",\"requestId\":\"{RequestId}\",\"plotId\":\"{PlotId}\",\"generation\":1,\"payload\":{{}},\"extra\":true}}";
        Assert.Equal(PlotProtocolError.InvalidJson, PlotMessageCodec.Parse(Encoding.UTF8.GetBytes(envelopeJson), PlotMessageDirection.ScriptToHost).Error);

        string payloadJson = $"{{\"schemaVersion\":1,\"type\":\"plot.pointClicked\",\"requestId\":\"{RequestId}\",\"plotId\":\"{PlotId}\",\"generation\":1,\"payload\":{{\"pointId\":\"p-1\",\"formattedValue\":\"untrusted\"}}}}";
        Assert.Equal(PlotProtocolError.InvalidPayload, PlotMessageCodec.Parse(Encoding.UTF8.GetBytes(payloadJson), PlotMessageDirection.ScriptToHost).Error);
    }

    [Fact]
    public void Point_events_carry_identifiers_only_and_selection_is_unique()
    {
        PlotMessageEnvelope click = Envelope(PlotMessageTypes.PointClicked, JsonSerializer.SerializeToElement(
            new PlotPointPayload { PointId = "point-42" }, PlotProtocolJsonContext.Default.PlotPointPayload));
        Assert.True(Parse(click, PlotMessageDirection.ScriptToHost).IsSuccess);

        PlotMessageEnvelope duplicate = Envelope(PlotMessageTypes.SelectionChanged, JsonSerializer.SerializeToElement(
            new PlotSelectionPayload { PointIds = ["point-42", "point-42"] }, PlotProtocolJsonContext.Default.PlotSelectionPayload));
        Assert.Equal(PlotProtocolError.InvalidPayload, Parse(duplicate, PlotMessageDirection.ScriptToHost).Error);
    }

    [Fact]
    public void Response_gate_drops_stale_and_uncorrelated_responses()
    {
        var gate = new PlotResponseGate();
        gate.Activate(PlotId, 3);
        gate.RegisterRequest(PlotId, 3, RequestId);

        Assert.False(gate.TryAccept(Envelope(PlotMessageTypes.Rendered, default) with { Generation = 2 }));
        Assert.False(gate.TryAccept(Envelope(PlotMessageTypes.Rendered, default) with { RequestId = Guid.NewGuid() }));
        Assert.True(gate.TryAccept(Envelope(PlotMessageTypes.Rendered, default)));
        Assert.False(gate.TryAccept(Envelope(PlotMessageTypes.Rendered, default)));

        gate.Activate(PlotId, 4);
        Assert.False(gate.TryAccept(Envelope(PlotMessageTypes.PointClicked, default) with { Generation = 3 }));
        Assert.True(gate.TryAccept(Envelope(PlotMessageTypes.PointClicked, default) with { Generation = 4 }));
        Assert.False(gate.TryAccept(Envelope(PlotMessageTypes.Render, default) with { Generation = 4 }));
    }

    [Fact]
    public void Response_gate_registers_only_commands_with_correlated_terminal_responses()
    {
        var gate = new PlotResponseGate();
        gate.Activate(PlotId, 3);

        Guid cameraRequestId = Guid.NewGuid();
        Assert.True(gate.RegisterCommand(PlotId, 3, cameraRequestId, PlotMessageTypes.ResetCamera));
        Assert.True(gate.TryAccept(Envelope(PlotMessageTypes.CameraChanged, default) with { RequestId = cameraRequestId }));

        Guid selectionRequestId = Guid.NewGuid();
        Assert.True(gate.RegisterCommand(PlotId, 3, selectionRequestId, PlotMessageTypes.SetSelection));
        Assert.True(gate.TryAccept(Envelope(PlotMessageTypes.SelectionChanged, default) with { RequestId = selectionRequestId }));
        Assert.True(gate.RegisterCommand(PlotId, 3, RequestId, PlotMessageTypes.Render));
        Assert.True(gate.TryAccept(Envelope(PlotMessageTypes.Rendered, default)));

        Guid exportRequestId = Guid.NewGuid();
        Assert.True(gate.RegisterCommand(PlotId, 3, exportRequestId, PlotMessageTypes.Export));
        Assert.True(gate.TryAccept(Envelope(PlotMessageTypes.ExportCompleted, default) with { RequestId = exportRequestId }));
    }

    [Fact]
    public void Camera_serialization_exposes_renderer_neutral_fields_for_explicit_adapter_mapping()
    {
        PlotSpec spec = Spec(PlotKind.Scatter3D);
        JsonElement json = JsonSerializer.SerializeToElement(spec, PlotProtocolJsonContext.Default.PlotSpec);
        JsonElement camera = json.GetProperty("camera");

        Assert.Equal(1.25, camera.GetProperty("eyeX").GetDouble());
        Assert.Equal(1.25, camera.GetProperty("eyeY").GetDouble());
        Assert.Equal(1.25, camera.GetProperty("eyeZ").GetDouble());
        Assert.Equal("Perspective", camera.GetProperty("projection").GetString());
    }

    private static PlotSpec Spec(PlotKind kind)
    {
        bool is3D = kind is PlotKind.Scatter3D or PlotKind.Line3D;
        return new PlotSpec(PlotId, kind, new("Time"), new("RFU"), is3D ? new AxisSpec("Z") : null,
            [new SeriesSpec("series-1", "FAM")], is3D ? new CameraSpec(1.25, 1.25, 1.25) : null, true, "Research plot");
    }

    private static PlotMessageEnvelope Envelope(string type, JsonElement payload) => new()
    {
        SchemaVersion = 1, Type = type, RequestId = RequestId, PlotId = PlotId, Generation = 3, Payload = payload,
    };

    private static PlotParseResult Parse(PlotMessageEnvelope envelope, PlotMessageDirection direction) =>
        PlotMessageCodec.Parse(JsonSerializer.SerializeToUtf8Bytes(envelope, PlotProtocolJsonContext.Default.PlotMessageEnvelope), direction);
}
