// Self-test du shim WebSocket et du confinement, exécuté dans le vrai WebView2.
// Lancement : BeatInsight.CommunityOverlays.Poc.exe --overlay <bin>\SelfTest
//   --smoke <dossier> --samples 9000 --disconnect-at 0 --probe "window.__selfTest"
(async () => {
    const results = [];
    const violations = [];
    document.addEventListener('securitypolicyviolation', (e) => violations.push(`${e.effectiveDirective} ${e.blockedURI}`));

    const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
    const withTimeout = (promise, ms, label) =>
        Promise.race([promise, sleep(ms).then(() => { throw new Error(`timeout: ${label}`); })]);
    const host = location.host;
    const stats = () => window.__beatInsightTosuBridge.stats();

    async function test(name, fn) {
        try {
            const detail = await fn();
            results.push({ name, ok: true, detail: detail ?? '' });
        } catch (e) {
            results.push({ name, ok: false, detail: String(e && e.message || e) });
        }
    }
    function assert(condition, message) {
        if (!condition) throw new Error(message);
    }
    function openSocket(url) {
        const ws = new WebSocket(url);
        return withTimeout(new Promise((resolve, reject) => {
            ws.onopen = () => resolve(ws);
            ws.onerror = () => reject(new Error('error event'));
        }), 2000, `open ${url}`);
    }
    function nextMessage(ws, ms = 2000) {
        return withTimeout(new Promise((resolve) => {
            ws.addEventListener('message', (e) => resolve(e), { once: true });
        }), ms, 'message');
    }

    // ---- Chargement ----
    await test('shim et COUNTER_PATH présents avant le premier script', () => {
        const early = window.__earlyCheck;
        assert(early.bridge, 'bridge absent');
        assert(early.webSocketPatched, 'WebSocket non remplacé');
        assert(early.counterPath === 'SelfTest', `COUNTER_PATH=${early.counterPath}`);
    });

    // ---- Cycle de vie ----
    await test('readyState CONNECTING → OPEN → CLOSING → CLOSED, événements asynchrones', async () => {
        const ws = new WebSocket(`ws://${host}/websocket/v2`);
        assert(ws.readyState === WebSocket.CONNECTING, `initial ${ws.readyState}`);
        let syncOpen = true;
        const opened = new Promise((r) => { ws.onopen = () => r(syncOpen); });
        syncOpen = false;
        assert((await withTimeout(opened, 2000, 'open')) === false, 'open synchrone');
        assert(ws.readyState === WebSocket.OPEN, `après open ${ws.readyState}`);
        const msg = await nextMessage(ws);
        assert(typeof msg.data === 'string' && JSON.parse(msg.data).state, 'message v2 invalide');
        let closeSeen = false;
        const closed = new Promise((r) => ws.addEventListener('close', (e) => { closeSeen = true; r(e); }));
        ws.close();
        assert(ws.readyState === WebSocket.CLOSING, `après close() ${ws.readyState}`);
        assert(!closeSeen, 'close synchrone');
        const e = await withTimeout(closed, 1000, 'close');
        // close() sans code : le navigateur signale 1005 (aucun code de statut).
        assert(ws.readyState === WebSocket.CLOSED && e.wasClean && e.code === 1005, `close ${ws.readyState} ${e.code} ${e.wasClean}`);
    });

    await test('addEventListener / removeEventListener', async () => {
        const ws = await openSocket(`ws://${host}/websocket/v2`);
        let removedCalls = 0;
        const removed = () => removedCalls++;
        ws.addEventListener('message', removed);
        ws.removeEventListener('message', removed);
        await nextMessage(ws);
        ws.close();
        assert(removedCalls === 0, 'listener retiré appelé');
    });

    await test('send() : exception en CONNECTING, ignoré après fermeture', async () => {
        const ws = new WebSocket(`ws://${host}/websocket/v2`);
        let threw = '';
        try { ws.send('x'); } catch (e) { threw = e.name; }
        assert(threw === 'InvalidStateError', `CONNECTING: ${threw || 'pas d\'exception'}`);
        await new Promise((r) => { ws.onopen = r; });
        ws.close();
        await new Promise((r) => { ws.onclose = r; });
        ws.send('après fermeture'); // ne doit pas lever
    });

    await test('close(code) invalide lève InvalidAccessError', async () => {
        const ws = await openSocket(`ws://${host}/websocket/v2`);
        let threw = '';
        try { ws.close(1001); } catch (e) { threw = e.name; }
        ws.close();
        assert(threw === 'InvalidAccessError', threw || 'pas d\'exception');
    });

    await test('instanceof WebSocket et constantes', async () => {
        const ws = await openSocket(`ws://${host}/websocket/v2`);
        const ok = ws instanceof WebSocket && WebSocket.OPEN === 1 && ws.OPEN === 1 && ws.protocol === '' && ws.binaryType === 'blob';
        ws.close();
        assert(ok, 'API incomplète');
    });

    await test('plusieurs sockets simultanées reçoivent chacune les données', async () => {
        const [a, b] = await Promise.all([openSocket(`ws://${host}/websocket/v2`), openSocket(`ws://${host}/ws`)]);
        const [ma, mb] = await Promise.all([nextMessage(a), nextMessage(b)]);
        a.close(); b.close();
        assert(JSON.parse(ma.data).state && JSON.parse(mb.data).menu, 'payloads v2/v1 attendus');
    });

    // ---- Interception limitée aux destinations tosu ----
    await test('ws://127.0.0.1:24050/websocket/v2/precise est intercepté', async () => {
        const before = stats().created;
        const ws = await openSocket('ws://127.0.0.1:24050/websocket/v2/precise');
        const msg = JSON.parse((await nextMessage(ws)).data);
        ws.close();
        assert(stats().created === before + 1 && msg.keys && msg.keys.k1, 'non intercepté ou forme invalide');
    });

    for (const url of ['ws://127.0.0.1:9999/ws', 'ws://localhost:24051/websocket/v2', 'ws://example.com/websocket/v2', `ws://${host}/autre`]) {
        await test(`${url} n'est pas intercepté et ne se connecte pas`, async () => {
            const before = stats().native;
            let outcome = '';
            try {
                const ws = new WebSocket(url);
                outcome = await withTimeout(new Promise((r) => {
                    ws.onopen = () => r('open');
                    ws.onerror = () => r('error');
                }), 3000, url);
            } catch (e) {
                outcome = `throw ${e.name}`;
            }
            assert(stats().native === before + 1, 'aurait dû rester un WebSocket natif');
            assert(outcome !== 'open', 'connexion ouverte');
            return outcome;
        });
    }

    // ---- Commandes ----
    await test('getSettings : réponse uniquement à la socket émettrice', async () => {
        const a = await openSocket(`ws://${host}/websocket/commands?l=${encodeURI(window.COUNTER_PATH)}`);
        const b = await openSocket(`ws://${host}/websocket/commands?l=${encodeURI(window.COUNTER_PATH)}`);
        let bReceived = 0;
        b.onmessage = () => bReceived++;
        const reply = nextMessage(a);
        a.send(`getSettings:${encodeURI(window.COUNTER_PATH)}`);
        const data = JSON.parse((await reply).data);
        await sleep(300);
        a.close(); b.close();
        assert(data.command === 'getSettings', 'commande');
        assert(data.message.answer === 42 && data.message.enabled === true && !('section' in data.message), JSON.stringify(data.message));
        assert(bReceived === 0, 'réponse diffusée à une autre socket');
    });

    await test('getSettings pour un autre overlay : Wrong overlay', async () => {
        const a = await openSocket(`ws://${host}/websocket/commands?l=${encodeURI(window.COUNTER_PATH)}`);
        const reply = nextMessage(a);
        a.send('getSettings:Another%20overlay');
        const data = JSON.parse((await reply).data);
        a.close();
        assert(data.message && data.message.error === 'Wrong overlay', JSON.stringify(data));
    });

    // ---- Confinement HTTP ----
    await test('fetch /json/v2 (même origine) fonctionne', async () => {
        const r = await fetch('/json/v2');
        assert(r.ok && (await r.json()).state, `status ${r.status}`);
    });

    for (const path of ['/%2e%2e/%2e%2e/Windows/win.ini', '/..%5c..%5cWindows%5cwin.ini', '/C:/Windows/win.ini', '/index.html::$DATA', '/NUL']) {
        await test(`fetch ${path} refusé`, async () => {
            const r = await fetch(path);
            assert(!r.ok, `status ${r.status}`);
            return r.status;
        });
    }

    await test('fetch vers Internet bloqué', async () => {
        try {
            await fetch('https://example.com/');
        } catch (e) {
            return e.name;
        }
        throw new Error('requête externe réussie');
    });

    await test('XHR vers un autre port local bloqué', async () => {
        const status = await new Promise((r) => {
            const xhr = new XMLHttpRequest();
            xhr.open('GET', 'http://127.0.0.1:24050/json/v2');
            xhr.onload = () => r(`load ${xhr.status}`);
            xhr.onerror = () => r('error');
            xhr.send();
        });
        assert(status === 'error', status);
    });

    await test('image externe bloquée, image data: autorisée', async () => {
        const load = (src) => new Promise((r) => {
            const img = new Image();
            img.onload = () => r('load');
            img.onerror = () => r('error');
            img.src = src;
        });
        const external = await load('https://example.com/a.png');
        const inline = await load('data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7');
        assert(external === 'error' && inline === 'load', `externe=${external} data=${inline}`);
    });

    await test('iframe externe bloquée', async () => {
        const frame = document.createElement('iframe');
        frame.src = 'https://example.com/';
        document.body.appendChild(frame);
        await sleep(1500);
        let sameOriginBlank = false;
        try { sameOriginBlank = frame.contentWindow.location.href === 'about:blank'; } catch { sameOriginBlank = false; }
        frame.remove();
        const blockedByCsp = violations.some((v) => v.startsWith('frame-src'));
        assert(blockedByCsp || sameOriginBlank, `violations=${violations.join(',')}`);
    });

    await test('window.open refusé, requête suivante toujours interceptée', async () => {
        assert(window.open('https://example.com/') === null, 'popup ouverte');
        // Régression COM-02 : avant neutralisation, cette requête contournait le bridge (ERR_NAME_NOT_RESOLVED).
        const r = await fetch('/json/v2?after-open');
        assert(r.status === 200, `status ${r.status}`);
    });

    await test('géolocalisation refusée', async () => {
        const code = await withTimeout(new Promise((r) => navigator.geolocation.getCurrentPosition(
            () => r('granted'), (e) => r(e.code))), 3000, 'geolocation');
        assert(code === 1, `code ${code}`); // PERMISSION_DENIED
    });

    // ---- /files/beatmap/* (V3.14-COM-02) ----
    // Sans --beatmap-assets-root, le fournisseur est désactivé : tout doit répondre 404.
    // Avec la racine TestAssets/Songs, la beatmap courante du mock (cycle 0) est
    // "900001 BeatInsight - Community Overlay POC".
    const probe = await fetch('/files/beatmap/background');
    const assetsEnabled = probe.status !== 404;
    const folderA = '900001 BeatInsight - Community Overlay POC';
    const enc = (path) => path.split('/').map(encodeURIComponent).join('/');
    const bytesOf = async (response) => new Uint8Array(await response.arrayBuffer());

    if (!assetsEnabled) {
        for (const type of ['background', 'audio', 'file']) {
            await test(`fournisseur désactivé : /files/beatmap/${type} → 404`, async () => {
                const r = await fetch(`/files/beatmap/${type}`);
                assert(r.status === 404, `status ${r.status}`);
            });
        }
    } else {
        await test('/files/beatmap/background : PNG de la beatmap courante', async () => {
            const r = await fetch('/files/beatmap/background');
            const body = await bytesOf(r);
            assert(r.status === 200 && r.headers.get('content-type') === 'image/png', `${r.status} ${r.headers.get('content-type')}`);
            assert(body.length === Number(r.headers.get('content-length')) && body[0] === 0x89 && body[1] === 0x50, `taille ${body.length}`);
            return `${body.length} octets`;
        });

        await test('query strings ignorées (?v=, ?checksum=) comme tosu', async () => {
            const sizes = [];
            for (const q of ['', '?v=00000000000000000000000000000000', '?checksum=abc&x=1']) {
                const r = await fetch(`/files/beatmap/background${q}`);
                sizes.push(`${r.status}:${(await bytesOf(r)).length}`);
            }
            assert(new Set(sizes).size === 1 && sizes[0].startsWith('200:'), sizes.join(' '));
        });

        await test('100 requêtes répétées identiques', async () => {
            const started = performance.now();
            const lengths = new Set();
            for (let i = 0; i < 100; i++) {
                const r = await fetch(`/files/beatmap/background?v=${i}`);
                assert(r.status === 200, `requête ${i} : ${r.status}`);
                lengths.add((await bytesOf(r)).length);
            }
            assert(lengths.size === 1, [...lengths].join(','));
            return `${Math.round(performance.now() - started)} ms pour 100 requêtes`;
        });

        await test('/files/beatmap/audio : audio/wav, Accept-Ranges', async () => {
            const r = await fetch('/files/beatmap/audio');
            const body = await bytesOf(r);
            assert(r.status === 200 && r.headers.get('content-type') === 'audio/wav' && r.headers.get('accept-ranges') === 'bytes', `${r.status} ${r.headers.get('content-type')}`);
            assert(String.fromCharCode(...body.slice(0, 4)) === 'RIFF', 'en-tête RIFF absent');
            return `${body.length} octets`;
        });

        await test('Range bytes=0-11 → 206 + Content-Range', async () => {
            const full = Number((await fetch('/files/beatmap/audio', { method: 'GET' })).headers.get('content-length'));
            const r = await fetch('/files/beatmap/audio', { headers: { Range: 'bytes=0-11' } });
            const body = await bytesOf(r);
            assert(r.status === 206 && body.length === 12, `${r.status} ${body.length}`);
            assert(r.headers.get('content-range') === `bytes 0-11/${full}`, r.headers.get('content-range'));
        });

        await test('Range hors fichier → 416', async () => {
            const r = await fetch('/files/beatmap/audio', { headers: { Range: 'bytes=99999999-' } });
            assert(r.status === 416, `status ${r.status}`);
        });

        await test('/files/beatmap/file : .osu en text/plain', async () => {
            const r = await fetch('/files/beatmap/file');
            const text = await r.text();
            assert(r.status === 200 && r.headers.get('content-type').startsWith('text/plain') && text.startsWith('osu file format v14'), `${r.status} ${text.slice(0, 20)}`);
        });

        await test('chemin générique depuis directPath (/json/v2), séparateur "\\" de tosu', async () => {
            const state = await (await fetch('/json/v2')).json();
            const raw = state.directPath.beatmapBackground;
            assert(raw === `${folderA}\\bg.png`, `directPath=${raw}`);
            const asIs = await fetch(`/files/beatmap/${raw}`);
            const encoded = await fetch(`/files/beatmap/${enc(raw.replace('\\', '/'))}`);
            assert(asIs.status === 200 && encoded.status === 200, `${asIs.status} ${encoded.status}`);
        });

        await test('URL absolue http://host/files/beatmap/background (style OBSDecoratePack)', async () => {
            const r = await fetch(`http://${location.host}/files/beatmap/background?v=1`);
            assert(r.status === 200, `status ${r.status}`);
        });

        await test('<img> du background : décodé (384×216)', async () => {
            const img = new Image();
            await withTimeout(new Promise((resolve, reject) => { img.onload = resolve; img.onerror = () => reject(new Error('onerror')); img.src = '/files/beatmap/background'; }), 3000, 'img');
            assert(img.naturalWidth === 384 && img.naturalHeight === 216, `${img.naturalWidth}×${img.naturalHeight}`);
        });

        const refused = [
            ['autre beatmap (non courante)', `/files/beatmap/${enc('900002 BeatInsight - Second Mock Map/bg2.png')}`, [403]],
            ['extension non autorisée', `/files/beatmap/${enc(folderA + '/notes.txt')}`, [403]],
            ['fichier absent', `/files/beatmap/${enc(folderA + '/missing.png')}`, [404]],
            ['listing de dossier', `/files/beatmap/${enc(folderA)}/`, [403]],
            ['remontée encodée', `/files/beatmap/${enc(folderA)}/..%2F..%2F..%2FSelfTest%2Fsettings.json`, [403, 404]],
            ['remontée %5c', `/files/beatmap/${enc(folderA)}/..%5c..%5cSelfTest%5cindex.html`, [403, 404]],
            ['double encodage', `/files/beatmap/${enc(folderA)}/%252e%252e%252fbg.png`, [404]],
            ['chemin absolu', '/files/beatmap/C:%5CWindows%5Cwin.ini', [403]],
            ['flux NTFS', `/files/beatmap/${enc(folderA)}/bg.png%3A%3A%24DATA`, [403]],
            ['point final', `/files/beatmap/${enc(folderA)}/notes.txt.`, [403, 404]],
        ];
        for (const [label, url, allowed] of refused) {
            await test(`refusé : ${label}`, async () => {
                const r = await fetch(url);
                assert(allowed.includes(r.status), `status ${r.status} pour ${url}`);
                return r.status;
            });
        }
    }

    await test('navigation de la page vers Internet annulée', async () => {
        const marker = Math.random();
        window.__navMarker = marker;
        location.href = 'https://example.com/';
        await sleep(1500);
        assert(window.__navMarker === marker && location.host === host, 'page remplacée');
    });

    const failed = results.filter((r) => !r.ok);
    window.__selfTest = { done: true, passed: results.length - failed.length, failed: failed.length, results, violations };
    document.getElementById('out').textContent = JSON.stringify(window.__selfTest, null, 2);
})();
