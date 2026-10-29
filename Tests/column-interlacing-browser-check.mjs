// Start the game and Chromium with remote debugging, then:
// node Tests/column-interlacing-browser-check.mjs 9273 http://127.0.0.1:5289
import assert from 'node:assert/strict';
import {uiInspectionSource} from './ui-browser-helpers.mjs';

const port = process.argv[2] ?? '9273';
const base = process.argv[3] ?? 'https://localhost:7280';
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
        pending.delete(message.id);
        clearTimeout(task.timer);
        message.error ? task.reject(Error(JSON.stringify(message.error))) : task.resolve(message.result);
    } else if (message.method === 'Runtime.exceptionThrown') errors.push(JSON.stringify(message.params));
    else if (message.method === 'Runtime.consoleAPICalled') {
        const text = message.params.args.map(arg => arg.value ?? arg.description ?? '').join(' ');
        if (/Initialization failed|Frame failed|Uncaptured error|Could not load map/.test(text)) errors.push(text);
    }
};
async function evaluate(expression) {
    const result = await send('Runtime.evaluate', {expression, awaitPromise: true, returnByValue: true});
    if (result.exceptionDetails) throw Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
}
async function waitFor(expression) {
    for (let attempt = 0; attempt < 600; attempt++) {
        assert.deepEqual(errors, []);
        const error = await evaluate(`document.querySelector('.game-error')?.textContent`);
        if (error) throw Error(error);
        if (await evaluate(expression)) return;
        await new Promise(resolve => setTimeout(resolve, 100));
    }
    throw Error(`Timed out: ${expression}`);
}

try {
    await send('Runtime.enable');
    await send('Page.enable');
    await send('Page.addScriptToEvaluateOnNewDocument', {source: `
        ${uiInspectionSource}
        window.__frames = []; window.__samples = []; window.__sampleCount = 0;
        window.__frame = null; window.__motion = 0; window.__retained = null;
        const fetchOriginal = window.fetch;
        window.fetch = async function(input, ...args) {
            const response = await fetchOriginal.call(this, input, ...args);
            if (String(input?.url ?? input).includes('graphics-settings.json')) {
                const settings = await response.json();
                Object.assign(settings, {width: 259, height: 145, targetFPS: 30, columnInterlaceFactor: 3});
                return new Response(JSON.stringify(settings), {headers: {'Content-Type': 'application/json'}});
            }
            return response;
        };
        const createTexture = GPUDevice.prototype.createTexture;
        GPUDevice.prototype.createTexture = function(descriptor) {
            const retained = descriptor.label === 'WolfNET Interlaced Frame';
            // Readback is test-only; the renderer does not otherwise need CopySrc.
            if (retained) descriptor = {...descriptor, usage: descriptor.usage | GPUTextureUsage.COPY_SRC};
            const texture = createTexture.call(this, descriptor);
            if (retained) { window.__retained = texture; window.__gpu = this; }
            return texture;
        };
        const createView = GPUTexture.prototype.createView;
        GPUTexture.prototype.createView = function(...args) {
            const view = createView.apply(this, args);
            view.__width = this.width;
            return view;
        };
        const destroyTexture = GPUTexture.prototype.destroy;
        GPUTexture.prototype.destroy = function() {
            if (this === window.__retained) window.__retained = null;
            return destroyTexture.call(this);
        };
        const write = GPUQueue.prototype.writeBuffer;
        GPUQueue.prototype.writeBuffer = function(buffer, offset, data, ...args) {
            if (buffer.label === 'WolfNET Camera') {
                const camera = new Float32Array(data.buffer ?? data, data.byteOffset ?? 0, 16).slice();
                if (window.__sampleCount > 0) {
                    // Move the real render camera so refreshed columns visibly change.
                    const angle = ++window.__motion * 0.07, c = Math.cos(angle), s = Math.sin(angle);
                    for (const start of [2, 4]) {
                        const x = camera[start], y = camera[start + 1];
                        camera[start] = x * c - y * s; camera[start + 1] = x * s + y * c;
                    }
                }
                window.__frame = {step: camera[12], phase: camera[13], passes: []};
                return write.call(this, buffer, offset, camera);
            }
            return write.call(this, buffer, offset, data, ...args);
        };
        const dispatch = GPUComputePassEncoder.prototype.dispatchWorkgroups;
        GPUComputePassEncoder.prototype.dispatchWorkgroups = function(x, ...args) {
            if (window.__frame) window.__frame.workgroups = x;
            return dispatch.call(this, x, ...args);
        };
        const beginPass = GPUCommandEncoder.prototype.beginRenderPass;
        GPUCommandEncoder.prototype.beginRenderPass = function(descriptor) {
            const pass = beginPass.call(this, descriptor);
            if (window.__frame) {
                pass.__record = {label: descriptor.label, load: descriptor.colorAttachments[0].loadOp,
                    width: descriptor.colorAttachments[0].view.__width, pipelines: []};
                window.__frame.passes.push(pass.__record);
            }
            return pass;
        };
        const setPipeline = GPURenderPassEncoder.prototype.setPipeline;
        GPURenderPassEncoder.prototype.setPipeline = function(pipeline) {
            this.__record?.pipelines.push(pipeline.label);
            return setPipeline.call(this, pipeline);
        };
        const submit = GPUQueue.prototype.submit;
        GPUQueue.prototype.submit = function(commands) {
            submit.call(this, commands);
            const frame = window.__frame;
            if (!frame) return;
            window.__frame = null;
            window.__frames.push(frame);
            if (window.__sampleCount <= 0 || !window.__retained || frame.step === 1) return;
            window.__sampleCount--;
            const device = window.__gpu, texture = window.__retained;
            const bytesPerRow = Math.ceil(texture.width * 4 / 256) * 256;
            const buffer = device.createBuffer({size: bytesPerRow * texture.height,
                usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ});
            const encoder = device.createCommandEncoder();
            encoder.copyTextureToBuffer({texture}, {buffer, bytesPerRow}, [texture.width, texture.height]);
            submit.call(this, [encoder.finish()]);
            const sample = {step: frame.step, phase: frame.phase, workgroups: frame.workgroups, width: texture.width,
                height: texture.height, bytesPerRow, pixels: null};
            window.__samples.push(sample);
            buffer.mapAsync(GPUMapMode.READ).then(() => {
                sample.pixels = Array.from(new Uint8Array(buffer.getMappedRange()));
                buffer.unmap(); buffer.destroy();
            });
        };
    `});
    await send('Page.navigate', {url: base});
    await send('Page.bringToFront');
    await waitFor(`window.__frames.length >= 8 && !document.querySelector('.loading-overlay')`);
    assert.equal(await evaluate(`document.querySelector('#column-interlace').value`), '3', 'Startup mode did not load from JSON');

    function verifyFrames(frames, factor) {
        assert.equal(frames[0].step, 1, 'Mode/scene change did not prime a complete frame');
        assert.equal(frames[0].phase, 0);
        for (let i = 0; i < frames.length; i++) {
            const frame = frames[i];
            const step = i === 0 ? 1 : factor;
            assert.equal(frame.step, step);
            assert.equal(frame.phase, i === 0 ? 0 : (i - 1) % factor);
            const columns = Math.ceil((259 - frame.phase) / step);
            assert.equal(frame.workgroups, Math.ceil(columns / 64), 'Dispatched extra raycast workgroups');
            assert.equal(frame.passes[0].load, 'clear');
            assert.equal(frame.passes[0].width, Math.ceil(259 / step), 'World shading target was not reduced');
            assert.equal(frame.passes.length, step === 1 ? 1 : 2);
            const screenPass = frame.passes.at(-1);
            if (step > 1) {
                assert(!frame.passes[0].pipelines.includes('WolfNET Overlay Pipeline'), 'Screen overlays were retained in world history');
                assert(screenPass.pipelines.includes('WolfNET Interlaced Presentation Pipeline'));
            }
        }
    }
    verifyFrames(await evaluate('window.__frames.slice(0, 8)'), 3);
    assert.equal(await evaluate('window.__uiImages.length'), 2, 'Cockpit and wheel are missing from the separate HUD canvas');

    for (const factor of [2, 3, 1]) {
        const start = await evaluate(`(() => {
            const start = window.__frames.length;
            window.__samples = []; window.__sampleCount = ${factor === 1 ? 0 : 7}; window.__motion = 0;
            const select = document.querySelector('#column-interlace');
            select.value = '${factor}'; select.dispatchEvent(new Event('change', {bubbles: true}));
            return start;
        })()`);
        await waitFor(`window.__frames.length >= ${start + 9} && window.__sampleCount === 0 && window.__samples.every(s => s.pixels)`);
        verifyFrames(await evaluate(`window.__frames.slice(${start}, ${start + 9})`), factor);
        assert.equal(await evaluate('window.__uiImages.length'), 2, 'Mode change hid the cockpit or wheel');
        if (factor === 1) {
            assert.equal(await evaluate('window.__retained'), null, 'Full rendering did not release the retained texture');
            continue;
        }
        const samples = await evaluate('window.__samples');
        for (let i = 1; i < samples.length; i++) {
            const previous = samples[i - 1], current = samples[i];
            let updated = 0;
            for (let x = 0; x < current.width; x++) {
                let changed = false;
                for (let y = 0; y < current.height; y++) {
                    const offset = y * current.bytesPerRow + x * 4;
                    for (let channel = 0; channel < 4; channel++)
                        if (current.pixels[offset + channel] !== previous.pixels[offset + channel]) changed = true;
                }
                if (x % factor === current.phase) updated += changed ? 1 : 0;
                else assert(!changed, 'A skipped column lost its previous color');
            }
            assert(updated > 0, 'Refreshed columns did not change with the moving camera');
        }
        console.log(`PASS: 1/${factor} column rotation, reduced dispatch, retained GPU pixels, and separate HUD.`);
    }

    await evaluate(`(() => {
        const select = document.querySelector('#column-interlace');
        select.value = '3'; select.dispatchEvent(new Event('change', {bubbles: true}));
    })()`);
    await waitFor('window.__frames.at(-1).step === 3');
    const restart = await evaluate(`(() => {
        const start = window.__frames.length;
        document.querySelector('.race-restart').click(); return start;
    })()`);
    await waitFor(`!document.querySelector('.loading-overlay') && window.__frames.length >= ${restart + 9}`);
    verifyFrames(await evaluate(`window.__frames.slice(${restart}, ${restart + 9})`), 3);
    assert.equal(await evaluate(`document.querySelector('#column-interlace').value`), '3');
    assert.deepEqual(errors, []);
    console.log('PASS: full rendering, odd viewport width, startup JSON, live mode switches, and race restart priming.');
} finally {
    await send('Page.close').catch(() => {});
    socket.close();
    for (const task of pending.values()) clearTimeout(task.timer);
}
