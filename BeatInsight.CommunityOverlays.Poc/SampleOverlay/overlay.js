// Overlay d'exemple au format tosu. Adresse tosu codée en dur, comme beaucoup
// d'overlays : dans BeatInsight, le bridge l'intercepte (aucun réseau).
const TOSU = '127.0.0.1:24050';
const $ = (id) => document.getElementById(id);

const counters = { v2: 0, v1: 0, http: 'pending' };

function fmtTime(ms) {
    const s = Math.max(0, Math.floor(ms / 1000));
    return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
}

function render(data) {
    const bm = data.beatmap;
    const play = data.play;

    $('title').textContent = `${bm.artist} - ${bm.title}`;
    $('meta').textContent = `[${bm.version}] by ${bm.mapper} · ${bm.stats.stars.total.toFixed(2)}★`;
    $('state').textContent = `state ${data.state.number} · ${data.state.name}`;
    $('mods').textContent = play.mods.name || 'NM';
    $('time').textContent = `${fmtTime(bm.time.live)} / ${fmtTime(bm.time.lastObject)}`;
    $('combo').textContent = `${play.combo.current}x`;
    $('acc').textContent = `${play.accuracy.toFixed(2)}%`;
    $('pp').textContent = Math.round(play.pp.current);
    $('ppfc').textContent = Math.round(play.pp.fc);
    $('h300').textContent = play.hits['300'];
    $('h100').textContent = play.hits['100'];
    $('h50').textContent = play.hits['50'];
    $('h0').textContent = play.hits['0'];

    const span = Math.max(1, bm.time.lastObject - bm.time.firstObject);
    const pct = Math.min(1, Math.max(0, (bm.time.live - bm.time.firstObject) / span));
    $('bar').style.width = `${(pct * 100).toFixed(1)}%`;
}

function updateStatus() {
    $('bi-status').textContent = `v2 msgs ${counters.v2} · v1 msgs ${counters.v1} · /json/v2 ${counters.http}`;
}

function connect(path, onData) {
    const socket = new WebSocket(`ws://${TOSU}${path}`);
    socket.onmessage = (event) => onData(JSON.parse(event.data));
    socket.onclose = () => setTimeout(() => connect(path, onData), 1000);
    socket.onerror = () => socket.close();
}

connect('/websocket/v2', (data) => { counters.v2++; render(data); updateStatus(); });
connect('/ws', (data) => { counters.v1 = data.gameplay ? counters.v1 + 1 : counters.v1; updateStatus(); });

fetch('/json/v2')
    .then((r) => (r.ok ? r.json() : Promise.reject(r.status)))
    .then((d) => { counters.http = `ok (${d.client})`; updateStatus(); })
    .catch((e) => { counters.http = `fail ${e}`; updateStatus(); });
