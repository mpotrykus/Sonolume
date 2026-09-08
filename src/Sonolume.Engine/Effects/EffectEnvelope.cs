namespace Sonolume.Engine.Effects;

/// <summary>The exponential decay envelope shared by every one-shot effect (Flash and the effects layered on top
/// of it): reaches 5% of peak at DecaySeconds, snaps to 0 below a floor so IsActive can settle cleanly.</summary>
internal static class EffectEnvelope
{
    // ln(20): the fraction remaining after one DecaySeconds is 1/20.
    private const float DecayConstant = 2.9957323f;
    private const float Floor = 1f / 512f;

    /// <param name="held">True while the key is still down on a Gate mapping: the level is sustained unchanged
    /// instead of decaying, so the effect lasts as long as it's held. Decay resumes (or starts) once released.</param>
    public static float Decay(float level, float dt, float decaySeconds, bool held = false)
    {
        if (held || level <= 0f || dt <= 0f) return level;
        float decay = MathF.Max(0.005f, decaySeconds);
        level *= MathF.Exp(-dt * DecayConstant / decay);
        return level < Floor ? 0f : level;
    }
}
