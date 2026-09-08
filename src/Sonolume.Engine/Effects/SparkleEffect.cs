using Sonolume.Engine.Core;

namespace Sonolume.Engine.Effects;

/// <summary>Random cells twinkle and fade independently while the overall zone decays under a Flash-style
/// envelope. EffectSpeed sets how often new sparkles spawn. A Gate mapping keeps sparkling at full level for as
/// long as the key is down; decay only runs after release.</summary>
public sealed class SparkleEffect : IEffect
{
    public const string TypeName = "sparkle";

    private const float SparkleFadeRate = 6f;

    private float level;
    private float[] cellLevels = Array.Empty<float>();
    private bool held;

    public string TypeId => TypeName;

    public bool IsActive => level > 0f;

    public void Trigger(in TriggerInfo info, in ResolvedParams p)
    {
        level = Math.Clamp(info.Intensity01 * p.EffectIntensity, 0f, 1f);
        held = info.Sustain;
    }

    public void Release() => held = false;

    public void Update(float dt, in ResolvedParams p) => level = EffectEnvelope.Decay(level, dt, p.DecaySeconds, held);

    public void Render(Span<Rgb8> cells, int cellsW, int cellsH, in ResolvedParams p)
    {
        int count = cellsW * cellsH;
        if (cellLevels.Length != count) cellLevels = new float[count];

        float spawnChance = 0.05f + p.EffectSpeed * 0.35f;
        float fade = MathF.Exp(-SparkleFadeRate / 60f); // ~1 tick's worth of fade at the engine's nominal rate

        for (int i = 0; i < count; i++)
        {
            cellLevels[i] *= fade;
            if (Random.Shared.NextSingle() < spawnChance) cellLevels[i] = 1f;
            cells[i] = p.ZoneColor.Scale(level * cellLevels[i]);
        }
    }
}
