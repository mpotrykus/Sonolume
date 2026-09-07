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
