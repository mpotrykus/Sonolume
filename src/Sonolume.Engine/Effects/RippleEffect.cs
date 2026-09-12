using Sonolume.Engine.Core;

namespace Sonolume.Engine.Effects;

/// <summary>A ring that expands outward from (PosX, PosY) across the zone's grid, fading as it decays. EffectSpeed
/// picks the note value (<see cref="NoteDuration"/>) the ring takes to reach the grid's edge, tempo-locked when the
/// host reports one. A Gate mapping holds the level at peak (the ring keeps expanding) for as long as the key is
/// down; decay only runs after release. Retriggering while one is still active layers a new, independent ring
/// instead of resetting it.</summary>
public sealed class RippleEffect : IEffect
{
    public const string TypeName = "ripple";

    private const float RingWidth = 0.25f;

    private float level;
    private float age;
    private float originX;
    private float originY;
    private bool held;

    public string TypeId => TypeName;

    public bool IsActive => level > 0f;

    public bool AllowsOverlap => true;

    public void Trigger(in TriggerInfo info, in ResolvedParams p)
    {
        level = Math.Clamp(info.Intensity01 * p.EffectIntensity, 0f, 1f);
        age = 0f;
        originX = p.PosX;
        originY = p.PosY;
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
        float radius = age / NoteDuration.Seconds(p);

        for (int cy = 0; cy < cellsH; cy++)
        {
            for (int cx = 0; cx < cellsW; cx++)
            {
                float nx = cellsW <= 1 ? 0.5f : (float)cx / (cellsW - 1);
                float ny = cellsH <= 1 ? 0.5f : (float)cy / (cellsH - 1);
                float dist = MathF.Sqrt(MathF.Pow(nx - originX, 2) + MathF.Pow(ny - originY, 2));
                float ring = MathF.Max(0f, 1f - MathF.Abs(dist - radius) / RingWidth);
                cells[cy * cellsW + cx] = p.ZoneColor.Scale(level * ring);
            }
        }
    }
}
