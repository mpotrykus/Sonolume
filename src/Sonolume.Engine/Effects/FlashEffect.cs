using Sonolume.Engine.Core;

namespace Sonolume.Engine.Effects;

/// <summary>Instant attack to the trigger intensity, exponential decay. Reaches 5% of the peak at DecaySeconds.</summary>
public sealed class FlashEffect : IEffect
{
    public const string TypeName = "flash";

    // ln(20): the fraction remaining after one DecaySeconds is 1/20.
    private const float DecayConstant = 2.9957323f;
    private const float Floor = 1f / 512f;

    private float level;

    public string TypeId => TypeName;

    public bool IsActive => level > 0f;

    public float Level => level;

    public void Trigger(in TriggerInfo info, in ResolvedParams p)
    {
        level = Math.Clamp(info.Intensity01 * p.EffectIntensity, 0f, 1f);
    }

    public void Release()
    {
    }

    public void Update(float dt, in ResolvedParams p)
    {
        if (level <= 0f || dt <= 0f) return;
        float decay = MathF.Max(0.005f, p.DecaySeconds);
        level *= MathF.Exp(-dt * DecayConstant / decay);
        if (level < Floor) level = 0f;
    }

    public void Render(Span<Rgb8> cells, int cellsW, int cellsH, in ResolvedParams p)
    {
        cells.Fill(p.ZoneColor.Scale(level));
    }
}
