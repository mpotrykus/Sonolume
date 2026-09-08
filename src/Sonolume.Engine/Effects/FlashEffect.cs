using Sonolume.Engine.Core;

namespace Sonolume.Engine.Effects;

/// <summary>Instant attack to the trigger intensity, exponential decay. Reaches 5% of the peak at DecaySeconds.
/// A Gate mapping holds it at peak for as long as the key is down; decay only runs after release (or immediately,
/// for a one-shot Trigger mapping, which never releases).</summary>
public sealed class FlashEffect : IEffect
{
    public const string TypeName = "flash";

    private float level;
    private bool held;

    public string TypeId => TypeName;

    public bool IsActive => level > 0f;

    public float Level => level;

    public void Trigger(in TriggerInfo info, in ResolvedParams p)
    {
        level = Math.Clamp(info.Intensity01 * p.EffectIntensity, 0f, 1f);
        held = info.Sustain;
    }

    public void Release() => held = false;

    public void Update(float dt, in ResolvedParams p) => level = EffectEnvelope.Decay(level, dt, p.DecaySeconds, held);

    public void Render(Span<Rgb8> cells, int cellsW, int cellsH, in ResolvedParams p)
    {
        cells.Fill(p.ZoneColor.Scale(level));
    }
}
