using Sonolume.Engine.Core;

namespace Sonolume.Engine.Mappings;

/// <summary>
/// Maps a normalized input (0..1) to a normalized output (0..1): input window, inversion, curve, output range.
/// Example: MIDI 0..127 to brightness 10%..100% is InMin 0, InMax 1, OutMin 0.1, OutMax 1.
/// </summary>
public sealed record Transform(
    float InMin = 0f,
    float InMax = 1f,
    float OutMin = 0f,
    float OutMax = 1f,
    Curve Curve = Curve.Linear,
    bool Invert = false)
{
    public static readonly Transform Identity = new();

    public float Apply(float x01)
    {
        float span = InMax - InMin;
        float t = span == 0f ? 0f : (x01 - InMin) / span;
        t = Math.Clamp(t, 0f, 1f);
        if (Invert) t = 1f - t;
        t = CurveMath.Apply(Curve, t);
        return Math.Clamp(OutMin + t * (OutMax - OutMin), 0f, 1f);
    }
}
