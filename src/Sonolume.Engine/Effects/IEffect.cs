using Sonolume.Engine.Core;

namespace Sonolume.Engine.Effects;

/// <summary>What started an effect: the transformed mapping value plus the raw source details for pitch- or velocity-aware effects.</summary>
public readonly record struct TriggerInfo(float Intensity01, int Note, float Velocity01, long TimestampTicks);

/// <summary>Zone parameters after group composition. Computed once per zone per tick.</summary>
public readonly record struct ResolvedParams(
    float Brightness,
    float Hue,
    float Saturation,
    bool Active,
    float EffectIntensity,
    float EffectSpeed,
    float DecaySeconds,
    float PosX,
    float PosY,
    float PaletteIndex)
{
    /// <summary>The zone's steady color at full effect gain.</summary>
    public Rgb8 ZoneColor => Active ? new Hsb(Hue, Saturation, Brightness).ToRgb8() : Rgb8.Black;
}

/// <summary>
/// An animated effect instance owned by one zone. Effects consume resolved parameters every tick,
/// so a CC mapped to decay or intensity takes effect immediately.
/// </summary>
public interface IEffect
{
    string TypeId { get; }

    /// <summary>False once the effect has fully decayed; the zone then shows its idle state.</summary>
    bool IsActive { get; }

    void Trigger(in TriggerInfo info, in ResolvedParams p);

    void Release();

    void Update(float dt, in ResolvedParams p);

    void Render(Span<Rgb8> cells, int cellsW, int cellsH, in ResolvedParams p);
}
