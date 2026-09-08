using Sonolume.Engine.Core;

namespace Sonolume.Engine.Effects;

/// <summary>Latches to full color with no decay: Trigger turns it on, Release turns it off. For a Gate mapping
/// that's "solid while held"; for a one-shot Trigger mapping (no release ever arrives) it just stays lit.</summary>
public sealed class SolidEffect : IEffect
{
    public const string TypeName = "solid";

    private bool active;
    private float level;

    public string TypeId => TypeName;

    public bool IsActive => active;

    public void Trigger(in TriggerInfo info, in ResolvedParams p)
    {
        level = Math.Clamp(info.Intensity01 * p.EffectIntensity, 0f, 1f);
        active = true;
    }

    public void Release() => active = false;

    public void Update(float dt, in ResolvedParams p)
    {
    }

    public void Render(Span<Rgb8> cells, int cellsW, int cellsH, in ResolvedParams p) =>
        cells.Fill(p.ZoneColor.Scale(level));
}
