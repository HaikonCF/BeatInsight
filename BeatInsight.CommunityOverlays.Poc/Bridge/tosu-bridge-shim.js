// BeatInsight — bridge tosu-compatible (POC).
// Injecté avant tout script de l'overlay (AddScriptToExecuteOnDocumentCreatedAsync).
// Remplace window.WebSocket pour les seuls endpoints tosu et route les messages
// via chrome.webview au lieu d'un serveur réseau : aucun port ouvert.
(() => {
    'use strict';
    const webview = window.chrome && window.chrome.webview;
    if (!webview || window.__beatInsightTosuBridge) return;

    const NativeWebSocket = window.WebSocket;
    const CHANNELS = {
        '/websocket/v2': 'v2',
        '/websocket/v2/precise': 'precise',
        '/websocket/commands': 'commands',
        '/ws': 'v1',
    };
    const TOSU_DEFAULT_PORT = '24050';

    const sockets = new Set();
    const latest = Object.create(null);
    let nextSocketId = 1;
    // Compteurs de diagnostic, lisibles via window.__beatInsightTosuBridge.stats().
    const counters = { created: 0, opened: 0, closed: 0, delivered: 0, sent: 0, native: 0 };
    // Profilage (V3.14-COM-04) : cadence de réception et latence hôte → JS par canal.
    const reception = Object.create(null);
    function recordReception(channel, ts) {
        const now = performance.timeOrigin + performance.now();
        const r = reception[channel] || (reception[channel] = { received: 0, first: now, last: now, latencies: [], sum: 0, max: 0 });
        r.received++;
        r.last = now;
        if (typeof ts === 'number') {
            const latency = Math.max(0, now - ts);
            r.sum += latency;
            if (latency > r.max) r.max = latency;
            r.latencies.push(latency);
            if (r.latencies.length > 2048) r.latencies.shift();
        }
    }
    function receptionStats() {
        const out = {};
        for (const [channel, r] of Object.entries(reception)) {
            const sorted = [...r.latencies].sort((a, b) => a - b);
            const p95 = sorted.length ? sorted[Math.min(sorted.length - 1, Math.ceil(sorted.length * 0.95) - 1)] : 0;
            const seconds = (r.last - r.first) / 1000;
            out[channel] = {
                received: r.received,
                ratePerSec: seconds > 0 ? +((r.received - 1) / seconds).toFixed(2) : 0,
                latencyMeanMs: r.received ? +(r.sum / r.received).toFixed(3) : 0,
                latencyP95Ms: +p95.toFixed(3),
                latencyMaxMs: +r.max.toFixed(3),
            };
        }
        return out;
    }

    // Destinations tosu interceptées : l'hôte de la page (overlays qui utilisent
    // window.location.host) ou l'adresse tosu par défaut 127.0.0.1/localhost:24050.
    // Tout autre WebSocket, y compris vers un autre port local, reste natif.
    function channelFor(url) {
        try {
            const u = new URL(String(url), location.href);
            if (u.protocol !== 'ws:' && u.protocol !== 'wss:') return null;
            const samePageHost = u.host === location.host;
            const tosuDefault = (u.hostname === '127.0.0.1' || u.hostname === 'localhost') && u.port === TOSU_DEFAULT_PORT;
            if (!samePageHost && !tosuDefault) return null;
            const path = u.pathname.replace(/\/+$/, '') || '/';
            return CHANNELS[path] || null;
        } catch {
            return null;
        }
    }

    class BridgeWebSocket extends EventTarget {
        constructor(url, channel) {
            super();
            this.url = new URL(String(url), location.href).href;
            this.protocol = '';
            this.extensions = '';
            this.binaryType = 'blob';
            this.bufferedAmount = 0;
            this.readyState = BridgeWebSocket.CONNECTING;
            this.onopen = null;
            this.onmessage = null;
            this.onerror = null;
            this.onclose = null;
            this._channel = channel;
            this._id = nextSocketId++;
            sockets.add(this);
            counters.created++;

            // Comme un vrai WebSocket : "open" est toujours asynchrone.
            setTimeout(() => {
                if (this.readyState !== BridgeWebSocket.CONNECTING) return;
                this.readyState = BridgeWebSocket.OPEN;
                counters.opened++;
                this._emit(new Event('open'));
                if (latest[this._channel] !== undefined) this._deliver(latest[this._channel]);
            }, 0);
        }

        _emit(event) {
            const handler = this['on' + event.type];
            if (typeof handler === 'function') {
                try { handler.call(this, event); } catch (e) { reportError(e); }
            }
            this.dispatchEvent(event);
        }

        _deliver(data) {
            if (this.readyState !== BridgeWebSocket.OPEN) return;
            counters.delivered++;
            this._emit(new MessageEvent('message', { data, origin: new URL(this.url).origin }));
        }

        _finishClose(code, reason, wasClean) {
            if (this.readyState === BridgeWebSocket.CLOSED) return;
            this.readyState = BridgeWebSocket.CLOSED;
            sockets.delete(this);
            counters.closed++;
            this._emit(new CloseEvent('close', { code, reason, wasClean }));
        }

        send(data) {
            // API navigateur : exception seulement pendant CONNECTING ; après
            // fermeture, les données sont ignorées silencieusement.
            if (this.readyState === BridgeWebSocket.CONNECTING) {
                throw new DOMException("Failed to execute 'send' on 'WebSocket': Still in CONNECTING state.", 'InvalidStateError');
            }
            if (this.readyState !== BridgeWebSocket.OPEN) return;
            // Ex. "applyFilters:[...]" (socket.js tosu) ou commandes : transmis à l'hôte.
            counters.sent++;
            webview.postMessage({ type: 'bridge.send', socketId: this._id, channel: this._channel, url: this.url, data: String(data) });
        }

        close(code, reason = '') {
            if (code !== undefined && code !== 1000 && (code < 3000 || code > 4999)) {
                throw new DOMException(`Failed to execute 'close' on 'WebSocket': The code must be either 1000, or between 3000 and 4999. ${code} is neither.`, 'InvalidAccessError');
            }
            if (this.readyState >= BridgeWebSocket.CLOSING) return;
            const wasConnecting = this.readyState === BridgeWebSocket.CONNECTING;
            this.readyState = BridgeWebSocket.CLOSING;
            // Comme un vrai WebSocket : CLOSING immédiat, "close" asynchrone.
            setTimeout(() => {
                if (wasConnecting) this._emit(new Event('error'));
                this._finishClose(wasConnecting ? 1006 : (code ?? 1005), wasConnecting ? '' : String(reason), !wasConnecting);
            }, 0);
        }
    }

    for (const [name, value] of [['CONNECTING', 0], ['OPEN', 1], ['CLOSING', 2], ['CLOSED', 3]]) {
        BridgeWebSocket[name] = value;
        BridgeWebSocket.prototype[name] = value;
    }

    function PatchedWebSocket(url, protocols) {
        const channel = channelFor(url);
        if (channel) return new BridgeWebSocket(url, channel);
        counters.native++;
        return protocols === undefined ? new NativeWebSocket(url) : new NativeWebSocket(url, protocols);
    }
    PatchedWebSocket.prototype = NativeWebSocket.prototype;
    for (const name of ['CONNECTING', 'OPEN', 'CLOSING', 'CLOSED']) PatchedWebSocket[name] = NativeWebSocket[name];
    Object.defineProperty(PatchedWebSocket, Symbol.hasInstance, {
        value: (instance) => instance instanceof BridgeWebSocket || instance instanceof NativeWebSocket,
    });
    window.WebSocket = PatchedWebSocket;

    // Les popups sont refusées par l'hôte (NewWindowRequested), mais WebView2 laisse
    // alors partir sans interception la requête émise juste après window.open()
    // (observé en V3.14-COM-02). window.open est donc neutralisé dès la page.
    Object.defineProperty(window, 'open', { value: () => null, writable: false, configurable: false });

    webview.addEventListener('message', (event) => {
        const message = event.data;
        if (!message || typeof message.type !== 'string') return;

        if (message.type === 'bridge.disconnect') {
            // Diagnostic hôte : simule une coupure serveur (code 1006, non propre).
            for (const socket of [...sockets]) socket._finishClose(1006, '', false);
            return;
        }

        if (message.type === 'bridge.reply') {
            for (const socket of sockets) {
                if (socket._id === message.socketId) socket._deliver(message.payload);
            }
            return;
        }

        if (message.type !== 'bridge.data' || typeof message.channel !== 'string') return;
        recordReception(message.channel, message.ts);
        latest[message.channel] = message.payload;
        for (const socket of sockets) {
            if (socket._channel === message.channel) socket._deliver(message.payload);
        }
    });

    Object.defineProperty(window, '__beatInsightTosuBridge', {
        value: Object.freeze({
            version: 'poc-2',
            channels: Object.values(CHANNELS),
            stats: () => {
                const open = {};
                for (const socket of sockets) open[socket._channel] = (open[socket._channel] || 0) + 1;
                return { ...counters, open, reception: receptionStats() };
            },
        }),
    });
})();
