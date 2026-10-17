// BeatInsight — limiteur requestAnimationFrame (POC V3.14-COM-05, expérimental).
// Ne limite QUE les animations pilotées par requestAnimationFrame (canvas, compteurs JS).
// Les transitions/animations CSS et le compositeur Chromium ne sont pas concernés.
// __BI_FPS_CAP__ est remplacé par l'hôte (images par seconde visées).
(() => {
    'use strict';
    const cap = __BI_FPS_CAP__;
    if (!(cap > 0) || window.__beatInsightFrameLimiter) return;

    const nativeRaf = window.requestAnimationFrame.bind(window);
    const nativeCancel = window.cancelAnimationFrame.bind(window);
    const interval = 1000 / cap;
    // Tolérance : à 239 Hz une image dure ~4,2 ms ; sans marge, 60 i/s deviendrait 48 i/s.
    const tolerance = 2;

    let queue = new Map();
    let nextId = 1;
    let pending = false;
    let last = -Infinity;
    let delivered = 0;

    function pump(timestamp) {
        const wait = interval - tolerance - (timestamp - last);
        if (wait > 0) {
            // Attendre hors boucle d'images : redemander rAF à chaque image natif
            // maintiendrait le thread principal à la fréquence de l'écran.
            setTimeout(() => nativeRaf(pump), Math.max(0, wait - tolerance));
            return;
        }
        last = timestamp;
        pending = false;
        delivered++;
        const callbacks = queue;
        queue = new Map();
        for (const callback of callbacks.values()) {
            try { callback(timestamp); } catch (e) { reportError(e); }
        }
    }

    window.requestAnimationFrame = function requestAnimationFrame(callback) {
        const id = nextId++;
        queue.set(id, callback);
        if (!pending) {
            pending = true;
            nativeRaf(pump);
        }
        return id;
    };
    window.cancelAnimationFrame = function cancelAnimationFrame(id) {
        queue.delete(id);
    };

    Object.defineProperty(window, '__beatInsightFrameLimiter', {
        value: Object.freeze({ cap, nativeRequestAnimationFrame: nativeRaf, nativeCancelAnimationFrame: nativeCancel, delivered: () => delivered }),
    });
})();
