using Sonolume.Engine.Core;

namespace Sonolume.Engine.Effects;

/// <summary>What started an effect: the transformed mapping value plus the raw source details for pitch- or
/// velocity-aware effects.</summary>
/// <param name="Sustain">True for a Gate mapping: the effect should hold at its triggered level - still animating,
/// but not decaying - until <see cref="IEffect.Release"/> arrives, so it lasts exactly as long as the key stays
/// down. False for a Trigger mapping (no release ever arrives): decays immediately per DecaySeconds, matching a
/// one-shot percussive hit regardless of how long the source note lasts.</param>
public readonly record struct TriggerInfo(float Intensity01, int Note, float Velocity01, long TimestampTicks, bool Sustain = false);

/// <summary>Zone parameters after group composition. Computed once per zone per tick.</summary>
/// <param name="BeatsPerSecond">Host tempo (BPM/60), or 0 when nothing is driving tempo (standalone: always
/// free-running; see <see cref="TempoSynced"/>).</param>
/// <param name="SongBeats">Elapsed beats since the host started transport, frozen while stopped. Only meaningful
/// when <see cref="TempoSynced"/>; use it (not per-effect elapsed time) so every zone's tempo-locked effect
/// stays in phase with the others regardless of when each was triggered.</param>
/// <param name="TempoSynced">True once a host has reported a tempo (the plugin does this every audio buffer);
/// false in the standalone app, which has no transport and always runs effects free-running off EffectSpeed.</param>
public readonly record struct ResolvedParams(
    float Brightness,
    float Hue,
    float Saturation,
    bool Active,
    float EffectIntensity,
    float EffectSpeed,
    float DecaySeconds,
    float PosX,
    float PosY,
    float PaletteIndex,
    float BeatsPerSecond = 0f,
    double SongBeats = 0.0,
    bool TempoSynced = false)
{
    /// <summary>The zone's steady color at full effect gain.</summary>
    public Rgb8 ZoneColor => Active ? new Hsb(Hue, Saturation, Brightness).ToRgb8() : Rgb8.Black;
}

/// <summary>
/// An animated effect instance owned by one zone. Effects consume resolved parameters every tick,
/// so a CC mapped to decay or intensity takes effect immediately.
/// </summary>
public interface IEffect
{
    string TypeId { get; }

    /// <summary>False once the effect has fully decayed; the zone then shows its idle state.</summary>
    bool IsActive { get; }

    /// <summary>True if triggering this effect type again while an instance is still active should layer a fresh,
    /// independent instance on top (additively composited by the zone runtime) instead of resetting the running
    /// one. Effects whose whole character is time-since-trigger (Pulse, Ripple) want this so repeated hits stack
    /// rather than cancel each other; most effects don't.</summary>
    bool AllowsOverlap => false;

    void Trigger(in TriggerInfo info, in ResolvedParams p);

    void Release();

    void Update(float dt, in ResolvedParams p);

    void Render(Span<Rgb8> cells, int cellsW, int cellsH, in ResolvedParams p);
}
