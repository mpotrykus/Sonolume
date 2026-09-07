# Sonolume

**Turn sound into light.** Sonolume is a MIDI-driven lighting instrument for DAWs. It runs as a VST3 plugin (or standalone), maps MIDI to lighting zones and effects, and drives your RGB hardware through SignalRGB.

```
DAW  --MIDI-->  Sonolume  --regions-->  SignalRGB canvas  --device layout-->  RGB hardware
```

## Status

Vertical slice. The default project has three flash zones: C1 (note 36) kick, D1 (38) snare, F#1 (42) hi-hat. Velocity sets brightness, the flash decays over 250 ms (adjustable in the UI), and the same frame is shown in the plugin preview and on the lights.

## Requirements

- Windows 11, .NET 10 SDK (build) or .NET 10 Desktop Runtime (run)
- SignalRGB (the Canvas API is used; no Pro features required)
- A DAW that hosts VST3 (tested target: REAPER), or a MIDI controller for the standalone app

## Build and test

```powershell
dotnet build Sonolume.slnx -c Release
dotnet test tests/Sonolume.Engine.Tests
```

## Install

1. Effect: `.\tools\install-effect.ps1` copies `Sonolume.html` into your SignalRGB effects folder. Restart SignalRGB once, then select **Sonolume** in the effect library.
2. Plugin: `.\tools\install-plugin.ps1` (elevated shell) copies the build output to `C:\Program Files\Common Files\VST3\Sonolume`. Rescan plugins in the DAW and insert **Sonolume** as an instrument.
3. Standalone (no DAW): run `src/Sonolume.Standalone/bin/Release/net10.0-windows/Sonolume.Standalone.exe`, pick a MIDI input, play.

## Layout

See `docs/architecture.md` for the design and decisions, and `docs/protocol.md` for the wire format and SignalRGB measurements. The original product brief is `plan.txt`.
