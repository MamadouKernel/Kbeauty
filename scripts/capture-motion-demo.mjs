import { mkdir, rm, writeFile } from "node:fs/promises";
import { resolve } from "node:path";

const endpoint = process.env.CDP_ENDPOINT ?? "http://127.0.0.1:9223";
const targetUrl = process.env.DEMO_URL ?? "http://localhost:5090/";
const outputDir = resolve(process.env.DEMO_FRAMES ?? "artifacts/motion-demo/frames");

await rm(outputDir, { recursive: true, force: true });
await mkdir(outputDir, { recursive: true });

const target = await fetch(`${endpoint}/json/new?${encodeURIComponent(targetUrl)}`, {
  method: "PUT",
}).then((response) => {
  if (!response.ok) throw new Error(`Impossible de créer l'onglet CDP (${response.status}).`);
  return response.json();
});

const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolveOpen, reject) => {
  socket.addEventListener("open", resolveOpen, { once: true });
  socket.addEventListener("error", reject, { once: true });
});

let nextId = 1;
const pending = new Map();
socket.addEventListener("message", ({ data }) => {
  const message = JSON.parse(data);
  if (!message.id || !pending.has(message.id)) return;
  const { resolve: resolveCommand, reject } = pending.get(message.id);
  pending.delete(message.id);
  if (message.error) reject(new Error(message.error.message));
  else resolveCommand(message.result);
});

function command(method, params = {}) {
  const id = nextId++;
  socket.send(JSON.stringify({ id, method, params }));
  return new Promise((resolveCommand, reject) => pending.set(id, { resolve: resolveCommand, reject }));
}

const wait = (milliseconds) => new Promise((resolveWait) => setTimeout(resolveWait, milliseconds));
await command("Page.enable");
await command("Runtime.enable");
await command("Emulation.setDeviceMetricsOverride", {
  width: 390,
  height: 844,
  deviceScaleFactor: 1,
  mobile: true,
});
await command("Page.navigate", { url: targetUrl });
await wait(1200);

let frame = 0;
async function capture() {
  const result = await command("Page.captureScreenshot", {
    format: "jpeg",
    quality: 88,
    captureBeyondViewport: false,
  });
  const name = `frame-${String(frame++).padStart(4, "0")}.jpg`;
  await writeFile(resolve(outputDir, name), Buffer.from(result.data, "base64"));
}

// L'introduction laisse le temps aux animations d'apparition de se jouer.
for (let index = 0; index < 28; index += 1) {
  await capture();
  await wait(100);
}

const documentHeight = await command("Runtime.evaluate", {
  expression: "Math.max(document.body.scrollHeight, document.documentElement.scrollHeight)",
  returnByValue: true,
});
const maxScroll = Math.max(0, documentHeight.result.value - 844);
const stages = [0.28, 0.58, 0.86, 0.35, 0];

for (const stage of stages) {
  const destination = Math.round(maxScroll * stage);
  const originResult = await command("Runtime.evaluate", {
    expression: "window.scrollY",
    returnByValue: true,
  });
  const origin = originResult.result.value;
  for (let step = 1; step <= 20; step += 1) {
    const progress = step / 20;
    const eased = 1 - Math.pow(1 - progress, 3);
    const position = Math.round(origin + (destination - origin) * eased);
    await command("Runtime.evaluate", { expression: `window.scrollTo(0, ${position})` });
    await wait(70);
    await capture();
  }
  await wait(250);
}

socket.close();
console.log(`${frame} images capturées dans ${outputDir}`);
