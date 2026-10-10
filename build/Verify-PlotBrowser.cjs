"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const { chromium } = require("playwright");
const sharp = require("sharp");
const repository = path.resolve(__dirname, "..");
const outputEntry = process.argv[2];
if (!outputEntry?.startsWith("artifacts/plot-validation/") || path.isAbsolute(outputEntry) || outputEntry.split(/[\\/]/).some(value => value === ".." || value === "." || value === "")) throw new Error("Relative unique plot artifact output required.");
const output = path.join(repository, outputEntry);
const assets = path.join(repository, "web", "FluoAxis.PlotHost");
(async () => {
  await fs.mkdir(path.dirname(output), { recursive: true });
  await fs.mkdir(output, { recursive: false });
  const browser = await chromium.launch({ channel: "msedge", headless: true });
  const results = [];
  try {
    const page = await browser.newPage();
    await page.addInitScript(() => {
      window.plotMessages = [];
      window.chrome = { webview: { postMessage: message => window.plotMessages.push(message), addEventListener: (_name, callback) => { window.receivePlot = callback; } } };
      window.addEventListener("securitypolicyviolation", event => { window.cspViolations ??= []; window.cspViolations.push(event.violatedDirective); });
    });
    await page.route("**/*", async route => {
      const url = new URL(route.request().url());
      if (url.origin !== "https://appassets.local") return route.abort();
      const entry = decodeURIComponent(url.pathname).slice(1);
      if (entry.split("/").some(value => value === "..")) return route.abort();
      const body = await fs.readFile(path.join(assets, entry));
      const contentType = entry.endsWith(".js") ? "application/javascript" : entry.endsWith(".css") ? "text/css" : "text/html";
      await route.fulfill({ body, contentType });
    });
    await page.goto("https://appassets.local/plot-host.html");
    let commandNumber = 10;
    const command = async (type, payload, generation = 1, plotId = "20000000-0000-4000-8000-000000000001") => {
      const requestId = `50000000-0000-4000-8000-${String(commandNumber++).padStart(12, "0")}`;
      await page.evaluate(message => window.receivePlot({ data: message }), { schemaVersion: 1, type, requestId, plotId, generation, payload });
      await page.waitForFunction(id => window.plotMessages.some(message => message.requestId === id), requestId);
      return page.evaluate(id => window.plotMessages.find(message => message.requestId === id), requestId);
    };
    const exports = [];
    const checkExport = async (name, format, generation = 1, plotId) => {
      const started = performance.now();
      const response = await command("plot.export", { format, width: 1600, height: 900, scale: 2 }, generation, plotId);
      assert.equal(response.type, "plot.exportCompleted");
      const bytes = Buffer.from(response.payload.dataUrl.split(",")[1], "base64");
      await fs.writeFile(path.join(output, `${name}.${format.toLowerCase()}`), bytes);
      if (format === "Svg") {
        const svg = bytes.toString("utf8");
        assert.match(svg, /FAM \(RFU\)/);
        assert.match(svg, /Elapsed \(min\)/);
        assert.doesNotMatch(svg, /<image\b/);
      }
      const pixels = await sharp(bytes).removeAlpha().raw().toBuffer({ resolveWithObject: true });
      assert.equal(pixels.info.width, 3200);
      assert.equal(pixels.info.height, 1800);
      let colored = 0;
      for (let i = 0; i < pixels.data.length; i += pixels.info.channels) if (pixels.data[i+2] > pixels.data[i]+40 && pixels.data[i+2] > pixels.data[i+1]+20) colored++;
      assert.ok(colored > 500, "Export must contain the data curve.");
      exports.push({ name, format, width: pixels.info.width, height: pixels.info.height, coloredPixels: colored, bytes: bytes.length, elapsedMs: Math.round(performance.now() - started) });
    };
    for (const [name, width, height, constant] of [["wide-constant", 1280, 460, true], ["narrow-constant", 680, 340, true], ["wide-changing", 1280, 460, false]]) {
      await page.setViewportSize({ width, height });
      await page.evaluate(({ constant }) => window.receivePlot({ data: {
        schemaVersion: 1, type: "plot.render", requestId: "10000000-0000-4000-8000-000000000001", plotId: "20000000-0000-4000-8000-000000000001", generation: 1,
        payload: { spec: { plotId: "20000000-0000-4000-8000-000000000001", kind: "ScatterLine2D", title: "A1 - Raw RFU", showLegend: false, x: { label: "Elapsed (min)", scale: "Linear" }, y: { label: "FAM (RFU)", scale: "Linear" }, series: [{ seriesId: "raw", label: "FAM", markerSize: 7, visible: true }] },
          data: { points: [0,90,120].map((x,i) => ({ pointId: `p-${i}`, seriesId: "raw", x, y: constant ? 1000 : [100,500,1000][i] })) } }
      }}), { constant });
      await page.waitForFunction(() => document.getElementById("plot")._fullLayout?._size?.h > 200);
      const geometry = await page.evaluate(() => {
        const plot = document.getElementById("plot");
        const buttons = [...document.querySelectorAll(".modebar-btn")].map(button => button.getBoundingClientRect());
        return { plotHeight: plot.clientHeight, canvasHeight: plot._fullLayout._size.h, yTitle: document.querySelector(".ytitle")?.textContent, yTicks: [...document.querySelectorAll(".ytick")].map(tick => tick.textContent), modebarPosition: getComputedStyle(document.querySelector(".modebar")).position, buttonRows: new Set(buttons.map(button => Math.round(button.top))).size, violations: window.cspViolations ?? [], values: plot.data[0].y };
      });
      assert.ok(geometry.canvasHeight >= 220);
      assert.equal(geometry.yTitle, "FAM (RFU)");
      assert.ok(geometry.yTicks.length >= 3);
      assert.equal(geometry.modebarPosition, "absolute");
      assert.equal(geometry.buttonRows, 1);
      assert.deepEqual(geometry.violations, []);
      if (constant) assert.deepEqual(geometry.values, [1000,1000,1000]);
      const screenshot = await page.screenshot({ path: path.join(output, `${name}.png`) });
      const { data, info } = await sharp(screenshot).removeAlpha().raw().toBuffer({ resolveWithObject: true });
      let colored = 0;
      for (let index = 0; index < data.length; index += info.channels) if (data[index+2] > data[index]+40 && data[index+2] > data[index+1]+20) colored++;
      assert.ok(colored > 300, "Data curve must contain visible colored pixels.");
      results.push({ name, width, height, coloredPixels: colored, ...geometry });
    }
    await page.setViewportSize({ width: 920, height: 420 });
    await page.waitForFunction(() => document.getElementById("plot")._fullLayout.width === 920);
    const point = page.locator(".scatterlayer .points path").first();
    const box = await point.boundingBox();
    await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
    await page.waitForFunction(() => window.plotMessages.some(message => message.type === "plot.pointClicked" && message.payload.pointId === "p-0"));
    await command("plot.setSelection", { pointIds: ["p-1"] });
    assert.deepEqual(await page.evaluate(() => document.getElementById("plot").data[0].selectedpoints), [1]);
    await command("plot.setSelection", { pointIds: [] });
    assert.equal(await page.evaluate(() => document.getElementById("plot").data[0].selectedpoints == null), true);
    await checkExport("changing-curve", "Png");
    await checkExport("changing-curve", "Svg");
    await page.evaluate(() => window.receivePlot({ data: {
      schemaVersion: 1, type: "plot.render", requestId: "30000000-0000-4000-8000-000000000001", plotId: "40000000-0000-4000-8000-000000000001", generation: 2,
      payload: { spec: { plotId: "40000000-0000-4000-8000-000000000001", kind: "Scatter3D", title: "Raw paired signals", showLegend: false, x: {label:"Time (min)",scale:"Linear"},y:{label:"FAM (RFU)",scale:"Linear"},z:{label:"ROX (RFU)",scale:"Linear"},camera:{eyeX:1.8,eyeY:1.8,eyeZ:1.5333333333333334,projection:"Perspective"},series:[{seriesId:"raw",label:"Paired",markerSize:5,visible:true}]}, data:{points:[0,30,60,90,120].map((x,i)=>({pointId:`three-${i}`,seriesId:"raw",x,y:100+i*200,z:50+i*90}))} }
    }}));
    await page.waitForFunction(() => window.plotMessages.some(message => message.type === "plot.rendered" && message.generation === 2));
    const threeAxes = await page.evaluate(() => {
      const scene = document.getElementById("plot")._fullLayout.scene;
      return [scene.xaxis.title.text, scene.yaxis.title.text, scene.zaxis.title.text];
    });
    assert.deepEqual(threeAxes, ["Time (min)", "FAM (RFU)", "ROX (RFU)"]);
    assert.deepEqual(await page.evaluate(() => window.cspViolations ?? []), []);
    await page.screenshot({ path: path.join(output,"three-dimensional.png") });
    await page.evaluate(() => window.receivePlot({ data: {
      schemaVersion: 1, type: "plot.render", requestId: "60000000-0000-4000-8000-000000000001", plotId: "70000000-0000-4000-8000-000000000001", generation: 3,
      payload: { spec: { plotId: "70000000-0000-4000-8000-000000000001", kind: "Scatter2D", title: "Dense signal", showLegend: false, x: {label:"Time (min)",scale:"Linear"},y:{label:"FAM (RFU)",scale:"Linear"},series:[{seriesId:"raw",label:"FAM",markerSize:3,visible:true}]}, data:{points:Array.from({length:10000},(_,i)=>({pointId:`dense-${i}`,seriesId:"raw",x:i/100,y:100+Math.sin(i/100)*50}))} }
    }}));
    await page.waitForFunction(() => window.plotMessages.some(message => message.type === "plot.rendered" && message.generation === 3));
    assert.equal(await page.evaluate(() => document.getElementById("plot").data[0].type), "scattergl");
    const rejected = await command("plot.export", {format:"Svg",width:1600,height:900,scale:1}, 3, "70000000-0000-4000-8000-000000000001");
    assert.equal(rejected.payload.code, "PLOT_EXPORT_FORMAT_UNAVAILABLE");
    await checkExport("dense-curve", "Png", 3, "70000000-0000-4000-8000-000000000001");
    assert.deepEqual(await page.evaluate(() => window.cspViolations ?? []), []);
    await fs.writeFile(path.join(output,"browser.json"), JSON.stringify({ status: "passed", browser: browser.version(), cases: results, exports, threeAxes, resize: "passed", pointIdentity: "passed", selectionAndClear: "passed", threeDimensional: "passed", webglSvgRejection: "passed", scope: "Actual local Plotly/CSP in Edge, not native WinUI layout." }, null, 2));
    console.log("Browser plot passed: axes/CSP/resize, mouse point identity, selection/clear, real PNG/SVG pixels, 3D and dense WebGL export eligibility.");
  } finally { await browser.close(); }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
