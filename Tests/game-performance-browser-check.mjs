// node Tests/game-performance-browser-check.mjs 9273 http://127.0.0.1:5289 baseline
// Match the reported low graphics settings and measure real active gameplay.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import {createBrowserCheck, graphicsPreferenceFixture} from './browser-check-session.mjs';

const check = await createBrowserCheck(process.argv[2] ?? '9273');
const {send, evaluate, waitFor, logs, errors} = check;
const base = process.argv[3] ?? 'http://localhost:5280', label = process.argv[4] ?? 'current';
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
const percentile = (values, p) => values.toSorted((a, b) => a - b)[Math.floor((values.length - 1) * p)];
async function key(code, down = true) {
    await send('Input.dispatchKeyEvent', {type: down ? 'keyDown' : 'keyUp', code,
        key: code === 'Escape' ? 'Escape' : code.slice(3).toLowerCase(),
        windowsVirtualKeyCode: code === 'Escape' ? 27 : code.charCodeAt(3)});
}
function profiles(start, prefix) {
    const samples = logs.slice(start).filter(line => line.startsWith(prefix)).map(line => JSON.parse(line.slice(prefix.length)));
    const rows = samples.length > 2 ? samples.slice(1, -1) : samples;
    const frames = rows.reduce((total, row) => total + row.frames, 0);
    return Object.fromEntries(Object.keys(rows[0] ?? {}).filter(key => key !== 'frames')
        .map(key => [key, rows.reduce((total, row) => total + row[key] * row.frames, 0) / frames]));
}
const results = [];
async function sample(scenario) {
    const start = logs.length;
    await evaluate('window.__frames=0;window.__cpu=[];window.__measure=true');
    await delay(4000);
    assert.deepEqual(errors, []);
    const gpu = await evaluate('window.__readGpu()'), cpu = await evaluate('window.__cpu');
    assert(cpu.length > 0, logs.slice(-15).join('\n'));
    const result = {scenario, frames: cpu.length,
        fps: (cpu.length - 1) * 1000 / (cpu.at(-1).time - cpu[0].time),
        cpuMedianMs: percentile(cpu.map(row => row.ms), .5), cpuP95Ms: percentile(cpu.map(row => row.ms), .95),
        gpuMedianMs: gpu ? percentile(gpu, .5) : null, gpuP95Ms: gpu ? percentile(gpu, .95) : null,
        stages: profiles(start, '[FRAME PROFILE] '), renderer: profiles(start, '[PROFILE] ')};
    results.push(result); console.log(JSON.stringify(result));
}
try {
    await send('Page.addScriptToEvaluateOnNewDocument', {source: `
        ${graphicsPreferenceFixture()}
        window.__frames=0;window.__measure=false;window.__slot=null;window.__cpu=[];window.__camera=null;
        const fetchOriginal=window.fetch;
        window.fetch=async function(input,...args){
            const response=await fetchOriginal.call(this,input,...args);
            if(String(input?.url??input).includes('graphics-settings.json')){
                const settings=await response.json();
                Object.assign(settings,{width:800,height:600,targetFPS:120,maxAnisotropy:1,
                    columnInterlaceFactor:2,fovDegrees:60,renderDistance:40,fogStartDistance:30});
                return new Response(JSON.stringify(settings),{headers:{'Content-Type':'application/json'}});
            }
            return response;
        };
        const raf=window.requestAnimationFrame;
        window.requestAnimationFrame=function(callback){return raf.call(this,time=>{
            const count=window.__frames,start=performance.now();callback(time);
            if(window.__frames>count)window.__cpu.push({ms:performance.now()-start,time});
        });};
        const request=GPUAdapter.prototype.requestDevice;
        GPUAdapter.prototype.requestDevice=async function(descriptor={}){
            const timing=this.features.has('timestamp-query');
            const device=await request.call(this,{...descriptor,requiredFeatures:[...(descriptor.requiredFeatures??[]),...(timing?['timestamp-query']:[])]});
            window.__gpu=device;if(timing)window.__queries=device.createQuerySet({type:'timestamp',count:2048});return device;
        };
        const write=GPUQueue.prototype.writeBuffer;
        GPUQueue.prototype.writeBuffer=function(buffer,offset,data,...args){
            if(buffer.label==='WolfNET Camera'){
                window.__camera=Array.from(new Float32Array(data.buffer??data,data.byteOffset??0,16));
                window.__slot=window.__measure&&window.__frames<1024?window.__frames++:null;
            }
            return write.call(this,buffer,offset,data,...args);
        };
        const compute=GPUCommandEncoder.prototype.beginComputePass;
        GPUCommandEncoder.prototype.beginComputePass=function(descriptor={}){
            if(window.__slot!==null&&window.__queries)descriptor={...descriptor,timestampWrites:{querySet:window.__queries,beginningOfPassWriteIndex:window.__slot*2}};
            return compute.call(this,descriptor);
        };
        const submit=GPUQueue.prototype.submit;
        GPUQueue.prototype.submit=function(commands){
            if(window.__slot!==null&&window.__queries){
                const encoder=window.__gpu.createCommandEncoder();
                const pass=compute.call(encoder,{timestampWrites:{querySet:window.__queries,beginningOfPassWriteIndex:window.__slot*2+1}});
                pass.end();commands=[...commands,encoder.finish()];
            }
            window.__slot=null;return submit.call(this,commands);
        };
        window.__readGpu=async function(){
            window.__measure=false;if(!window.__queries)return null;
            const device=window.__gpu,bytes=window.__frames*16;
            const resolve=device.createBuffer({size:bytes,usage:GPUBufferUsage.QUERY_RESOLVE|GPUBufferUsage.COPY_SRC});
            const read=device.createBuffer({size:bytes,usage:GPUBufferUsage.COPY_DST|GPUBufferUsage.MAP_READ});
            const encoder=device.createCommandEncoder();encoder.resolveQuerySet(window.__queries,0,window.__frames*2,resolve,0);
            encoder.copyBufferToBuffer(resolve,0,read,0,bytes);device.queue.submit([encoder.finish()]);await read.mapAsync(GPUMapMode.READ);
            const stamps=new BigUint64Array(read.getMappedRange()),result=[];
            for(let i=0;i<stamps.length;i+=2)result.push(Number(stamps[i+1]-stamps[i])/1e6);
            read.unmap();read.destroy();resolve.destroy();return result;
        };
    `});
    await send('Emulation.setDeviceMetricsOverride', {width: 1280, height: 850, deviceScaleFactor: 1, mobile: false});
    await send('Page.navigate', {url: `${base.replace(/\/$/, '')}/?profile=1`}); await send('Page.bringToFront');
    await waitFor('window.__camera?.[6] === 800 && window.__camera?.[7] === 600 && window.__camera?.[12] === 2 && !document.querySelector(".loading-overlay")');
    await delay(1000); await sample('paused');
    const point = await evaluate(`(()=>{const r=document.querySelector('canvas').getBoundingClientRect();return{x:r.x+r.width/2,y:r.y+r.height/2}})()`);
    await send('Input.dispatchMouseEvent', {type: 'mousePressed', ...point, button: 'left', clickCount: 1});
    await send('Input.dispatchMouseEvent', {type: 'mouseReleased', ...point, button: 'left', clickCount: 1});
    await waitFor('!!document.pointerLockElement');
    await waitFor(`document.querySelector('.race-total')?.textContent.trim() !== '0:00.00'`);
    await key('KeyW'); await sample('driving-cockpit');
    await key('KeyA'); await sample('driving-steering'); await key('KeyA', false);
    await key('KeyV'); await key('KeyV', false); await sample('driving-chase');
    await key('KeyW', false); await key('Escape'); await key('Escape', false);
    assert.deepEqual(errors, []);
    fs.mkdirSync('artifacts/game-performance', {recursive: true});
    fs.writeFileSync(`artifacts/game-performance/${label.replace(/[^a-z0-9_-]/gi, '_')}.json`, JSON.stringify({
        settings: {width:800,height:600,targetFPS:120,maxAnisotropy:1,columnInterlaceFactor:2,fovDegrees:60,renderDistance:40,fogStartDistance:30},
        note:'CPU includes simulation, audio, rendering, and HUD; GPU covers WebGPU passes/copies and excludes browser composition.',results},null,2));
} finally { await check.close(); }
