# Sonolume architecture

Sonolume is a MIDI-driven lighting instrument: **DAW -> Sonolume -> SignalRGB canvas -> RGB hardware**. The product brief is `plan.txt`; this document records what was verified, what was decided, and why.

## Verified SignalRGB facts

- SignalRGB effects ("lightscripts") are HTML files in the user effects folder (`D:\Documents\WhirlwindFX\Effects` on the dev machine). `<meta property>` tags become JS globals for user settings. The effect draws on a 2D canvas that SignalRGB samples onto devices via the user's layout.
- External data reaches the running effect only through the Canvas API: `GET/POST http://localhost:16034/canvas/event?sender=<name>&event=<string>` -> `window.onCanvasApiEvent({sender, event})`. It is one-way, unacknowledged, and reaches only the selected effect. Details and measurements: `docs/protocol.md`.
- New effect files are discovered on SignalRGB restart. The REST API under `/api/v1` (effect selection, brightness) is a Pro feature and is not required.
- The original proof of concept streamed full 32x18 pixel frames as hex at 10 fps. Sonolume keeps its sender/event contract and replaces the payload with a zone/region protocol.

## Decisions

### Stack: C# on .NET 10, no paywalled frameworks

| Layer | Choice | License |
|---|---|---|
| Engine, persistence, transport | Plain .NET class libraries | ours |
| VST3 shell | AudioPlugSharp 0.7 (prebuilt C++/CLI bridge from NuGet) | MIT |
| Fallback shell | NPlug (NativeAOT, VST3 SDK 3.8 which is MIT) | BSD-2 |
| Standalone app | WPF + DryWetMIDI | MIT |
| SignalRGB effect | HTML + `region-renderer.js` | ours |
| Tests | xUnit | Apache-2.0 |

JUCE was rejected because of its revenue-tiered license. A .NET plugin loads the CLR into the DAW; only one .NET runtime version can live in a process, so two different .NET plugins could conflict. The engine is shell-agnostic so NPlug can replace AudioPlugSharp if that ever bites.

### Render split: the engine renders regions, the effect paints them

All MIDI interpretation, mapping, envelopes, and effects run in `Sonolume.Engine`. The engine emits a **Frame**: a list of regions, each a zone rect plus a small cell grid of RGB values (1x1 for solid zones; larger grids for spatial effects later). The SignalRGB effect is a dumb renderer and never changes when effects are added. The plugin preview shows the same Frame, so what you see is what the lights do.

Rejected: animating in the effect JS (forks effect logic into a file that needs a SignalRGB restart to update, and is untestable) and streaming pixel grids (hides the zone model, wasteful for solid zones).

### Latency first

- Host/MIDI thread: `EngineRunner.Enqueue` copies the event into a lock-free ring buffer and signals the engine thread. No allocation, no I/O.
- Engine thread (`EngineRunner`): wakes immediately on MIDI, applies it, renders, hands the frame to the sink. Between events it ticks at 120 Hz so decays animate. Frames are sent only when dirty, plus a 1 s full-frame heartbeat. Windows timer resolution is raised to 1 ms for the thread's lifetime.
- Sender thread (`HttpCanvasSink`): one keep-alive connection, latest-wins slot (never a backlog), 250 ms request timeout, 1 s back-off while SignalRGB is offline.
- Effect: optional interpolation between frames at its own display rate.
- Measured: about 0.5 ms per request, SignalRGB CPU unaffected by rate (see `docs/protocol.md`).

### Pipeline and types

```
MidiEvent (Input)           raw message, 14-bit value scale, timestamp
  -> MidiInterpreter        NoteOn -> Trigger, NoteOff -> Release, CC/pitch/aftertouch -> Set
  -> ControlEvent           SourceAddress (kind, channel, number) + type + value 0..1
  -> MappingEngine          matches mappings (wildcards on channel/number), applies Transform
  -> Compositor             Set -> zone/group ParamSet; Trigger/Gate -> effect instance on each targeted zone
  -> Effects (IEffect)      Flash today; Pulse, Wave, Chase... register in EffectRegistry
  -> Frame (Render)         dirty regions since the last take
  -> FrameEncoder (Output)  text protocol
  -> IFrameSink             HttpCanvasSink (SignalRGB) or RecordingSink (tests)
```

Future inputs (OSC, audio analysis, host automation) add `SourceKind` members and produce `ControlEvent`s. Nothing downstream changes.

### Semantics fixed here

- Mapping modes: **Set** (persistent value), **Trigger** (one-shot, release ignored), **Gate** (release ends the effect).
- Retrigger restarts the effect envelope.
- A zone targeted by any Trigger/Gate mapping (directly or via a group) is *event-driven*: idle is dark and effects light it. Other zones show their steady color.
- Group composition per tick: brightness, saturation and effect intensity multiply; hue adds as an offset; active ANDs. Groups nest via `ParentId`.
- Parameters are stored in raw units (`ParamInfos` holds range and default); mappings write normalized 0..1 values through `Transform` (input window, invert, curve, output range).
- Zone color is HSB from Hue/Saturation/Brightness params; palettes are modeled but not yet used by effects.
- Terms: **Layout** is Sonolume's zone geometry, **Frame** is a rendered state, **SignalRGB canvas** is the pixel surface SignalRGB samples.

## Repository layout

```
src/Sonolume.Engine       core types, input, model, mappings, effects, render, output, Engine, EngineRunner
src/Sonolume.Persist      ProjectJson (System.Text.Json, schema-versioned)
src/Sonolume.Transport    HttpCanvasSink
src/Sonolume.UI           SonolumeSession (composition root), SonolumeView, PreviewControl (WPF)
src/Sonolume.Plugin       SonolumePlugin (AudioPlugSharp VST3 shell); output assembly is Sonolume.dll + SonolumeBridge.vst3
src/Sonolume.Standalone   WPF app with direct MIDI input (DryWetMIDI); no DAW needed
signalrgb-effect          Sonolume.template.html + region-renderer.js -> dist/Sonolume.html
tests/Sonolume.Engine.Tests
tools                     canvas-probe.ps1 (Phase 0), install-effect.ps1, install-plugin.ps1
docs                      this file, protocol.md
```

## Operational gotchas

- Events reach only the selected SignalRGB effect. If the lights do nothing, check that "Sonolume" is the active effect. The plugin status line only knows whether SignalRGB answered HTTP 200.
- A new effect file needs a SignalRGB restart; edits to an existing file are picked up when the effect reloads.
- Multiple plugin instances each send with their own instance id; the effect follows the most recent layout sender. One instance per DAW project is the supported setup for now.
- Plugin state stores the project JSON (plus AudioPlugSharp's parameter state) in the DAW project.
- VST3 has no native CC events; hosts deliver CC through parameter emulation. CC/aftertouch mappings in the plugin depend on that path and are Phase 2 work. The standalone app receives CC, pitch bend and aftertouch directly today.

## Roadmap

- Phase 1 (this repo state): vertical slice. Notes 36/38/42 flash kick/snare/hi-hat zones, velocity scales brightness, 250 ms decay, plugin + standalone + effect + tests.
- Phase 2: CC/aftertouch/pitch Set mappings in the plugin (parameter emulation), MIDI Learn UI, group editing, presets import/export in the plugin UI, 16 host-automatable macro parameters.
- Phase 3: cell-grid effects (Pulse, Fade, Wave, Chase, Rainbow, Spectrum), palettes, retrigger policies.
- Phase 4: configuration UI (WebView2 in the WPF editor sharing `region-renderer.js`): zone editor, mapping table.
- Phase 5: multi-instance policy, installer, optional Pro REST call to select the effect, OSC input.
