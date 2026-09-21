(() => {
  "use strict";

  const plot = document.getElementById("plot");
  const fallback = document.getElementById("fallback");
  const bridge = window.chrome?.webview;
  let activeGeneration = -1;
  let activePlotId = null;
  let activeKind = null;
  let initialCamera = null;
  let operationQueue = Promise.resolve();
  const locked3dModeBarButtons = [
    "orbitRotation",
    "tableRotation",
    "pan3d",
    "zoom3d",
    "resetCameraDefault3d",
    "resetCameraLastSave3d"
  ];

  function supportsWebGl2() {
    const canvas = document.createElement("canvas");
    return Boolean(canvas.getContext("webgl2"));
  }

  function supportsWebGl() {
    const canvas = document.createElement("canvas");
    return Boolean(canvas.getContext("webgl2") || canvas.getContext("webgl"));
  }

  function post(type, request, payload = {}) {
    bridge?.postMessage({
      schemaVersion: 1,
      type,
      requestId: request.requestId,
      plotId: request.plotId,
      generation: request.generation,
      payload
    });
  }

  function toPlotlyCamera(camera) {
    return {
      eye: { x: camera.eyeX, y: camera.eyeY, z: camera.eyeZ },
      center: { x: 0, y: 0, z: 0 },
      up: { x: 0, y: 0, z: 1 },
      projection: { type: camera.projection === "Orthographic" ? "orthographic" : "perspective" }
    };
  }

  function cameraPayload(value) {
    if (!value?.eye) return null;
    return {
      camera: {
        eyeX: value.eye.x, eyeY: value.eye.y, eyeZ: value.eye.z,
        projection: value.projection?.type === "orthographic" ? "Orthographic" : "Perspective"
      }
    };
  }

  async function render(message) {
    let stage = "PREPARE";
    try {
    const spec = message.payload.spec;
    const snapshot = message.payload.data;
    const is3d = spec.kind === "Scatter3D" || spec.kind === "Line3D";
    if (is3d && !supportsWebGl()) {
      plot.hidden = true;
      fallback.hidden = false;
      post("plot.error", message, { code: "PLOT_WEBGL_UNAVAILABLE", message: "3D rendering is unavailable." });
      return;
    }

    const bySeries = new Map(spec.series.map(series => [series.seriesId, { series, points: [] }]));
    for (const point of snapshot.points) bySeries.get(point.seriesId)?.points.push(point);
    const useWebGl2D = !is3d && snapshot.points.length >= 10_000 && supportsWebGl();
    const traces = [...bySeries.values()].map(({ series, points }) => {
      const trace = {
        type: is3d ? "scatter3d" : useWebGl2D ? "scattergl" : "scatter",
        mode: spec.kind.includes("Line") ? (spec.kind.includes("Scatter") ? "lines+markers" : "lines") : "markers",
        name: series.label,
        visible: series.visible,
        x: points.map(point => point.x),
        y: points.map(point => point.y),
        z: is3d ? points.map(point => point.z) : undefined,
        customdata: points.map(point => point.pointId),
        marker: { size: series.markerSize }
      };
      if (!is3d) {
        trace.selected = { marker: { color: "#b42318", size: series.markerSize + 3 } };
        trace.unselected = { marker: { opacity: 0.55 } };
      }
      return trace;
    });
    const axis = value => ({
      title: { text: value.label },
      type: value.scale === "Log" ? "log" : "linear",
      range: value.minimum == null || value.maximum == null
        ? undefined
        : value.scale === "Log"
          ? [Math.log10(value.minimum), Math.log10(value.maximum)]
          : [value.minimum, value.maximum]
    });
    const layout = {
      title: { text: spec.title },
      showlegend: spec.showLegend,
      uirevision: spec.plotId
    };
    if (is3d) {
      initialCamera = toPlotlyCamera(spec.camera);
      layout.scene = {
        xaxis: axis(spec.x),
        yaxis: axis(spec.y),
        zaxis: axis(spec.z),
        camera: initialCamera,
        dragmode: false,
        aspectmode: "manual",
        aspectratio: { x: 1, y: 1, z: 1 }
      };
    } else {
      initialCamera = null;
      layout.xaxis = axis(spec.x);
      layout.yaxis = axis(spec.y);
    }

    plot.hidden = false;
    fallback.hidden = true;
    stage = "REACT";
    const config = { responsive: true, displaylogo: false };
    if (is3d) {
      config.scrollZoom = false;
      config.doubleClick = false;
      config.modeBarButtonsToRemove = locked3dModeBarButtons;
    }
    await Plotly.react(plot, traces, layout, config);
    activeGeneration = message.generation;
    activePlotId = message.plotId;
    activeKind = spec.kind;
    stage = "EVENTS";
    plot.removeAllListeners?.("plotly_click");
    plot.removeAllListeners?.("plotly_relayout");
    plot.removeAllListeners?.("plotly_selected");
    plot.on?.("plotly_click", event => {
      const pointId = event?.points?.[0]?.customdata;
      if (typeof pointId === "string" && bridge && activePlotId) {
        bridge.postMessage({ schemaVersion: 1, type: "plot.pointClicked", requestId: crypto.randomUUID(), plotId: activePlotId, generation: activeGeneration, payload: { pointId } });
      }
    });
    plot.on?.("plotly_selected", event => {
      if (!activePlotId) return;
      const pointIds = [...new Set((event?.points ?? []).map(point => point.customdata).filter(value => typeof value === "string"))];
      bridge?.postMessage({
        schemaVersion: 1, type: "plot.selectionChanged", requestId: crypto.randomUUID(),
        plotId: activePlotId, generation: activeGeneration, payload: { pointIds }
      });
    });
    stage = "COMPLETE";
    post("plot.rendered", message);
    } catch {
      post("plot.error", message, { code: `PLOT_RENDER_${stage}_FAILED`, message: "Plot rendering failed." });
    }
  }

  async function exportPlot(message) {
    const requested = message.payload;
    const format = requested.format === "Svg" ? "svg" : "png";
    if (format === "svg" && (activeKind === "Scatter3D" || activeKind === "Line3D")) {
      post("plot.error", message, { code: "PLOT_EXPORT_FORMAT_UNAVAILABLE", message: "SVG export is unavailable for 3D plots." });
      return;
    }
    let dataUrl = await Plotly.toImage(plot, { format, width: requested.width, height: requested.height, scale: requested.scale });
    if (format === "svg" && !dataUrl.startsWith("data:image/svg+xml;base64,")) {
      const separator = dataUrl.indexOf(",");
      if (separator < 0) throw new Error("Invalid SVG data URL.");
      const svg = decodeURIComponent(dataUrl.slice(separator + 1));
      const bytes = new TextEncoder().encode(svg);
      let binary = "";
      for (let offset = 0; offset < bytes.length; offset += 0x8000) {
        binary += String.fromCharCode(...bytes.subarray(offset, offset + 0x8000));
      }
      dataUrl = `data:image/svg+xml;base64,${btoa(binary)}`;
    }
    post("plot.exportCompleted", message, {
      format: requested.format,
      mediaType: format === "svg" ? "image/svg+xml" : "image/png",
      dataUrl
    });
  }

  async function setSelection(message) {
    const wanted = new Set(message.payload.pointIds);
    const updates = plot.data.map(trace => {
      const selected = [];
      trace.customdata?.forEach((pointId, index) => { if (wanted.has(pointId)) selected.push(index); });
      return selected;
    });
    await Plotly.restyle(plot, { selectedpoints: updates });
    post("plot.selectionChanged", message, { pointIds: message.payload.pointIds });
  }

  function isActiveCommand(message) {
    return activePlotId !== null
      && message.plotId === activePlotId
      && message.generation === activeGeneration;
  }

  async function dispatchMessage(message) {
    if (!message || message.schemaVersion !== 1 || !Number.isSafeInteger(message.generation)) return;
    let operation = "MESSAGE";
    try {
      if (message.type === "plot.render") {
        if (message.generation < activeGeneration) return;
        operation = "RENDER";
        await render(message);
        return;
      }
      if (!isActiveCommand(message)) return;

      if (message.type === "plot.resize") { operation = "RESIZE"; Plotly.Plots.resize(plot); }
      else if (message.type === "plot.export") { operation = "EXPORT"; await exportPlot(message); }
      else if (message.type === "plot.setSelection") { operation = "SELECTION"; await setSelection(message); }
      else if (message.type === "plot.dispose") {
        operation = "DISPOSE";
        Plotly.purge(plot);
        activeGeneration = -1;
        activePlotId = null;
        activeKind = null;
        initialCamera = null;
      }
      else if (message.type === "plot.resetCamera" && initialCamera) {
        operation = "CAMERA_RESET";
        const camera = structuredClone(initialCamera);
        await Plotly.relayout(plot, { "scene.camera": camera });
        post("plot.cameraChanged", message, cameraPayload(camera));
      }
    } catch {
      post("plot.error", message, { code: `PLOT_${operation}_FAILED`, message: "Plot operation failed." });
    }
  }

  bridge?.addEventListener("message", event => {
    const message = event.data;
    operationQueue = operationQueue.then(() => dispatchMessage(message));
    return operationQueue;
  });

  bridge?.postMessage({
    schemaVersion: 1,
    type: "plot.ready",
    requestId: crypto.randomUUID(),
    plotId: crypto.randomUUID(),
    generation: 0,
    payload: { protocolVersion: 1, plotlyVersion: Plotly.version, supportsSharedBuffer: false, supportsWebGl2: supportsWebGl2(), supportsToImage: typeof Plotly.toImage === "function" }
  });
})();
