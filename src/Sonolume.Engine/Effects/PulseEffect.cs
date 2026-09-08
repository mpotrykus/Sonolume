using Sonolume.Engine.Core;

namespace Sonolume.Engine.Effects;

/// <summary>A sine "breathe" in overall zone brightness under a Flash-style decay envelope. EffectSpeed sets the
/// breathing rate - tempo-locked when the host reports a tempo, otherwise free-running. A Gate mapping keeps it
/// breathing at full level for as long as the key is down; decay only runs after release. Retriggering while one
/// is still active layers a new, independently-phased instance instead of resetting it.</summary>
public sealed class PulseEffect : IEffect
{
    public const string TypeName = "pulse";

    private float level;
    private float phase;
    private bool held;

    public string TypeId => TypeName;

    public bool IsActive => level > 0f;

    public bool AllowsOverlap => true;

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
        float breathe = 0.5f + 0.5f * MathF.Sin(2f * MathF.PI * phase);
        cells.Fill(p.ZoneColor.Scale(level * breathe));
    }
}
