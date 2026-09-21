"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

function createHarness({ webgl = true, webgl2 = true, deferReact = false } = {}) {
  const posted = [];
  const plotEvents = new Map();
  const reactResolvers = [];
  let messageHandler;
  const plot = {
    data: [],
    hidden: false,
    on(name, handler) { plotEvents.set(name, handler); },
    removeAllListeners(name) { plotEvents.delete(name); },
  };
  const fallback = { hidden: true };
  const plotly = {
    version: "4.0.0",
    lastLayout: null,
    lastConfig: null,
    relayoutCalls: [],
    restyleCalls: [],
    resizeCalls: 0,
    purgeCalls: 0,
    reactCalls: 0,
    async react(target, traces, layout, config) {
      this.reactCalls += 1;
      target.data = traces;
      this.lastLayout = layout;
      this.lastConfig = config;
      if (deferReact) await new Promise(resolve => reactResolvers.push(resolve));
    },
    async relayout(_target, update) { this.relayoutCalls.push(update); },
    async restyle(_target, update) { this.restyleCalls.push(update); },
    purge() { this.purgeCalls += 1; },
    toImage: async () => "data:image/png;base64,iVBORw==",
    Plots: { resize() { plotly.resizeCalls += 1; } },
  };
  const bridge = {
    postMessage(message) { posted.push(message); },
    addEventListener(name, handler) {
      if (name === "message") messageHandler = handler;
    },
  };
  const context = {
    console,
    crypto: { randomUUID: () => "00000000-0000-4000-8000-000000000001" },
    document: {
      getElementById(id) { return id === "plot" ? plot : fallback; },
      createElement() {
        return {
          getContext(kind) {
            if (kind === "webgl2") return webgl2 ? {} : null;
            if (kind === "webgl") return webgl ? {} : null;
            return null;
          },
        };
      },
    },
    Plotly: plotly,
    structuredClone,
    TextEncoder,
    window: { chrome: { webview: bridge } },
  };
  const source = fs.readFileSync(path.join(__dirname, "plot-host.js"), "utf8");
  vm.runInNewContext(source, context, { filename: "plot-host.js" });
  return {
    fallback,
    message: value => messageHandler({ data: value }),
    plot,
    plotEvents,
    plotly,
    posted,
    resolveReact: () => reactResolvers.shift()?.(),
  };
}

function renderMessage(overrides = {}) {
  return {
    schemaVersion: 1,
    type: "plot.render",
    requestId: "10000000-0000-4000-8000-000000000001",
    plotId: "20000000-0000-4000-8000-000000000001",
    generation: 1,
    payload: {
      dataRevision: 7,
      data: { points: [{ pointId: "p-1", x: 1, y: 10, z: 100, seriesId: "s-1" }] },
      spec: {
        plotId: "20000000-0000-4000-8000-000000000001",
        kind: "Scatter3D",
        title: "Research plot",
        showLegend: true,
        x: { label: "X", scale: "Log", minimum: 1, maximum: 100 },
        y: { label: "Y", scale: "Linear", minimum: null, maximum: null },
        z: { label: "Z", scale: "Linear", minimum: null, maximum: null },
        camera: { eyeX: 1.35, eyeY: 1.25, eyeZ: 1.15, projection: "Perspective" },
        series: [{ seriesId: "s-1", label: "Series", visible: true, markerSize: 8 }],
      },
    },
    ...overrides,
  };
}

function twoDimensionalMessage(pointCount) {
  const message = renderMessage();
  message.payload.spec.kind = "Scatter2D";
  message.payload.spec.z = null;
  message.payload.spec.camera = null;
  message.payload.data.points = Array.from({ length: pointCount }, (_, index) => ({
    pointId: `p-${index + 1}`, x: index, y: index % 101, z: null, seriesId: "s-1",
  }));
  return message;
}

test("3D rendering accepts WebGL1 and locks the camera and screen-relative axes", async () => {
  const harness = createHarness({ webgl: true, webgl2: false });
  await harness.message(renderMessage());

  assert.equal(harness.posted[0].type, "plot.ready");
  assert.equal(harness.posted[0].payload.supportsWebGl2, false);
  assert.equal(harness.posted.at(-1).type, "plot.rendered");
  assert.deepEqual(
    JSON.parse(JSON.stringify(harness.plotly.lastLayout.scene.camera.eye)),
    { x: 1.35, y: 1.25, z: 1.15 },
  );
  assert.equal(harness.plotly.lastLayout.scene.camera.projection.type, "perspective");
  assert.deepEqual(
    JSON.parse(JSON.stringify(harness.plotly.lastLayout.scene.camera.up)),
    { x: 0, y: 0, z: 1 },
  );
  assert.equal(harness.plotly.lastLayout.scene.dragmode, false);
  assert.equal(harness.plotly.lastConfig.scrollZoom, false);
  assert.equal(harness.plotly.lastConfig.doubleClick, false);
  assert.deepEqual(
    Array.from(harness.plotly.lastConfig.modeBarButtonsToRemove),
    [
      "orbitRotation",
      "tableRotation",
      "pan3d",
      "zoom3d",
      "resetCameraDefault3d",
      "resetCameraLastSave3d",
    ],
  );
  assert.equal(harness.plotly.lastLayout.scene.aspectmode, "manual");
  assert.deepEqual(
    JSON.parse(JSON.stringify(harness.plotly.lastLayout.scene.aspectratio)),
    { x: 1, y: 1, z: 1 },
  );
  assert.deepEqual(Array.from(harness.plotly.lastLayout.scene.xaxis.range), [0, 2]);
  assert.equal(harness.plotly.lastLayout.uirevision, renderMessage().plotId);
});

test("point and selection events preserve exact identities while camera changes are ignored", async () => {
  const harness = createHarness();
  await harness.message(renderMessage());
  harness.plotEvents.get("plotly_click")({ points: [{ customdata: "p-1" }] });
  assert.equal(harness.plotEvents.has("plotly_relayout"), false);
  harness.plotEvents.get("plotly_selected")({
    points: [{ customdata: "p-1" }, { customdata: "p-1" }, { customdata: "p-2" }],
  });

  assert.equal(harness.posted.at(-2).payload.pointId, "p-1");
  assert.deepEqual(Array.from(harness.posted.at(-1).payload.pointIds), ["p-1", "p-2"]);
  assert.equal(harness.posted.some(message => message.type === "plot.cameraChanged"), false);
});

test("reset and selection acknowledgements retain the command request identifier", async () => {
  const harness = createHarness();
  await harness.message(renderMessage());
  await harness.message({ ...renderMessage(), type: "plot.resetCamera", requestId: "camera-request", payload: {} });
  assert.equal(harness.posted.at(-1).type, "plot.cameraChanged");
  assert.equal(harness.posted.at(-1).requestId, "camera-request");
  assert.deepEqual(harness.plotly.relayoutCalls.at(-1)["scene.camera"].eye, { x: 1.35, y: 1.25, z: 1.15 });

  await harness.message({
    ...renderMessage(), type: "plot.setSelection", requestId: "selection-request", payload: { pointIds: ["p-1"] },
  });
  assert.equal(harness.posted.at(-1).type, "plot.selectionChanged");
  assert.equal(harness.posted.at(-1).requestId, "selection-request");
  assert.deepEqual(Array.from(harness.posted.at(-1).payload.pointIds), ["p-1"]);
});

test("non-render commands require the exact active plot and generation", async () => {
  const harness = createHarness();
  const active = renderMessage();
  await harness.message(active);

  const resize = { ...active, type: "plot.resize", payload: {} };
  await harness.message({ ...resize, plotId: "30000000-0000-4000-8000-000000000001" });
  await harness.message({ ...resize, generation: active.generation - 1 });
  await harness.message({ ...resize, generation: active.generation + 1 });
  assert.equal(harness.plotly.resizeCalls, 0);

  await harness.message(resize);
  assert.equal(harness.plotly.resizeCalls, 1);

  const dispose = { ...active, type: "plot.dispose", payload: {} };
  await harness.message(dispose);
  assert.equal(harness.plotly.purgeCalls, 1);

  await harness.message(resize);
  assert.equal(harness.plotly.resizeCalls, 1);
});

test("asynchronous plot operations execute in arrival order", async () => {
  const harness = createHarness({ deferReact: true });
  const first = renderMessage();
  const second = renderMessage({
    requestId: "10000000-0000-4000-8000-000000000002",
    plotId: "20000000-0000-4000-8000-000000000002",
    generation: 2,
  });
  second.payload.spec.plotId = second.plotId;

  const firstCompletion = harness.message(first);
  await Promise.resolve();
  assert.equal(harness.plotly.reactCalls, 1);

  const secondCompletion = harness.message(second);
  await Promise.resolve();
  assert.equal(harness.plotly.reactCalls, 1);

  harness.resolveReact();
  await firstCompletion;
  await Promise.resolve();
  assert.equal(harness.plotly.reactCalls, 2);

  harness.resolveReact();
  await secondCompletion;
  harness.plotEvents.get("plotly_click")({ points: [{ customdata: "p-1" }] });
  assert.equal(harness.posted.at(-1).plotId, second.plotId);
  assert.equal(harness.posted.at(-1).generation, second.generation);
});

test("3D rendering fails closed with a readable state when WebGL is unavailable", async () => {
  const harness = createHarness({ webgl: false, webgl2: false });
  await harness.message(renderMessage());

  assert.equal(harness.plot.hidden, true);
  assert.equal(harness.fallback.hidden, false);
  assert.equal(harness.posted.at(-1).type, "plot.error");
  assert.equal(harness.posted.at(-1).payload.code, "PLOT_WEBGL_UNAVAILABLE");
});

test("large 2D datasets use scattergl while small datasets retain SVG scatter", async () => {
  const large = createHarness();
  await large.message(twoDimensionalMessage(10_000));
  assert.equal(large.plot.data[0].type, "scattergl");

  const small = createHarness();
  await small.message(twoDimensionalMessage(9_999));
  assert.equal(small.plot.data[0].type, "scatter");
});
