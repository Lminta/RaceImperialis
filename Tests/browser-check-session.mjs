import assert from 'node:assert/strict';

export function graphicsPreferenceFixture(persistAcrossReloads = false) {
    return `
        const graphicsKey = 'race-imperialis.graphics.v1', originalKey = '__graphicsTestOriginal';
        const firstGraphicsLoad = sessionStorage.getItem(originalKey) === null;
        if (firstGraphicsLoad) sessionStorage.setItem(originalKey, JSON.stringify(localStorage.getItem(graphicsKey)));
        if (firstGraphicsLoad || ${!persistAcrossReloads}) localStorage.removeItem(graphicsKey);
        window.__restoreGraphicsPreferences = () => {
            const original = JSON.parse(sessionStorage.getItem(originalKey));
            if (original === null) localStorage.removeItem(graphicsKey); else localStorage.setItem(graphicsKey, original);
            sessionStorage.removeItem(originalKey);
        };
    `;
}

export async function createBrowserCheck(port) {
    const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, {method: 'PUT'})).json();
    const socket = new WebSocket(target.webSocketDebuggerUrl);
    await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
    let id = 0;
    const pending = new Map(), errors = [], logs = [];
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
            logs.push(text);
            if (message.params.type === 'error' || /Initialization failed|Frame failed|Uncaptured error|Could not load map/.test(text)) errors.push(text);
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
        throw Error(`Timed out: ${expression}\n${JSON.stringify({logs: logs.slice(-12), state: await evaluate(`({
            locked:!!document.pointerLockElement, active:document.activeElement?.outerHTML?.slice(0,300),
            dialog:document.querySelector('#graphics-menu')?.outerHTML?.slice(0,300), text:document.body.innerText.slice(-1500)})`)})}`);
    }
    await send('Page.enable'); await send('Runtime.enable'); await send('Network.enable');
    await send('Network.setCacheDisabled', {cacheDisabled: true});
    return {send, evaluate, waitFor, errors, logs, async close() {
        await evaluate('window.__restoreGraphicsPreferences?.()').catch(() => {});
        await send('Page.close').catch(() => {}); socket.close();
        for (const task of pending.values()) clearTimeout(task.timer);
    }};
}
