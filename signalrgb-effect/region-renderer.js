/*
 * Sonolume region renderer. Shared by the SignalRGB effect (Sonolume.html) and, later, the plugin's web UI.
 * Protocol v1 (see docs/protocol.md):
 *   L1|inst|projectName|zoneIndex:id:name:x:y:w:h:cw:ch;...   (no JSON: SignalRGB mangles braces/quotes)
 *   S1|inst|seq|zoneIndex:WWHHrrggbb...;zoneIndex:...
 *   C1|inst
 * The renderer holds no effect logic: it paints what the engine sends and optionally interpolates between frames.
 */
(function (global) {
  'use strict';

  var PROTOCOL_VERSION = 1;

  function createRenderer() {
    var state = {
      inst: null,
      name: '',
      zones: [],
      lastSeq: 0,
      lastFrameAt: 0,
      lastLayoutAt: 0,
      frameInterval: 33,
      framesReceived: 0,
      layoutsReceived: 0,
      ignored: 0,
      mismatch: false,
      lastPayloadLength: 0,
      lastRaw: '',
      lastDecoded: '',
      unparsed: 0,
      errors: 0,
      lastError: ''
    };

    function now() { return (typeof performance !== 'undefined' ? performance.now() : Date.now()); }

    function hexByte(s, i) { return parseInt(s.substr(i, 2), 16); }

    function makeZone(z) {
      var cw = Math.max(1, z.cw | 0), ch = Math.max(1, z.ch | 0);
      var n = cw * ch * 3;
      return {
        index: z.i | 0, id: String(z.id), name: String(z.name || z.id),
        x: +z.x, y: +z.y, w: +z.w, h: +z.h, cw: cw, ch: ch,
        cells: new Uint8Array(n), prev: new Uint8Array(n), stampedAt: 0
      };
    }

    // L payload after the header: inst|name|index:id:name:x:y:w:h:cw:ch;...
    function applyLayout(rest) {
      var p1 = rest.indexOf('|');
      var p2 = rest.indexOf('|', p1 + 1);
      if (p1 < 0 || p2 < 0) throw new Error('layout header');
      var obj = { inst: rest.slice(0, p1), name: rest.slice(p1 + 1, p2), zones: [] };
      var items = rest.slice(p2 + 1).split(';');
      for (var k = 0; k < items.length; k++) {
        if (!items[k]) continue;
        var f = items[k].split(':');
        if (f.length < 9) throw new Error('layout zone fields');
        obj.zones.push({ i: parseInt(f[0], 10), id: f[1], name: f[2], x: parseFloat(f[3]), y: parseFloat(f[4]),
                         w: parseFloat(f[5]), h: parseFloat(f[6]), cw: parseInt(f[7], 10), ch: parseInt(f[8], 10) });
      }
      var previous = {};
      for (var i = 0; i < state.zones.length; i++) if (state.zones[i]) previous[state.zones[i].id] = state.zones[i];
      var zones = [];
      for (var j = 0; j < obj.zones.length; j++) {
        var z = makeZone(obj.zones[j]);
        var old = previous[z.id];
        if (old && old.cells.length === z.cells.length) {
          z.cells.set(old.cells);
          z.prev.set(old.prev);
          z.stampedAt = old.stampedAt;
        }
        zones[z.index] = z;
      }
      state.zones = zones;
      state.inst = obj.inst || null;
      state.name = obj.name || '';
      state.layoutsReceived++;
      state.lastLayoutAt = now();
    }

    function applyState(rest) {
      var p1 = rest.indexOf('|');
      var p2 = rest.indexOf('|', p1 + 1);
      if (p1 < 0 || p2 < 0) return;
      var inst = rest.slice(0, p1);
      var seq = parseInt(rest.slice(p1 + 1, p2), 10);
      if (state.inst !== null && inst !== state.inst) { state.ignored++; return; }
      if (state.inst === null) return; // no layout yet for this instance
      if (seq <= state.lastSeq && state.lastSeq - seq < 1000) { state.ignored++; return; }
      state.lastSeq = seq;

      var t = now();
      if (state.lastFrameAt > 0) {
        var gap = t - state.lastFrameAt;
        if (gap > 0 && gap < 250) state.frameInterval = state.frameInterval * 0.8 + gap * 0.2;
      }
      state.lastFrameAt = t;
      state.framesReceived++;

      var regions = rest.slice(p2 + 1).split(';');
      for (var r = 0; r < regions.length; r++) {
        var reg = regions[r];
        var colon = reg.indexOf(':');
        if (colon < 0) continue;
        var idx = parseInt(reg.slice(0, colon), 10);
        var zone = state.zones[idx];
        if (!zone) continue;
        var hex = reg.slice(colon + 1);
        var cw = hexByte(hex, 0), ch = hexByte(hex, 2);
        var count = cw * ch;
        if (hex.length < 4 + count * 6) continue;
        if (cw !== zone.cw || ch !== zone.ch) {
          zone.cw = cw; zone.ch = ch;
          zone.cells = new Uint8Array(count * 3);
          zone.prev = new Uint8Array(count * 3);
        }
        zone.prev.set(zone.cells);
        for (var c = 0; c < count; c++) {
          var o = 4 + c * 6;
          zone.cells[c * 3] = hexByte(hex, o);
          zone.cells[c * 3 + 1] = hexByte(hex, o + 2);
          zone.cells[c * 3 + 2] = hexByte(hex, o + 4);
        }
        zone.stampedAt = t;
      }
    }

    function clear(rest) {
      if (state.inst !== null && rest !== state.inst) return;
      for (var i = 0; i < state.zones.length; i++) {
        var z = state.zones[i];
        if (!z) continue;
        z.prev.set(z.cells);
        z.cells.fill(0);
        z.stampedAt = now();
      }
      state.lastFrameAt = 0;
    }

    function decodeLoose(text) {
      // SignalRGB may hand the event over still percent-encoded (the PoC decoded it) or already decoded.
      // Decode while it still looks encoded, at most twice.
      var out = String(text);
      for (var i = 0; i < 2 && /%[0-9A-Fa-f]{2}/.test(out); i++) {
        try { out = decodeURIComponent(out); } catch (e) { break; }
      }
      return out;
    }

    function handleEvent(payload) {
      if (typeof payload !== 'string' || payload.length < 2) return;
      state.lastRaw = payload;
      state.lastPayloadLength = payload.length;
      var text = decodeLoose(payload);
      state.lastDecoded = text;

      // Tolerate wrappers/prefixes: find the first "<kind><version>|" header anywhere in the string.
      var m = /([LSC])(\d+)\|/.exec(text);
      if (!m) { state.unparsed++; return; }
      var kind = m[1];
      var version = parseInt(m[2], 10);
      var rest = text.slice(m.index + m[0].length);
      if (version !== PROTOCOL_VERSION) { state.mismatch = true; return; }
      state.mismatch = false;
      try {
        if (kind === 'L') applyLayout(rest);
        else if (kind === 'S') applyState(rest);
        else if (kind === 'C') clear(rest);
      } catch (e) { state.errors++; state.lastError = String(e && e.message || e); }
    }

    function parseColor(text, fallback) {
      if (typeof text === 'string' && /^#?[0-9a-fA-F]{6}$/.test(text)) {
        var h = text.charAt(0) === '#' ? text.slice(1) : text;
        return [hexByte(h, 0), hexByte(h, 2), hexByte(h, 4)];
      }
      return fallback;
    }

    /*
     * opts: { interpolate: bool, timeoutMs: number, idleColor: '#rrggbb', showOutlines: bool }
     * Returns true while something is lit (so callers can skip work when idle).
     */
    function render(ctx, width, height, opts) {
      opts = opts || {};
      var t = now();
      var idle = parseColor(opts.idleColor, [0, 0, 0]);
      var timeoutMs = opts.timeoutMs > 0 ? opts.timeoutMs : 2000;
      var active = state.lastFrameAt > 0 && (t - state.lastFrameAt) < timeoutMs;
      var fade = 1;
      if (!active && state.lastFrameAt > 0) {
        fade = Math.max(0, 1 - ((t - state.lastFrameAt) - timeoutMs) / 500);
      }
      if (state.lastFrameAt === 0) fade = 0;

      ctx.fillStyle = 'rgb(' + idle[0] + ',' + idle[1] + ',' + idle[2] + ')';
      ctx.fillRect(0, 0, width, height);

      if (state.mismatch) {
        ctx.fillStyle = 'rgb(40,0,40)';
        ctx.fillRect(0, 0, width, height);
        return false;
      }

      var lit = false;
      var lerpMs = Math.min(Math.max(state.frameInterval, 8), 50);
      for (var i = 0; i < state.zones.length; i++) {
        var z = state.zones[i];
        if (!z) continue;
        var zx = z.x * width, zy = z.y * height, zw = z.w * width, zh = z.h * height;
        var cellW = zw / z.cw, cellH = zh / z.ch;
        var k = 1;
        if (opts.interpolate && z.stampedAt > 0) {
          k = Math.min(1, (t - z.stampedAt) / lerpMs);
        }
        for (var cy = 0; cy < z.ch; cy++) {
          for (var cx = 0; cx < z.cw; cx++) {
            var o = (cy * z.cw + cx) * 3;
            var r = z.cells[o], g = z.cells[o + 1], b = z.cells[o + 2];
            if (k < 1) {
              r = z.prev[o] + (r - z.prev[o]) * k;
              g = z.prev[o + 1] + (g - z.prev[o + 1]) * k;
              b = z.prev[o + 2] + (b - z.prev[o + 2]) * k;
            }
            r *= fade; g *= fade; b *= fade;
            if (r < 1 && g < 1 && b < 1) continue;
            lit = true;
            ctx.fillStyle = 'rgb(' + (r | 0) + ',' + (g | 0) + ',' + (b | 0) + ')';
            ctx.fillRect(zx + cx * cellW, zy + cy * cellH, cellW + 0.5, cellH + 0.5);
          }
        }
        if (opts.showOutlines) {
          ctx.strokeStyle = 'rgba(255,255,255,0.25)';
          ctx.lineWidth = 1;
          ctx.strokeRect(zx + 0.5, zy + 0.5, zw - 1, zh - 1);
        }
      }
      return lit;
    }

    function debugText() {
      var age = state.lastFrameAt > 0 ? Math.round(now() - state.lastFrameAt) : -1;
      return 'Sonolume renderer v' + PROTOCOL_VERSION +
        '\ninstance: ' + (state.inst || '-') + '  project: ' + state.name +
        '\nzones: ' + state.zones.length + '  layouts: ' + state.layoutsReceived +
        '\nframes: ' + state.framesReceived + '  seq: ' + state.lastSeq + '  ignored: ' + state.ignored +
        '\nlast frame: ' + (age < 0 ? 'never' : age + ' ms ago') + '  interval ~' + Math.round(state.frameInterval) + ' ms' +
        '\nlast payload: ' + state.lastPayloadLength + ' chars  unparsed: ' + state.unparsed + '  errors: ' + state.errors +
        '\nraw:     ' + state.lastRaw.slice(0, 90) +
        '\ndecoded: ' + state.lastDecoded.slice(0, 90) +
        (state.lastError ? '\nlast error: ' + state.lastError : '') +
        (state.mismatch ? '\nPROTOCOL VERSION MISMATCH: update Sonolume.html' : '');
    }

    return { handleEvent: handleEvent, render: render, debugText: debugText, state: state };
  }

  global.SonolumeRenderer = { create: createRenderer, PROTOCOL_VERSION: PROTOCOL_VERSION };
})(typeof window !== 'undefined' ? window : globalThis);
