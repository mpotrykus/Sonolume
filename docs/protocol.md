# Sonolume wire protocol and SignalRGB Canvas API notes

## Transport

- Endpoint: `http://localhost:16034/canvas/event?sender=Sonolume&event=<url-encoded payload>`
- SignalRGB dispatches every request to the **currently selected effect** as `window.onCanvasApiEvent({ sender, event })`. Other effects never see it. There is no acknowledgement and no way to query state.
- Sonolume uses GET with one keep-alive connection (`HttpCanvasSink`). Frames are latest-wins: a stalled SignalRGB drops intermediate frames instead of building a backlog.

## Messages

All messages start with a kind letter and the protocol version. The effect paints a dim magenta canvas when the version does not match its renderer.

| Message | Format | When |
|---|---|---|
| Layout | `L1|{"v":1,"inst":"<8 hex>","name":"<project>","zones":[{"i":0,"id":"kick","name":"Kick","x":0.04,"y":0.56,"w":0.28,"h":0.38,"cw":1,"ch":1}, ...]}` | On start, on project load, every 10 s |
| State | `S1|<inst>|<seq>|<zoneIndex>:<WW><HH><rrggbb>...;<zoneIndex>:...` | Whenever a zone changed (dirty only), plus a full frame every 1 s as heartbeat |
| Clear | `C1|<inst>` | On plugin/standalone shutdown |

- `inst` is a per-process-instance id. The effect follows the instance that sent the most recent layout and ignores state from others.
- `seq` increases per frame; older or duplicate sequence numbers are ignored.
- `WW`/`HH` are the cell grid of the region in hex (`0101` for a solid zone). Cells are row-major `rrggbb` hex.
- Coordinates are 0..1 fractions of the SignalRGB canvas.

Example single kick hit at full velocity: `S1|a1b2c3d4|17|0:0101FF0000`

## Effect-side behavior (`signalrgb-effect/`)

- Paints regions scaled to the canvas; optional linear interpolation between the last two frames over the observed frame interval (max 50 ms).
- Idle: after `timeout_ms` (default 2000) without a frame the canvas fades to the idle color over 500 ms. No text is drawn unless "Show Debug Text" is enabled, because text gets sampled onto the LEDs.
- `Sonolume.html` is generated from `Sonolume.template.html` + `region-renderer.js` by `tools/install-effect.ps1`. SignalRGB needs a restart to discover a new effect file.

## Phase 0 measurements (2026-09-07, SignalRGB 2.5.77, Windows 11)

Probe: `tools/canvas-probe.ps1`. Server side only; delivery into the effect is confirmed by the debug counter in `Sonolume.html`.

Verbs and validation:

| Request | Result |
|---|---|
| GET with `sender` and `event` in the query | 200 |
| POST with `sender` and `event` in the query, empty body | 200 |
| POST JSON body `{"sender","event"}` | 400 `Invalid Sender` (parameters must be in the query string) |
| GET without `event` | 400 `Invalid Event` |
| Other path under `/canvas/` | 404 |

Payload ceiling: `event` lengths of 1 KB, 8 KB, 64 KB, 128 KB and 256 KB were all accepted with 200. Sonolume frames are well under 1 KB.

Sustained rate soak (20 s each, one HttpClient, payload ~90 chars):

| Rate | Sent | Failed | HTTP p50 | HTTP p99 | Max | SignalRGB CPU (all cores) | Client sockets |
|---|---|---|---|---|---|---|---|
| 30/s | 600 | 0 | 0.58 ms | 43 ms | 55 ms | 13.9% | 1 |
| 60/s | 1200 | 0 | 0.57 ms | 42 ms | 57 ms | 13.5% | 1 |
| 120/s | 2398 | 1 (timeout) | 0.49 ms | 6 ms | 519 ms | 13.7% | 2 |

Conclusions:

- Keep-alive is honored: one socket for the whole run, no ephemeral-port churn.
- SignalRGB CPU is flat across rates (it is the app's normal render load); receiving events is effectively free.
- Typical request cost is about 0.5 ms; occasional 40 to 50 ms stalls occur at any rate (SignalRGB's own frame work blocking its HTTP server). One 519 ms stall at 120/s. The latest-wins sender absorbs these without delaying later frames.
- Decision: engine ticks at 120 Hz and sends dirty frames as they happen; no artificial rate cap needed. Interpolation in the effect stays on by default to smooth over stalls.

Still to measure with hardware: end-to-end latency from key press to LED (phone slow-motion video). Expected 30 to 60 ms, dominated by SignalRGB's canvas sampling and device push.

## Phase 1 measurements: image-sized payload viability (2026-09-08, SignalRGB 2.5.77, Windows 11)

Motivation: `docs/tasks/image-canvas-protocol.md` proposes replacing the cell-grid `S1` message with a
rendered composite image (PNG, base64'd into the same GET event payload) to remove the `CellsW`/`CellsH`
resolution ceiling. Phase 0's payload-ceiling check (256 KB accepted with 200) looked like a green light,
but that check sent a single request per size, and the payload was a repeated literal `'A'` character —
nothing like a real image's byte content. This phase re-tests with realistic payloads before any
engine/rendering code is written, per the task's own gate ("if SignalRGB chokes on large payloads at high
frequency, this whole direction is dead").

Probe: `tools/canvas-probe.ps1` (extended with an image-payload soak: `-ImageSizesKB`, `-ImageRate`,
`-ImageSecondsPerSize`). Each size soaks for 30 s at 60 req/s using a fresh random byte buffer
(base64-encoded, properly URL-escaped) **per request** — not one fixed blob resent — over a dedicated
HttpClient/connection per size.

| Size (raw bytes, pre-base64) | Sent | Failed | Fail % | Max consecutive-fail streak | p50 | p99 |
|---|---|---|---|---|---|---|
| 10 KB | 1752 | 48 | 2.7% | 2 | 2.0 ms | 3.4 ms |
| 20 KB | 1710 | 90 | 5.0% | 2 | 3.6 ms | 5.5 ms |
| 50 KB | 1575 | 225 | 12.5% | 3 | 8.3 ms | 10.9 ms |
| 90 KB | 1390 | 410 | 22.8% | 5 | 14.6 ms | 26.3 ms |
| 150 KB | 820 | 421 | 33.9% | 6 | 24.3 ms | 28.9 ms |
| 256 KB | 392 | 334 | 46.0% | 9 | 41.6 ms | 55.6 ms |
| 400 KB | 165 | 308 | 65.1% | 11 | 63.5 ms | 79.5 ms |

Every failure returned HTTP 400 `Invalid Event` (not a timeout, not a dropped connection). The cell
protocol soak in the same run stayed at 0.0% failure across 30/60/120 req/s, matching Phase 0.

Diagnosis (ruling out the obvious alternatives before trusting "size" as the cause):

- **Not raw request length by itself.** A repeated-character payload (Phase 0's original ceiling test)
  never failed even once at 150 KB or 256 KB across 20 reps each. Real, varying content at the *identical*
  byte length failed 30-60% of the time. Content, not just length, matters.
- **Not URL-escaping overhead.** Switching to base64url (`-`/`_` alphabet, no `+`/`/`/`=`, so effectively
  zero percent-encoding needed) reduced but did not fix the failures (e.g. still ~25-40% failing at
  150-256 KB, ~95% at 400 KB).
- **Not a fixed byte-length threshold either.** Holding length exactly constant (200 KB) and varying only
  the content pattern (single repeated char / 2-char alternation / 4-char cycle / full-entropy random)
  produced inconsistent results across separate runs — sometimes all four patterns passed cleanly, other
  times realistic random content failed substantially. This means the failure is not a deterministic
  function of the bytes sent; it's sensitive to something else.
- **Not a "poisoned" keep-alive connection.** Failures mostly occur as isolated single drops with
  occasional short bursts (2-11 in a row, streak length growing with size), not one failure taking down
  the rest of a run. Explicitly opening a brand-new connection immediately after every single failure
  (instead of reusing the one that just failed) did **not** prevent further consecutive failures — bursts
  up to 5 in a row still occurred with a guaranteed-fresh connection each time.

The pattern most consistent with the evidence: SignalRGB's Canvas API HTTP server has some internal,
load- or timing-sensitive limitation in parsing large/varied query strings, and larger payloads simply
spend longer in the vulnerable window per request (both because they take longer to transmit and parse,
and because more requests land while SignalRGB's own render loop is busy). This could not be pinned to a
specific root cause without access to SignalRGB's source — it is closed-source and the Canvas API gives
no diagnostic feedback beyond `Invalid Event`.

Conclusions:

- **The image-canvas-protocol idea as scoped (a single base64 PNG per frame over the existing GET-query
  Canvas API) is not viable.** Failure rate is not a fixed ceiling to design around — it climbs
  continuously with payload size, starting from a non-trivial 2.7% at just 10 KB (smaller than almost any
  real composited-scene PNG would be) and reaching 65% at 400 KB. At 60 Hz, even the best case (10 KB)
  drops roughly one frame in 40 — for a system that leans on "latest-frame-wins" to hide transport hiccups,
  a hiccup rate this high would be visibly janky, not just occasionally imperceptible like the cell
  protocol's rare multi-hundred-ms SignalRGB-side stalls.
  - This satisfies the task's own explicit gate: chasing the image-protocol design (rendering backend
    choice, wire message format, effect-side rewrite) is not worth doing until/unless this transport
    problem has a real fix, which is now a prerequisite the original task didn't anticipate.
- Two directions *might* rescue the idea, neither validated here and both out of scope for this
  measurement pass: (a) keep any image payload very small (well under 10 KB, e.g. a heavily downscaled/
  quantized composite) and accept a non-zero but hopefully-rare drop rate, or (b) find a transport path to
  SignalRGB that doesn't route the payload through a URL query string at all (unknown whether one exists;
  the Canvas API is GET/POST-with-query-params only per Phase 0, and POST-with-body was already rejected
  with `Invalid Sender`). Neither should be assumed to work without its own dedicated measurement.
- The cell-grid protocol's own headroom is untouched by any of this: 0% failures at up to 120 Hz with
  the tiny `S1` payload, same as Phase 0. Recommendation: **do not proceed to the rendering-backend or
  wire-message design steps in `docs/tasks/image-canvas-protocol.md`** on the current transport. If a
  smaller-image or alternate-transport variant is worth chasing, that needs its own follow-up measurement
  pass first, not an assumption carried over from this one.
