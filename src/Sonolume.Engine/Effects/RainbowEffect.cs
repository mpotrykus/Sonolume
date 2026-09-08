using Sonolume.Engine.Core;

namespace Sonolume.Engine.Effects;

/// <summary>Cycles hue away from the zone's set color while active, latched like <see cref="SolidEffect"/>
/// (Trigger turns it on, Release turns it off). EffectSpeed sets the cycle rate - tempo-locked when the host
/// reports a tempo, otherwise free-running.</summary>
public sealed class RainbowEffect : IEffect
{
    public const string TypeName = "rainbow";

    private bool active;
    private float phase;

    public string TypeId => TypeName;

    public bool IsActive => active;

    public void Trigger(in TriggerInfo info, in ResolvedParams p) => active = true;

    public void Release() => active = false;

    public void Update(float dt, in ResolvedParams p)
    {
        if (!active) return;
        phase = EffectClock.Advance(phase, dt, p);
    }

    public void Render(Span<Rgb8> cells, int cellsW, int cellsH, in ResolvedParams p)
    {
        float hue = p.Hue + phase;
        hue -= MathF.Floor(hue);
        var color = new Hsb(hue, p.Saturation, p.Brightness * p.EffectIntensity).ToRgb8();
        cells.Fill(color);
    }
}
