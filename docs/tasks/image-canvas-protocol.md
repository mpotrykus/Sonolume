# Task: evaluate replacing the cell-grid wire protocol with a rendered-image protocol

## How we got here

While adding per-zone `Blend`/`ZIndex` (real compositing when zones overlap, see
`Compositor.ApplyBlending` in `src/Sonolume.Engine/Render/Compositor.cs`), blending precision
turned out to be capped by each zone's `CellsW`/`CellsH` grid — a zone is literally just that many
solid-colored blocks. Asked for a "Auto" grid-size default, we found there's no real signal to
derive it from (SignalRGB's Canvas API is one-way and unacknowledged; Sonolume never learns actual
device/LED counts — see the research notes buried in that conversation, not written down elsewhere).
Chasing "auto" bigger and bigger (the discussion landed on "% of a 1920x1920 grid" at one point,
i.e. hundreds of thousands of cells per zone) makes clear the discrete-cell-grid model itself is
the limitation, not the default value.

That raised the real question: should Sonolume keep shipping a hand-rolled grid-of-cells protocol
at all, or should it render the fully composited scene as an actual image and hand SignalRGB a
picture instead of a cell array? This task is to actually investigate and decide that, not to
guess from first principles.

## What exists today (don't relitigate, just know it)

- `Compositor` renders each zone into a `Rgb8[]` cell buffer (`src/Sonolume.Engine/Render/Compositor.cs`),
  including area-weighted blend-mode compositing across overlapping zones (`ApplyBlending`) — this
  part is correct and tested (`tests/Sonolume.Engine.Tests/CompositorTests.cs`), it's just
  resolution-limited by each zone's `CellsW`/`CellsH`.
- `FrameEncoder` (`src/Sonolume.Engine/Output/FrameEncoder.cs`) encodes zones into a compact delimited
  text protocol (not JSON — SignalRGB's Canvas API mangles JSON, see `docs/protocol.md`).
- `HttpCanvasSink` (`src/Sonolume.Transport/HttpCanvasSink.cs`) GETs that payload to SignalRGB's Canvas
  API (`http://localhost:16034/canvas/event`), one keep-alive connection, latest-frame-wins.
- The SignalRGB-side effect (`signalrgb-effect/Sonolume.template.html` + `region-renderer.js`) is a
  dumb renderer: it paints each zone's cells as solid rectangles onto an HTML5 `<canvas>` sized to
  `window.innerWidth`/`innerHeight`; SignalRGB samples *that* canvas onto physical LEDs via a device
  layout Sonolume has no visibility into.
- `docs/protocol.md` has real measurements from a "Phase 0" probe (`tools/canvas-probe.ps1`):
  - Today's frames are "well under 1 KB." Sustained soak tested clean at 30/60/120 requests/sec with
    ~90-byte payloads.
  - The endpoint's payload ceiling was tested up to 256 KB (all accepted with 200) — **not tested
    beyond that, and never tested at high frequency with large payloads.**
- `docs/architecture.md` explicitly records that an earlier proof-of-concept hardcoded a fixed pixel
  resolution (32x18) and that this was deliberately replaced — the project has already burned itself
  once on baking in a resolution assumption.

## The actual question to resolve

Replace (or augment) the per-zone cell-grid state message with a rendered composite image:
Sonolume renders the whole scene (all zones, already-blended, at whatever fidelity it wants — using
WPF's own drawing/rendering, or SkiaSharp, or similar) into a bitmap, encodes it (PNG likely, given
mostly-flat zone colors compress well), base64s it into the existing GET-based event payload, and the
SignalRGB-side effect just draws the image instead of manually painting cells.

This would remove the `CellsW`/`CellsH` resolution ceiling entirely (true per-pixel/vector blending,
free anti-aliasing, real blend modes if using a real graphics library) — but it is a genuine
architecture change, not a tweak. Things to actually work out, in rough order:

1. **Measure first.** Extend `tools/canvas-probe.ps1` (or a new script) to soak-test the Canvas API
   endpoint with realistic *image-sized* payloads (a few KB to 100+ KB) at 30/60/120 req/sec, the same
   way Phase 0 measured the tiny cell-protocol. This determines whether the idea is even viable before
   any engine code changes. If SignalRGB chokes on large payloads at high frequency, this whole
   direction is dead and we're back to tuning the cell-grid approach instead.
2. **Pick a rendering backend.** WPF's own `DrawingVisual` + `RenderTargetBitmap` (zero new
   dependency, already used in `PreviewControl`) vs. SkiaSharp (real blend modes like
   `SKBlendMode.Plus`/`Multiply`/`Screen` for free, likely better perf, but a new dependency and a
   currently-Windows-only app so cross-platform isn't a differentiator either way).
3. **Design the new wire message.** Something like `I1|<seq>|<w>|<h>|<base64 png>` alongside (or
   replacing) `S1`. Decide whether `L1` (Layout: zone rects/names) still gets sent for reference/UI
   purposes even once pixel delivery no longer depends on it zone-by-zone.
4. **Rewrite the effect side.** `signalrgb-effect/region-renderer.js` currently paints cells
   manually; it would need to decode base64 and `drawImage` instead. Bump the protocol version so
   old/new effects don't silently misrender (see the "dim magenta canvas on version mismatch"
   behavior already in place).
5. **Decide migration strategy.** Hard cutover vs. running both protocols side by side during
   development. Given `HasUnsavedChanges`/undo history don't touch this, a hard cutover is probably
   fine, but confirm.
6. **Re-evaluate `CellsW`/`CellsH` on `Zone`.** If images replace cells, decide whether the fields go
   away entirely, get repurposed as an "effect animation resolution" knob only (e.g. for future
   Wave/Chase spatial effects that still want a a logical sub-grid to animate across, independent of
   final pixel output), or something else. Don't decide this up front — it falls out of step 3.

## Explicitly NOT part of this task (already resolved, don't redo)

- Zone `Blend`/`ZIndex` model, UI (right-hand Params panel "Layer" group), and the compositor's
  area-weighted overlap blending logic are done, correct, and tested. Keep them — this task is about
  *replacing the transport/resolution layer underneath*, not the blending semantics, which should
  carry over conceptually (still "each zone blends over what's below it by ZIndex") regardless of
  whether the output is cells or a rendered image.
- The "Auto grid size" ask that started this thread is superseded by this task. Don't implement a
  `CellsW`/`CellsH` "Auto" heuristic — if this task lands, the field may disappear or change meaning
  entirely; if this task is rejected after the measurement step, come back and pick a modest fixed
  reference (32 or 64) for Auto instead, as a fallback.

## Suggested first message for the fresh chat

> Read `docs/tasks/image-canvas-protocol.md` and start on it. Begin with the measurement step
> (extend `tools/canvas-probe.ps1` for image-sized payloads) before writing any engine/rendering
> code — the whole task is contingent on that data.
