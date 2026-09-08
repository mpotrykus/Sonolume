using Sonolume.Engine.Core;

namespace Sonolume.Engine.Effects;

/// <summary>A hard on/off flicker under a Flash-style decay envelope. EffectSpeed sets the flicker rate -
/// tempo-locked when the host reports a tempo, otherwise free-running. A Gate mapping keeps it flickering at
/// full level for as long as the key is down; decay only runs after release.</summary>
public sealed class StrobeEffect : IEffect
{
    public const string TypeName = "strobe";

    private float level;
    private float phase;
    private bool held;

    public string TypeId => TypeName;

    public bool IsActive => level > 0f;

    public void Trigger(in TriggerInfo info, in ResolvedParams p)
    {
        level = Math.Clamp(info.Intensity01 * p.EffectIntensity, 0f, 1f);
        held = info.Sustain;
    }

    public void Release() => held = false;

    public void Update(float dt, in ResolvedParams p)
    {
        if (level <= 0f) return;
        phase = EffectClock.Advance(phase, dt, p);
        level = EffectEnvelope.Decay(level, dt, p.DecaySeconds, held);
    }

    public void Render(Span<Rgb8> cells, int cellsW, int cellsH, in ResolvedParams p)
    {
        Rgb8 color = phase < 0.5f ? p.ZoneColor.Scale(level) : Rgb8.Black;
        cells.Fill(color);
    }
}
