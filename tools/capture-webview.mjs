import fs from "node:fs";
import path from "node:path";

const outputArgument = process.argv.indexOf("--output");
const outputDirectory = outputArgument >= 0
  ? process.argv[outputArgument + 1]
  : path.resolve("docs/screenshots");
const port = process.env.THEBOOSTDECK_DEBUG_PORT || "9223";
const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
const target = targets.find(item => item.type === "page" && item.url.includes("app.theboostdeck/dashboard.html"));
if (!target) throw new Error("The TheBoostDeck dashboard target was not found.");

const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve, reject) => {
  socket.addEventListener("open", resolve, { once: true });
  socket.addEventListener("error", reject, { once: true });
});

let nextId = 0;
const pending = new Map();
const runtimeErrors = [];
socket.addEventListener("message", event => {
  const message = JSON.parse(event.data);
  if (message.id) {
    const request = pending.get(message.id);
    if (!request) return;
    pending.delete(message.id);
    if (message.error) request.reject(new Error(JSON.stringify(message.error)));
    else request.resolve(message.result);
    return;
  }
  if (message.method === "Runtime.exceptionThrown") {
    runtimeErrors.push(message.params.exceptionDetails?.exception?.description || message.params.exceptionDetails?.text || "Unknown runtime error");
  }
  if (message.method === "Log.entryAdded" && message.params.entry.level === "error") {
    runtimeErrors.push(message.params.entry.text);
  }
});

function send(method, params = {}) {
  const id = ++nextId;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    socket.send(JSON.stringify({ id, method, params }));
  });
}

const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
await send("Page.enable");
await send("Runtime.enable");
await send("Log.enable");
fs.mkdirSync(outputDirectory, { recursive: true });

async function capture(width, height, name) {
  await send("Emulation.setDeviceMetricsOverride", { width, height, deviceScaleFactor: 1, mobile: false });
  await delay(1400);
  const evaluation = await send("Runtime.evaluate", {
    expression: `JSON.stringify({
      viewport: [innerWidth, innerHeight],
      scrollWidth: document.documentElement.scrollWidth,
      scrollHeight: document.documentElement.scrollHeight,
      status: document.getElementById("data-status").textContent,
      issues: document.getElementById("issue-count").textContent,
      cpu: document.getElementById("cpu-header").textContent,
      cpuTemperature: document.getElementById("cpu-temperature").textContent,
      gpu: document.getElementById("gpu-header").textContent,
      gpuTemperature: document.getElementById("gpu-temperature").textContent,
      memory: document.getElementById("memory-header").textContent,
      vram: document.getElementById("vram-header").textContent,
      network: document.getElementById("network-download").textContent,
      cards: [...document.querySelectorAll(".card")].map(card => {
        const rect = card.getBoundingClientRect();
        return { name: card.querySelector(".card-title").textContent, x: Math.round(rect.x), y: Math.round(rect.y), width: Math.round(rect.width), height: Math.round(rect.height) };
      })
    })`,
    returnByValue: true
  });
  const summary = JSON.parse(evaluation.result.value);
  const screenshot = await send("Page.captureScreenshot", { format: "png", captureBeyondViewport: false });
  const screenshotPath = path.join(outputDirectory, `${name}.png`);
  fs.writeFileSync(screenshotPath, Buffer.from(screenshot.data, "base64"));
  const summaryPath = path.join(outputDirectory, `${name}.json`);
  fs.writeFileSync(summaryPath, JSON.stringify({ ...summary, runtimeErrors: [...runtimeErrors] }, null, 2));
  console.log(JSON.stringify({ screenshot: screenshotPath, ...summary, runtimeErrors }, null, 2));
}

await capture(1280, 720, "TheBoostDeck-landscape-1280x720");
await capture(720, 1280, "TheBoostDeck-portrait-720x1280");
await capture(1920, 1080, "TheBoostDeck-landscape-1920x1080");
await send("Emulation.clearDeviceMetricsOverride");

if (process.argv.includes("--test-interactions")) {
  const evaluate = async expression => {
    const result = await send("Runtime.evaluate", { expression, returnByValue: true });
    if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
  };
  const initialTopmost = await evaluate(`document.getElementById("pin-button").getAttribute("aria-pressed")`);
  const expectedToggled = initialTopmost === "true" ? "false" : "true";
  await evaluate(`document.getElementById("pin-button").click()`);
  await delay(500);
  const toggledTopmost = await evaluate(`document.getElementById("pin-button").getAttribute("aria-pressed")`);
  await evaluate(`document.getElementById("pin-button").click()`);
  await delay(500);
  const restoredTopmost = await evaluate(`document.getElementById("pin-button").getAttribute("aria-pressed")`);
  await evaluate(`document.getElementById("hide-button").click()`);
  await delay(700);
  const hiddenState = await evaluate(`document.body.dataset.hostVisible`);
  const result = {
    initialTopmost,
    toggledTopmost,
    restoredTopmost,
    hiddenState,
    passed: toggledTopmost === expectedToggled && restoredTopmost === initialTopmost && hiddenState === "false"
  };
  fs.writeFileSync(path.join(outputDirectory, "window-interactions.json"), JSON.stringify(result, null, 2));
  console.log(JSON.stringify({ interactions: result }, null, 2));
  if (!result.passed) throw new Error("Window interaction verification failed.");
}

socket.close();
