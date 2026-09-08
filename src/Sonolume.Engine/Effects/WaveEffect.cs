using Sonolume.Engine.Core;

namespace Sonolume.Engine.Effects;

/// <summary>A single bright band that travels once across the zone's cells (left to right) and finishes, under a
/// Flash-style decay envelope - the same one-shot shape as <see cref="RippleEffect"/>, just linear instead of
/// radial: one key press sends out one wave that runs its path rather than looping. EffectSpeed is a propagation
/// speed (strip-widths/second), not a tempo-locked rate, since the band's position is tied to real elapsed time
/// since it was triggered, not a beat grid. A Gate mapping holds the level at peak (the band keeps traveling) for
/// as long as the key is down; decay only runs after release.</summary>
public sealed class WaveEffect : IEffect
{
    public const string TypeName = "wave";

    private const float BandWidth = 0.25f;

    private float level;
    private float age;
    private bool held;

    public string TypeId => TypeName;

    public bool IsActive => level > 0f;

    public void Trigger(in TriggerInfo info, in ResolvedParams p)
    {
        level = Math.Clamp(info.Intensity01 * p.EffectIntensity, 0f, 1f);
        age = 0f;
        held = info.Sustain;
    }

    public void Release() => held = false;

    public void Update(float dt, in ResolvedParams p)
    {
        if (level <= 0f) return;
        age += dt;
        level = EffectEnvelope.Decay(level, dt, p.DecaySeconds, held);
    }

    public void Render(Span<Rgb8> cells, int cellsW, int cellsH, in ResolvedParams p)
    {
        float speed = MathF.Max(0.2f, p.EffectSpeed * 3f);
        float pos = age * speed;

        for (int cy = 0; cy < cellsH; cy++)
        {
            for (int cx = 0; cx < cellsW; cx++)
            {
                float cellPos = cellsW <= 1 ? 0f : (float)cx / (cellsW - 1);
                float band = MathF.Max(0f, 1f - MathF.Abs(cellPos - pos) / BandWidth);
                cells[cy * cellsW + cx] = p.ZoneColor.Scale(level * band);
            }
        }
    }
}
