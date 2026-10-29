// node Tests/column-rendering-performance.mjs 9273 http://127.0.0.1:5289
// Paused-scene comparison. FPS remains display-limited; CPU times include simulation/UI.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import {graphicsPreferenceFixture} from './browser-check-session.mjs';

const port = process.argv[2] ?? '9273', base = process.argv[3] ?? 'https://localhost:7280';
const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, {method: 'PUT'})).json();
const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
let id = 0;
const pending = new Map(), errors = [];
function send(method, params = {}) {
    return new Promise((resolve, reject) => {
        const requestId = ++id;
        const timer = setTimeout(() => { pending.delete(requestId); reject(Error(`CDP timeout: ${method}`)); }, 60000);
        pending.set(requestId, {resolve, reject, timer});
        socket.send(JSON.stringify({id: requestId, method, params}));
    });
}
socket.onmessage = event => {
    const message = JSON.parse(event.data);
    if (message.id) {
        const task = pending.get(message.id);
        if (!task) return;
        pending.delete(message.id); clearTimeout(task.timer);
        message.error ? task.reject(Error(JSON.stringify(message.error))) : task.resolve(message.result);
    } else if (message.method === 'Runtime.exceptionThrown') errors.push(JSON.stringify(message.params));
    else if (message.method === 'Runtime.consoleAPICalled') {
        const text = message.params.args.map(arg => arg.value ?? arg.description ?? '').join(' ');
        if (/Initialization failed|Frame failed|Uncaptured error/.test(text)) errors.push(text);
    }
};
async function evaluate(expression) {
    const result = await send('Runtime.evaluate', {expression, awaitPromise: true, returnByValue: true});
    if (result.exceptionDetails) throw Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
}
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function waitFor(expression) {
    for (let attempt = 0; attempt < 600; attempt++) {
        assert.deepEqual(errors, []);
        const error = await evaluate(`document.querySelector('.game-error')?.textContent`);
        if (error) throw Error(error);
        if (await evaluate(expression)) return;
        await delay(100);
    }
    throw Error(`Timed out: ${expression}`);
}
const percentile = (values, p) => values.toSorted((a, b) => a - b)[Math.floor((values.length - 1) * p)];

try {
    await send('Runtime.enable'); await send('Page.enable');
    await send('Page.addScriptToEvaluateOnNewDocument', {source: `
        ${graphicsPreferenceFixture()}
        window.__measure = false; window.__frameCount = 0; window.__slot = null;
        window.__cpu = []; window.__mode = null;
        const fetchOriginal = window.fetch;
        window.fetch = async function(input, ...args) {
            const response = await fetchOriginal.call(this, input, ...args);
            if (String(input?.url ?? input).includes('graphics-settings.json')) {
                const settings = await response.json();
                Object.assign(settings, {width: 1920, height: 1080, targetFPS: 1000});
                return new Response(JSON.stringify(settings), {headers: {'Content-Type': 'application/json'}});
            }
            return response;
        };
        const raf = window.requestAnimationFrame;
        window.requestAnimationFrame = function(callback) {
            return raf.call(this, timestamp => {
                const before = window.__frameCount, start = performance.now();
                callback(timestamp);
                if (window.__frameCount > before) window.__cpu.push({ms: performance.now() - start, timestamp});
            });
        };
        const requestDevice = GPUAdapter.prototype.requestDevice;
        GPUAdapter.prototype.requestDevice = async function(descriptor = {}) {
            const timing = this.features.has('timestamp-query');
            const device = await requestDevice.call(this, {...descriptor,
                requiredFeatures: [...(descriptor.requiredFeatures ?? []), ...(timing ? ['timestamp-query'] : [])]});
            window.__gpu = device;
            if (timing) window.__queries = device.createQuerySet({type: 'timestamp', count: 1440});
            return device;
        };
        const write = GPUQueue.prototype.writeBuffer;
        GPUQueue.prototype.writeBuffer = function(buffer, offset, data, ...args) {
            if (buffer.label === 'WolfNET Camera') {
                window.__mode = new Float32Array(data.buffer ?? data, data.byteOffset ?? 0, 16)[12];
                window.__slot = window.__measure && window.__frameCount < 180 ? window.__frameCount++ * 8 : null;
            }
            return write.call(this, buffer, offset, data, ...args);
        };
        const beginCompute = GPUCommandEncoder.prototype.beginComputePass;
        GPUCommandEncoder.prototype.beginComputePass = function(descriptor = {}) {
            if (window.__slot !== null && window.__queries) descriptor = {...descriptor, timestampWrites: {
                querySet: window.__queries, beginningOfPassWriteIndex: window.__slot, endOfPassWriteIndex: window.__slot + 1}};
            return beginCompute.call(this, descriptor);
        };
        const beginRender = GPUCommandEncoder.prototype.beginRenderPass;
        GPUCommandEncoder.prototype.beginRenderPass = function(descriptor) {
            if (window.__slot !== null && window.__queries) {
                const start = window.__slot + (descriptor.label === 'WolfNET Interlaced Presentation Pass' ? 4 : 2);
                descriptor = {...descriptor, timestampWrites: {
                    querySet: window.__queries, beginningOfPassWriteIndex: start, endOfPassWriteIndex: start + 1}};
            }
            return beginRender.call(this, descriptor);
        };
        const submit = GPUQueue.prototype.submit;
        GPUQueue.prototype.submit = function(commands) {
            if (window.__slot !== null && window.__queries) {
                const encoder = window.__gpu.createCommandEncoder();
                // The final marker includes presentation, history copies, and GPU barriers.
                const pass = beginCompute.call(encoder, {timestampWrites: {querySet: window.__queries,
                    beginningOfPassWriteIndex: window.__slot + 6, endOfPassWriteIndex: window.__slot + 7}});
                pass.end(); commands = [...commands, encoder.finish()];
            }
            window.__slot = null;
            return submit.call(this, commands);
        };
        window.__readTimings = async function() {
            window.__measure = false;
            if (!window.__queries) return null;
            const device = window.__gpu, bytes = window.__frameCount * 8 * 8;
            const resolve = device.createBuffer({size: bytes, usage: GPUBufferUsage.QUERY_RESOLVE | GPUBufferUsage.COPY_SRC});
            const read = device.createBuffer({size: bytes, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ});
            const encoder = device.createCommandEncoder();
            encoder.resolveQuerySet(window.__queries, 0, window.__frameCount * 8, resolve, 0);
            encoder.copyBufferToBuffer(resolve, 0, read, 0, bytes);
            device.queue.submit([encoder.finish()]); await read.mapAsync(GPUMapMode.READ);
            const stamps = new BigUint64Array(read.getMappedRange()), rows = [];
            for (let i = 0; i < stamps.length; i += 8) rows.push({
                compute: Number(stamps[i + 1] - stamps[i]) / 1e6,
                world: Number(stamps[i + 3] - stamps[i + 2]) / 1e6,
                gpuFrame: Number(stamps[i + 6] - stamps[i]) / 1e6});
            read.unmap(); read.destroy(); resolve.destroy(); return rows;
        };
    `});
    await send('Page.navigate', {url: base}); await send('Page.bringToFront');
    await waitFor(`window.__mode && !document.querySelector('.loading-overlay')`);
    const results = [];
    // Reverse the second round to expose warm-up and clock-speed bias.
    for (const mode of [1, 2, 3, 3, 2, 1]) {
        await evaluate(`(() => {const select = document.querySelector('#column-interlace');
            select.value = '${mode}'; select.dispatchEvent(new Event('change', {bubbles: true}));
            document.querySelector('#graphics-menu form').requestSubmit();})()`);
        await waitFor(`window.__mode === ${mode}`); await delay(500);
        await evaluate('window.__frameCount = 0; window.__cpu = []; window.__measure = true');
        await waitFor('window.__frameCount === 180');
        const gpu = await evaluate('window.__readTimings()'), cpu = await evaluate('window.__cpu');
        const result = {mode, cpuMedianMs: percentile(cpu.map(frame => frame.ms), .5),
            cpuP95Ms: percentile(cpu.map(frame => frame.ms), .95),
            fps: (cpu.length - 1) * 1000 / (cpu.at(-1).timestamp - cpu[0].timestamp),
            gpuMedianMs: gpu ? percentile(gpu.map(frame => frame.gpuFrame), .5) : null,
            worldMedianMs: gpu ? percentile(gpu.map(frame => frame.world), .5) : null};
        results.push(result); console.log(JSON.stringify(result));
    }
    fs.mkdirSync('artifacts/column-rendering', {recursive: true});
    fs.writeFileSync('artifacts/column-rendering/performance.json', JSON.stringify({
        scenario: 'Paused Pine Loop at 1920x1080; test-only targetFPS=1000; FPS is display-limited.',
        gpuTiming: 'Includes WebGPU passes, history copies, and barriers; excludes browser compositing.', results}, null, 2));
    assert.deepEqual(errors, []);
} finally {
    await evaluate('window.__restoreGraphicsPreferences?.()').catch(() => {});
    await send('Page.close').catch(() => {}); socket.close();
    for (const task of pending.values()) clearTimeout(task.timer);
}
