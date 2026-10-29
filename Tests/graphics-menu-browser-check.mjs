// node Tests/graphics-menu-browser-check.mjs 9273 http://127.0.0.1:5289
import assert from 'node:assert/strict';
import fs from 'node:fs';
import {createBrowserCheck, graphicsPreferenceFixture} from './browser-check-session.mjs';
import {uiInspectionSource} from './ui-browser-helpers.mjs';

const check = await createBrowserCheck(process.argv[2] ?? '9273');
const {send, evaluate, waitFor, errors} = check;
const base = process.argv[3] ?? 'https://localhost:7280';
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function open() {
    await evaluate(`document.querySelector('.graphics-open').focus(); document.querySelector('.graphics-open').click()`);
    await waitFor(`document.querySelector('#graphics-menu').open`);
}
async function set(id, value) {
    await evaluate(`(() => {const input = document.getElementById(${JSON.stringify(id)});
        input.value = ${JSON.stringify(String(value))}; input.dispatchEvent(new Event('change', {bubbles: true}));})()`);
}
async function apply() {
    await evaluate(`document.querySelector('#graphics-menu form').requestSubmit()`);
    await waitFor(`!document.querySelector('#graphics-menu').open`);
}
async function snapshot(name) {
    const shot = await send('Page.captureScreenshot', {format: 'png'});
    fs.mkdirSync('artifacts/graphics-menu', {recursive: true});
    fs.writeFileSync(`artifacts/graphics-menu/${name}.png`, Buffer.from(shot.data, 'base64'));
}

try {
    await send('Page.addScriptToEvaluateOnNewDocument', {source: `
        ${graphicsPreferenceFixture(true)}
        ${uiInspectionSource}
        window.__camera = null; window.__frames = 0; window.__samplers = []; window.__rayBytes = 0; window.__imageCopies = 0;
        const fetchOriginal = window.fetch;
        window.fetch = async function(input, ...args) {
            const response = await fetchOriginal.call(this, input, ...args);
            if (String(input?.url ?? input).includes('graphics-settings.json')) {
                const settings = await response.json();
                Object.assign(settings, {width: 640, height: 360, targetFPS: 30, maxAnisotropy: 4,
                    columnInterlaceFactor: 1, fovDegrees: 90, renderDistance: 60, fogStartDistance: 35});
                return new Response(JSON.stringify(settings), {headers: {'Content-Type': 'application/json'}});
            }
            return response;
        };
        const write = GPUQueue.prototype.writeBuffer;
        GPUQueue.prototype.writeBuffer = function(buffer, offset, data, ...args) {
            if (buffer.label === 'WolfNET Camera') {
                window.__camera = Array.from(new Float32Array(data.buffer ?? data, data.byteOffset ?? 0, 16));
                window.__frames++;
            }
            return write.call(this, buffer, offset, data, ...args);
        };
        const sampler = GPUDevice.prototype.createSampler;
        GPUDevice.prototype.createSampler = function(descriptor) {
            if (descriptor.label === 'WolfNET Wall and Sprite Filtering') window.__samplers.push(descriptor.maxAnisotropy);
            return sampler.call(this, descriptor);
        };
        const buffer = GPUDevice.prototype.createBuffer;
        GPUDevice.prototype.createBuffer = function(descriptor) {
            if (descriptor.label === 'WolfNET Ray Hits') window.__rayBytes = descriptor.size;
            return buffer.call(this, descriptor);
        };
        const copy = GPUQueue.prototype.copyExternalImageToTexture;
        GPUQueue.prototype.copyExternalImageToTexture = function(...args) {window.__imageCopies++; return copy.apply(this, args);};
    `});
    await send('Emulation.setDeviceMetricsOverride', {width: 1280, height: 850, deviceScaleFactor: 1, mobile: false});
    await send('Page.navigate', {url: base}); await send('Page.bringToFront');
    await waitFor(`window.__camera && !document.querySelector('.loading-overlay') && window.__uiImages.length === 2`);
    assert.equal(await evaluate(`document.querySelector('.map-switcher select')`), null, 'Graphics controls still clutter the right panel');
    assert.equal(await evaluate(`document.querySelector('#graphics-menu').open`), false);

    const point = await evaluate(`(() => {const r=document.querySelector('canvas').getBoundingClientRect();
        return {x:r.x+r.width/2,y:r.y+r.height/2};})()`);
    await send('Input.dispatchMouseEvent', {type: 'mousePressed', ...point, button: 'left', clickCount: 1});
    await send('Input.dispatchMouseEvent', {type: 'mouseReleased', ...point, button: 'left', clickCount: 1});
    await waitFor(`document.pointerLockElement && !document.querySelector('.car-paused')`);
    await waitFor(`document.querySelector('.race-total')?.textContent.trim() !== '0:00.00'`);

    await open();
    await waitFor(`!document.pointerLockElement && !!document.querySelector('.car-paused')`);
    // Timing labels refresh at 10 Hz; capture the final paused value rather than
    // the last label painted before pointer release.
    await delay(200);
    const before = await evaluate(`({pose:window.__camera.slice(0,4),total:document.querySelector('.race-total').textContent,
        copies:window.__imageCopies})`);
    assert.equal(await evaluate(`document.querySelectorAll('#graphics-menu input, #graphics-menu select').length`), 7);
    assert.equal(await evaluate(`document.querySelector('#graphics-width, #graphics-height')`), null);
    assert(await evaluate(`document.querySelector('#graphics-resolution').options.length >= 10`), 'Missing resolution presets');
    await snapshot('desktop');
    for (let i = 0; i < 16; i++) {
        await send('Input.dispatchKeyEvent', {type: 'keyDown', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9});
        await send('Input.dispatchKeyEvent', {type: 'keyUp', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9});
        assert(await evaluate(`document.querySelector('#graphics-menu').contains(document.activeElement)`), 'Tab escaped the modal');
    }
    for (const [id, value] of Object.entries({'graphics-resolution': '800x600', 'graphics-fps': 45,
        'graphics-anisotropy': 8, 'column-interlace': 2, 'graphics-fov': 110, 'graphics-distance': 75, 'graphics-fog': 50}))
        await set(id, value);
    await apply();
    await waitFor('window.__camera[6] === 800 && window.__camera[7] === 600 && window.__camera[12] === 2');
    const after = await evaluate(`({camera:window.__camera,rayBytes:window.__rayBytes,anisotropy:window.__samplers.at(-1),
        copies:window.__imageCopies,total:document.querySelector('.race-total').textContent,
        width:document.querySelector('canvas').width,height:document.querySelector('canvas').height})`);
    assert.deepEqual(after.camera.slice(0, 4), before.pose, 'Applying settings moved the camera');
    assert.equal(after.total, before.total, 'Applying graphics reset the race timer');
    assert.equal(after.copies, before.copies, 'Applying graphics reloaded scene images');
    assert.equal(after.rayBytes, 800 * 16); assert.equal(after.anisotropy, 8);
    assert.equal(after.width, 800); assert.equal(after.height, 600);
    assert(Math.abs(Math.hypot(after.camera[4], after.camera[5]) - Math.tan(55 * Math.PI / 180)) < .0001);
    assert.equal(after.camera[10], 75); assert.equal(after.camera[11], 50);
    const frameStart = await evaluate('window.__frames');
    await delay(1200);
    const frames = await evaluate(`window.__frames - ${frameStart}`);
    assert(frames >= 49 && frames <= 60, 'The new 45 FPS cap was not applied');
    console.log('PASS: right-panel menu, focus trap, pausing, all eight live settings, preserved race, and no texture reload.');

    await open(); await set('graphics-fog', 80);
    await evaluate(`document.querySelector('#graphics-menu form').requestSubmit()`);
    await waitFor(`document.querySelector('.graphics-validation')?.textContent.includes('Fog must start')`);
    assert.equal(await evaluate('window.__camera[11]'), 50, 'Invalid fog was applied');
    await evaluate(`Array.from(document.querySelectorAll('#graphics-menu button')).find(b=>b.textContent==='Cancel').click()`);
    await waitFor(`!document.querySelector('#graphics-menu').open`);
    await open();
    assert.equal(await evaluate(`document.querySelector('#graphics-fog').value`), '50', 'Cancel retained the invalid draft');
    await send('Input.dispatchKeyEvent', {type: 'keyDown', key: 'Escape', code: 'Escape', windowsVirtualKeyCode: 27});
    await send('Input.dispatchKeyEvent', {type: 'keyUp', key: 'Escape', code: 'Escape', windowsVirtualKeyCode: 27});
    await waitFor(`!document.querySelector('#graphics-menu').open`);
    assert(await evaluate(`document.activeElement.matches('.graphics-open')`), 'Closing did not restore button focus');

    await send('Page.reload', {ignoreCache: true});
    await waitFor(`window.__camera?.[6] === 800 && window.__camera?.[12] === 2 && !document.querySelector('.loading-overlay')`);
    await open();
    assert.equal(await evaluate(`document.querySelector('#graphics-fps').value`), '45');
    assert.equal(await evaluate(`document.querySelector('#graphics-anisotropy').value`), '8');
    await evaluate(`Array.from(document.querySelectorAll('#graphics-menu button')).find(b=>b.textContent==='Reset defaults').click()`);
    await waitFor(`document.querySelector('#graphics-resolution').value === '640x360'`);
    assert.equal(await evaluate('window.__camera[6]'), 800, 'Reset applied before confirmation');
    await apply(); await waitFor('window.__camera[6] === 640 && window.__camera[12] === 1');

    await send('Emulation.setDeviceMetricsOverride', {width: 390, height: 850, deviceScaleFactor: 1, mobile: false});
    await open();
    assert(await evaluate(`(() => {const r=document.querySelector('#graphics-menu').getBoundingClientRect();
        return r.left>=0 && r.right<=innerWidth && r.top>=0 && r.bottom<=innerHeight;})()`), 'Menu exceeds the narrow viewport');
    await snapshot('narrow');
    await evaluate(`localStorage.setItem('race-imperialis.graphics.v1', '{broken')`);
    await send('Page.reload', {ignoreCache: true});
    await waitFor('window.__camera?.[6] === 640 && !document.querySelector(".loading-overlay")');
    assert.deepEqual(errors, []);
    console.log('PASS: validation, cancel, Escape, restored focus, saved preferences, reset defaults, narrow layout, and invalid-save fallback.');
} finally { await check.close(); }
