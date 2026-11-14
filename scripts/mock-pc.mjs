#!/usr/bin/env node
/**
 * RigSight mock PC.
 *
 * A dependency-free WebSocket server that speaks the RigSight protocol. Use it
 * to exercise the Android app without sensors, PresentMon, or elevation.
 *
 *   node scripts/mock-pc.mjs
 *   node scripts/mock-pc.mjs --port 48000 --prompt
 *   node scripts/mock-pc.mjs --open --auto --duration 30
 *
 * First connection needs the pairing code (default 123456). The phone receives
 * a token (default dev-token) and reconnects with it after that.
 */

import crypto from 'node:crypto';
import http from 'node:http';
import os from 'node:os';

const PROTOCOL_VERSION = 1;
const WS_PATH = '/v1/stream';
const MAX_FRAME_BYTES = 8 * 1024 * 1024;

const GAMES = [
    { id: 'starfall', name: 'Starfall Odyssey', exeName: 'StarfallOdyssey.exe' },
    { id: 'neon-drift', name: 'Neon Drift', exeName: 'NeonDrift.exe' },
];

const options = {
    host: '0.0.0.0',
    port: 47823,
    token: 'dev-token',
    code: '123456',
    pcName: 'MOCK-RIG',
    prompt: false,
    auto: false,
    duration: 0,
    open: false,
};

parseArgs();

const sessions = [];
let active = null;
let pendingPrompt = null;
let pendingPromptTimer = null;
let firstAuthHandled = false;

seedHistory();

const clients = new Set();

const server = http.createServer((request, response) => {
    if (request.method === 'GET' && request.url === '/health') {
        response.writeHead(200, { 'Content-Type': 'text/plain' });
        response.end('ok');
        return;
    }
    response.writeHead(426, { 'Content-Type': 'text/plain' });
    response.end('RigSight mock PC: connect with a WebSocket client at /v1/stream\n');
});

server.on('upgrade', (request, socket, head) => {
    let pathname;
    try {
        pathname = new URL(request.url, 'http://localhost').pathname;
    } catch {
        socket.write('HTTP/1.1 400 Bad Request\r\n\r\n');
        socket.destroy();
        return;
    }

    if (pathname !== WS_PATH) {
        socket.write('HTTP/1.1 404 Not Found\r\n\r\n');
        socket.destroy();
        return;
    }

    const key = request.headers['sec-websocket-key'];
    const upgrade = String(request.headers.upgrade || '').toLowerCase();
    if (!key || upgrade !== 'websocket') {
        socket.write('HTTP/1.1 400 Bad Request\r\n\r\n');
        socket.destroy();
        return;
    }

    const accept = crypto
        .createHash('sha1')
        .update(key + '258EAFA5-E914-47DA-95CA-C5AB0DC85B11')
        .digest('base64');

    socket.write(
        'HTTP/1.1 101 Switching Protocols\r\n' +
        'Upgrade: websocket\r\n' +
        'Connection: Upgrade\r\n' +
        `Sec-WebSocket-Accept: ${accept}\r\n` +
        '\r\n',
    );

    if (head && head.length > 0) socket.unshift(head);

    const client = new Client(socket, handleText);
    clients.add(client);
    log(`Connection from ${client.address}`);
});

server.on('error', (error) => {
    console.error(`Mock PC failed: ${error.message}`);
    process.exit(1);
});

server.listen(options.port, options.host, () => {
    console.log('');
    console.log('RigSight mock PC');
    console.log(`  Local:       ws://localhost:${options.port}${WS_PATH}`);
    for (const address of lanAddresses()) {
        console.log(`  LAN:         ws://${address}:${options.port}${WS_PATH}`);
    }
    console.log(`  PC name:     ${options.pcName}`);
    console.log(`  Auth:        ${options.open ? 'open' : `code ${options.code}, token ${options.token}`}`);
    console.log(`  Games:       ${GAMES.map((game) => game.name).join(', ')}`);
    console.log(`  Sessions:    ${sessions.length} seeded in history`);
    console.log('  Modes:       --prompt (prompt after connect), --auto (start now), --duration N');
    console.log('  Stop:        Ctrl+C');
    console.log('');
});

process.on('SIGINT', shutdown);
process.on('SIGTERM', shutdown);

// --------------------------------------------------------------------- setup

function parseArgs() {
    const argv = process.argv.slice(2);
    for (let i = 0; i < argv.length; i += 1) {
        const arg = argv[i];
        switch (arg) {
            case '--host':
                options.host = requireValue(argv, ++i, arg);
                break;
            case '--port':
                options.port = Number(requireValue(argv, ++i, arg));
                break;
            case '--token':
                options.token = requireValue(argv, ++i, arg);
                break;
            case '--code':
                options.code = requireValue(argv, ++i, arg);
                break;
            case '--pc-name':
                options.pcName = requireValue(argv, ++i, arg);
                break;
            case '--duration':
                options.duration = Number(requireValue(argv, ++i, arg));
                break;
            case '--prompt':
                options.prompt = true;
                break;
            case '--auto':
                options.auto = true;
                break;
            case '--open':
                options.open = true;
                break;
            case '--help':
            case '-h':
                printHelp();
                process.exit(0);
                break;
            default:
                fail(`Unknown option: ${arg}`);
        }
    }

    if (!Number.isInteger(options.port) || options.port < 1 || options.port > 65535) {
        fail('--port must be an integer between 1 and 65535.');
    }
    if (!Number.isFinite(options.duration) || options.duration < 0) {
        fail('--duration must be zero or a positive number of seconds.');
    }
    if (!/^\d{6}$/.test(options.code)) {
        fail('--code must be exactly six digits.');
    }
}

function requireValue(argv, index, flag) {
    const value = argv[index];
    if (value === undefined || value.startsWith('--')) fail(`${flag} needs a value.`);
    return value;
}

function printHelp() {
    console.log(`
RigSight mock PC

Usage:
  node scripts/mock-pc.mjs [options]

Options:
  --port <n>        WebSocket port (default 47823)
  --host <ip>       Bind address (default 0.0.0.0)
  --pc-name <name>  Name shown on the phone (default MOCK-RIG)
  --token <text>    Token issued after pairing (default dev-token)
  --code <digits>   Six-digit pairing code (default 123456)
  --open            Accept clients without a token or code
  --prompt          Send a game prompt shortly after the phone connects
  --auto            Start a session as soon as the phone connects
  --duration <s>    Auto-stop a started session after this many seconds
  --help, -h        Show this help
`);
}

function fail(message) {
    console.error(`mock-pc: ${message}`);
    process.exit(1);
}

function lanAddresses() {
    const result = [];
    for (const interfaces of Object.values(os.networkInterfaces())) {
        for (const entry of interfaces || []) {
            if (entry && entry.family === 'IPv4' && !entry.internal) result.push(entry.address);
        }
    }
    return [...new Set(result)];
}

function log(message) {
    const stamp = new Date().toISOString().slice(11, 19);
    console.log(`[${stamp}] ${message}`);
}

// -------------------------------------------------------------------- history

function seedHistory() {
    const now = Date.now();
    sessions.push(createSeedSession(
        'mock-starfall-001',
        GAMES[0],
        now - (26 * 60 * 60 * 1000),
        22 * 60 * 1000,
        'game_exit',
        138,
    ));
    sessions.push(createSeedSession(
        'mock-neondrift-001',
        GAMES[1],
        now - (50 * 60 * 60 * 1000),
        41 * 60 * 1000,
        'user',
        96,
    ));
}

function createSeedSession(id, game, startedAtMs, durationMs, stopReason, baseFps) {
    const info = {
        id,
        gameId: game.id,
        gameName: game.name,
        exeName: game.exeName,
        startedAt: new Date(startedAtMs).toISOString(),
        sampleIntervalMs: 500,
    };
    const samples = [];
    for (let t = 0; t <= durationMs; t += 500) {
        samples.push(sampleAt(t, baseFps));
    }
    return { summary: summarize(info, samples, stopReason), samples };
}

// -------------------------------------------------------------------- session

function startSession(game, promptId) {
    if (active) return false;

    if (promptId && pendingPrompt && pendingPrompt.id === promptId) {
        clearTimeout(pendingPromptTimer);
        const prompt = pendingPrompt;
        pendingPrompt = null;
        broadcast({ type: 'prompt_closed', promptId: prompt.id, reason: 'started' });
    }

    const startedMs = Date.now();
    active = {
        info: {
            id: `mock-${startedMs.toString(36)}-${crypto.randomBytes(2).toString('hex')}`,
            gameId: game.id,
            gameName: game.name,
            exeName: game.exeName,
            startedAt: new Date(startedMs).toISOString(),
            sampleIntervalMs: 500,
        },
        samples: [],
        startedMs,
        timer: null,
        stopTimer: null,
    };

    broadcast({ type: 'session_started', session: active.info });
    tickSession();
    active.timer = setInterval(tickSession, 500);
    if (options.duration > 0) {
        active.stopTimer = setTimeout(() => finishSession('user'), options.duration * 1000);
    }
    log(`Session started: ${game.name}`);
    return true;
}

function tickSession() {
    if (!active) return;
    const sample = sampleAt(Date.now() - active.startedMs);
    active.samples.push(sample);
    broadcast({ type: 'sample', sample });
}

function finishSession(stopReason) {
    if (!active) return;
    const finished = active;
    active = null;
    clearInterval(finished.timer);
    if (finished.stopTimer) clearTimeout(finished.stopTimer);

    const summary = summarize(finished.info, finished.samples, stopReason);
    sessions.unshift({ summary, samples: finished.samples });
    broadcast({ type: 'session_ended', summary });
    log(`Session ended: ${summary.gameName} (${summary.sampleCount} samples, ${stopReason})`);
}

function sampleAt(t, baseFps = 144) {
    const frameWobble = Math.sin(t / 2600) * 18 + Math.sin(t / 700) * 5;
    const fps = Math.max(30, baseFps + frameWobble);
    const loadWobble = Math.sin(t / 2300);
    return {
        t,
        fps: round(fps, 1),
        frameTimeMs: round(1000 / fps, 2),
        low1Fps: round(fps * 0.78, 1),
        low01Fps: round(fps * 0.62, 1),
        cpuLoadPct: round(38 + 9 * Math.sin(t / 5100), 1),
        cpuTempC: round(61 + 5 * Math.sin(t / 3700), 1),
        cpuPowerW: round(88 + 14 * Math.sin(t / 2900), 1),
        cpuClockMhz: 5088,
        gpuLoadPct: round(94 + 4 * loadWobble, 1),
        gpuTempC: round(68 + 4 * Math.sin(t / 4400), 1),
        gpuHotspotC: round(80 + 5 * Math.sin(t / 4400), 1),
        gpuPowerW: round(300 + 30 * Math.sin(t / 3100), 1),
        gpuClockMhz: 1920,
        vramUsedMb: 7800 + Math.round(300 * Math.sin(t / 8000)),
        vramTotalMb: 12288,
        ramUsedMb: 18200 + Math.round(600 * Math.sin(t / 9000)),
        ramTotalMb: 32768,
    };
}

function summarize(info, samples, stopReason) {
    const fpsValues = samples.map((sample) => sample.fps).filter(Number.isFinite);
    const sortedFps = [...fpsValues].sort((a, b) => a - b);
    const medianFps = sortedFps.length > 0
        ? sortedFps[Math.floor(sortedFps.length / 2)]
        : null;
    const lastT = samples.length > 0 ? samples[samples.length - 1].t : 0;
    const durationMs = samples.length > 0 ? lastT + (info.sampleIntervalMs || 500) : 0;

    return {
        id: info.id,
        gameId: info.gameId,
        gameName: info.gameName,
        exeName: info.exeName,
        startedAt: info.startedAt,
        endedAt: new Date(Date.parse(info.startedAt) + durationMs).toISOString(),
        durationMs,
        sampleCount: samples.length,
        avgFps: round(avg(samples, 'fps'), 1),
        medianFps: round(medianFps, 1),
        low1Fps: round(avg(samples, 'low1Fps'), 1),
        low01Fps: round(avg(samples, 'low01Fps'), 1),
        avgFrameTimeMs: round(avg(samples, 'frameTimeMs'), 2),
        maxCpuTempC: round(peak(samples, 'cpuTempC'), 1),
        maxGpuTempC: round(peak(samples, 'gpuTempC'), 1),
        maxCpuPowerW: round(peak(samples, 'cpuPowerW'), 1),
        maxGpuPowerW: round(peak(samples, 'gpuPowerW'), 1),
        avgCpuLoadPct: round(avg(samples, 'cpuLoadPct'), 1),
        avgGpuLoadPct: round(avg(samples, 'gpuLoadPct'), 1),
        maxVramUsedMb: round(peak(samples, 'vramUsedMb'), 0),
        stopReason,
        schemaVersion: 1,
    };
}

function avg(samples, key) {
    const values = samples.map((sample) => sample[key]).filter(Number.isFinite);
    if (values.length === 0) return null;
    return values.reduce((total, value) => total + value, 0) / values.length;
}

function peak(samples, key) {
    const values = samples.map((sample) => sample[key]).filter(Number.isFinite);
    return values.length > 0 ? Math.max(...values) : null;
}

function round(value, digits = 1) {
    return Number.isFinite(value) ? Number(value.toFixed(digits)) : null;
}

function downsample(samples, maxSamples) {
    if (samples.length <= maxSamples) return samples;
    const result = [];
    const step = (samples.length - 1) / (maxSamples - 1);
    for (let i = 0; i < maxSamples; i += 1) {
        result.push(samples[Math.round(i * step)]);
    }
    return result;
}

// -------------------------------------------------------------------- prompts

function emitPrompt() {
    if (active || pendingPrompt) return;
    const game = GAMES[0];
    pendingPrompt = {
        id: `prompt-${Date.now().toString(36)}`,
        gameId: game.id,
        gameName: game.name,
        exeName: game.exeName,
        processId: 4812,
        expiresAt: Date.now() + 60_000,
    };
    broadcast({ type: 'prompt', prompt: pendingPrompt });
    log(`Prompt sent: ${game.name} (valid 60s)`);

    pendingPromptTimer = setTimeout(() => {
        if (!pendingPrompt) return;
        const promptId = pendingPrompt.id;
        pendingPrompt = null;
        broadcast({ type: 'prompt_closed', promptId, reason: 'expired' });
        log('Prompt expired.');
    }, 60_000);
}

function clearPendingPrompt(reason) {
    if (!pendingPrompt) return;
    const promptId = pendingPrompt.id;
    pendingPrompt = null;
    clearTimeout(pendingPromptTimer);
    broadcast({ type: 'prompt_closed', promptId, reason });
}

// ------------------------------------------------------------------ WebSocket

function handleText(client, text) {
    let message;
    try {
        message = JSON.parse(text);
    } catch {
        client.send({ type: 'error', code: 'bad_json', message: 'Could not parse message.' });
        return;
    }
    if (!message || typeof message.type !== 'string') {
        client.send({ type: 'error', code: 'bad_json', message: 'Message type is required.' });
        return;
    }

    if (!client.authenticated) {
        if (message.type !== 'hello') {
            client.send({ type: 'error', code: 'unauthorized', message: 'Send hello first.' });
            client.close(1000, 'unauthorized');
            return;
        }
        handleHello(client, message);
        return;
    }

    handleCommand(client, message);
}

function handleHello(client, hello) {
    if (hello.protocolVersion !== PROTOCOL_VERSION) {
        client.send({
            type: 'error',
            code: 'protocol_mismatch',
            message: `PC speaks protocol v${PROTOCOL_VERSION}; update the app.`,
        });
        client.close(1000, 'protocol mismatch');
        return;
    }

    const auth = authenticate(hello);
    if (!auth.ok) {
        client.send({
            type: 'error',
            code: 'unauthorized',
            message: 'Unknown device. Pair with the code shown on the PC.',
        });
        client.close(1000, 'unauthorized');
        log(`Rejected ${client.address} (bad token or code).`);
        return;
    }

    client.authenticated = true;
    client.deviceName = hello.deviceName || 'Phone';
    client.send({
        type: 'hello_ack',
        protocolVersion: PROTOCOL_VERSION,
        serverVersion: '0.1.0-mock',
        pcName: options.pcName,
        pcId: 'mock-pc',
        token: auth.issueToken ? options.token : null,
        games: GAMES,
        session: active ? active.info : null,
    });

    if (pendingPrompt) client.send({ type: 'prompt', prompt: pendingPrompt });
    if (active) {
        client.send({ type: 'session_started', session: active.info });
        const lastSample = active.samples[active.samples.length - 1];
        if (lastSample) client.send({ type: 'sample', sample: lastSample });
    }

    log(`Phone connected: ${client.deviceName} (${client.address})`);
    onFirstAuth();
}

function authenticate(hello) {
    if (options.open) {
        return { ok: true, issueToken: !hello.token };
    }
    if (typeof hello.token === 'string' && hello.token === options.token) {
        return { ok: true, issueToken: false };
    }
    if (typeof hello.code === 'string' && hello.code === options.code) {
        return { ok: true, issueToken: true };
    }
    return { ok: false };
}

function onFirstAuth() {
    if (firstAuthHandled) return;
    firstAuthHandled = true;
    if (options.auto) {
        setTimeout(() => {
            if (!active) startSession(GAMES[0], null);
        }, 800);
    } else if (options.prompt) {
        setTimeout(emitPrompt, 2500);
    }
}

function handleCommand(client, message) {
    switch (message.type) {
        case 'ping':
            client.send({ type: 'pong' });
            return;

        case 'list_sessions':
            client.send({
                type: 'sessions',
                sessions: sessions.map((entry) => entry.summary),
            });
            return;

        case 'get_session': {
            const found = sessions.find((entry) => entry.summary.id === message.sessionId);
            if (!found) {
                client.send({ type: 'error', code: 'not_found', message: 'Session not found.' });
                return;
            }
            client.send({
                type: 'session_detail',
                summary: found.summary,
                samples: downsample(found.samples, 1500),
            });
            return;
        }

        case 'start_session': {
            if (active) {
                client.send({ type: 'error', code: 'busy', message: 'A session is already recording.' });
                return;
            }
            let game = null;
            let promptId = null;

            if (message.promptId) {
                if (!pendingPrompt || pendingPrompt.id !== message.promptId) {
                    client.send({ type: 'error', code: 'not_found', message: 'That prompt is no longer active.' });
                    return;
                }
                promptId = pendingPrompt.id;
                game = GAMES.find((entry) => entry.id === pendingPrompt.gameId) || null;
            } else if (message.gameId) {
                game = GAMES.find((entry) => entry.id === message.gameId) || null;
            } else if (pendingPrompt) {
                promptId = pendingPrompt.id;
                game = GAMES.find((entry) => entry.id === pendingPrompt.gameId) || null;
            }

            if (!game) {
                client.send({ type: 'error', code: 'not_found', message: 'Unknown game.' });
                return;
            }
            startSession(game, promptId);
            return;
        }

        case 'decline_prompt': {
            if (pendingPrompt && (!message.promptId || pendingPrompt.id === message.promptId)) {
                clearPendingPrompt('declined');
                log('Prompt declined.');
            }
            return;
        }

        case 'stop_session': {
            if (!active) {
                client.send({ type: 'error', code: 'not_found', message: 'No active session.' });
                return;
            }
            if (message.sessionId && message.sessionId !== active.info.id) {
                client.send({ type: 'error', code: 'not_found', message: 'Session not found.' });
                return;
            }
            finishSession('phone');
            return;
        }

        default:
            client.send({
                type: 'error',
                code: 'unknown_type',
                message: `Unknown message type: ${message.type}`,
            });
    }
}

function broadcast(message) {
    const text = JSON.stringify(message);
    for (const client of clients) {
        if (client.authenticated && !client.closed) client.sendText(text);
    }
}

function frame(opcode, payload) {
    const body = Buffer.isBuffer(payload) ? payload : Buffer.from(payload);
    const length = body.length;
    let header;
    if (length < 126) {
        header = Buffer.alloc(2);
        header[1] = length;
    } else if (length < 65536) {
        header = Buffer.alloc(4);
        header[1] = 126;
        header.writeUInt16BE(length, 2);
    } else {
        header = Buffer.alloc(10);
        header[1] = 127;
        header.writeBigUInt64BE(BigInt(length), 2);
    }
    header[0] = 0x80 | opcode;
    return Buffer.concat([header, body]);
}

class Client {
    constructor(socket, onText) {
        this.socket = socket;
        this.onText = onText;
        this.address = String(socket.remoteAddress || 'unknown').replace(/^::ffff:/, '');
        this.buffer = Buffer.alloc(0);
        this.fragments = [];
        this.fragmentOpcode = 0;
        this.authenticated = false;
        this.deviceName = null;
        this.closed = false;

        socket.on('data', (chunk) => this.receive(chunk));
        socket.on('close', () => this.cleanup());
        socket.on('error', () => this.cleanup());
    }

    send(message) {
        this.sendText(JSON.stringify(message));
    }

    sendText(text) {
        if (this.closed || !this.socket.writable) return;
        try {
            this.socket.write(frame(0x1, Buffer.from(text, 'utf8')));
        } catch {
            this.cleanup();
        }
    }

    close(code = 1000, reason = '') {
        if (this.closed) return;
        this.closed = true;
        const reasonBuffer = Buffer.from(reason, 'utf8');
        const payload = Buffer.alloc(2 + reasonBuffer.length);
        payload.writeUInt16BE(code, 0);
        reasonBuffer.copy(payload, 2);
        try {
            this.socket.write(frame(0x8, payload));
            this.socket.end();
        } catch {
            // Socket is already gone.
        }
    }

    cleanup() {
        this.closed = true;
        if (clients.delete(this)) log(`Disconnected: ${this.deviceName || this.address}`);
    }

    receive(chunk) {
        if (this.closed) return;
        this.buffer = Buffer.concat([this.buffer, chunk]);

        while (!this.closed) {
            if (this.buffer.length < 2) return;

            const first = this.buffer[0];
            const second = this.buffer[1];
            const fin = (first & 0x80) !== 0;
            const opcode = first & 0x0f;
            const masked = (second & 0x80) !== 0;
            let length = second & 0x7f;
            let offset = 2;

            if (length === 126) {
                if (this.buffer.length < 4) return;
                length = this.buffer.readUInt16BE(2);
                offset = 4;
            } else if (length === 127) {
                if (this.buffer.length < 10) return;
                const bigLength = this.buffer.readBigUInt64BE(2);
                if (bigLength > BigInt(MAX_FRAME_BYTES)) {
                    this.close(1009, 'message too big');
                    return;
                }
                length = Number(bigLength);
                offset = 10;
            }

            let maskKey = null;
            if (masked) {
                if (this.buffer.length < offset + 4) return;
                maskKey = this.buffer.subarray(offset, offset + 4);
                offset += 4;
            }

            if (this.buffer.length < offset + length) return;

            let payload = Buffer.from(this.buffer.subarray(offset, offset + length));
            this.buffer = this.buffer.subarray(offset + length);
            if (maskKey) {
                for (let i = 0; i < payload.length; i += 1) {
                    payload[i] ^= maskKey[i % 4];
                }
            }

            if (opcode === 0x8) {
                this.close(1000, '');
                return;
            }
            if (opcode === 0x9) {
                if (!this.closed && this.socket.writable) this.socket.write(frame(0xA, payload));
                continue;
            }
            if (opcode === 0xA) continue;
            if (opcode !== 0x0 && opcode !== 0x1 && opcode !== 0x2) {
                this.close(1002, 'unsupported opcode');
                return;
            }

            if (opcode === 0x0) {
                if (this.fragmentOpcode === 0) {
                    this.close(1002, 'unexpected continuation');
                    return;
                }
                this.fragments.push(payload);
            } else {
                if (this.fragmentOpcode !== 0) {
                    this.close(1002, 'message interrupted');
                    return;
                }
                this.fragmentOpcode = opcode;
                this.fragments = [payload];
            }

            if (!fin) continue;

            const messageOpcode = this.fragmentOpcode;
            const body = Buffer.concat(this.fragments);
            this.fragments = [];
            this.fragmentOpcode = 0;

            if (messageOpcode === 0x1) {
                this.onText(this, body.toString('utf8'));
            }
        }
    }
}

function shutdown() {
    console.log('');
    log('Shutting down.');
    if (active?.timer) clearInterval(active.timer);
    if (active?.stopTimer) clearTimeout(active.stopTimer);
    if (pendingPromptTimer) clearTimeout(pendingPromptTimer);
    for (const client of clients) client.close(1001, 'mock shutting down');
    server.close(() => process.exit(0));
    setTimeout(() => process.exit(0), 500).unref();
}
