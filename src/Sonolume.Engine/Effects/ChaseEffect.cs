using Sonolume.Engine.Core;

namespace Sonolume.Engine.Effects;

/// <summary>A single lit cell that runs across the zone's grid (raster order) and wraps, under a Flash-style decay
/// envelope. EffectSpeed sets how fast it travels - tempo-locked when the host reports a tempo, otherwise
/// free-running. A Gate mapping keeps it running at full level for as long as the key is down; decay only runs
/// after release.</summary>
public sealed class ChaseEffect : IEffect
{
    public const string TypeName = "chase";

    private const float TailCells = 2f;

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
        int count = cellsW * cellsH;
        float head = phase * count;

        for (int i = 0; i < count; i++)
        {
            float d = MathF.Abs(i - head);
            d = MathF.Min(d, count - d); // wrap distance
            float falloff = MathF.Max(0f, 1f - d / TailCells);
            cells[i] = p.ZoneColor.Scale(level * falloff);
        }
    }
}
