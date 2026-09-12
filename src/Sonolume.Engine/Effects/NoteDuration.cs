namespace Sonolume.Engine.Effects;

/// <summary>The shared musical-duration meaning of EffectSpeed: every effect that reads it picks a length of time
/// from this same discrete table (1/32 note up to 4 bars, 4 beats/bar) instead of treating "speed" as its own
/// continuous rate - so the same dropdown selection means the same thing whether it is Wave's sweep duration,
/// Pulse's breathing cycle, or Sparkle's average time between twinkles. Tempo-locked when the host reports one;
/// otherwise the same note value is measured against <see cref="FallbackBeatsPerSecond"/> (an assumed 120 BPM), so
/// even standalone/no-host-tempo runs still give the selection a concrete duration rather than an arbitrary feel.
/// Steps are ordered shortest-to-longest, the order the UI dropdown lists them in; EffectSpeed itself still runs
/// 0..1 with 1 = shortest/fastest, matching every effect's pre-existing "higher speed = faster" convention.</summary>
public static class NoteDuration
{
    public const float FallbackBeatsPerSecond = 2f;

    public static readonly (string Label, float Beats)[] Steps =
    {
        ("1/32", 0.125f),
        ("1/16", 0.25f),
        ("1/8", 0.5f),
        ("1/4", 1f),
        ("1/2", 2f),
        ("1 bar", 4f),
        ("2 bar", 8f),
        ("3 bar", 12f),
        ("4 bar", 16f),
    };

    /// <summary>Index into <see cref="Steps"/> that EffectSpeed (0..1, 1 = fastest) currently selects.</summary>
    public static int IndexOf(float effectSpeed01)
    {
        int lastIndex = Steps.Length - 1;
        int fromFastest = (int)MathF.Round(Math.Clamp(effectSpeed01, 0f, 1f) * lastIndex);
        return lastIndex - Math.Clamp(fromFastest, 0, lastIndex);
    }

    /// <summary>The EffectSpeed value (0..1) that selects <see cref="Steps"/>[index].</summary>
    public static float ValueFor(int index)
    {
        int lastIndex = Steps.Length - 1;
        return 1f - Math.Clamp(index, 0, lastIndex) / (float)lastIndex;
    }

    /// <summary>Beats for the note value EffectSpeed currently selects.</summary>
    public static float Beats(float effectSpeed01) => Steps[IndexOf(effectSpeed01)].Beats;

    /// <summary>Seconds for one cycle/traversal of the selected note value.</summary>
    public static float Seconds(in ResolvedParams p)
    {
        float bps = p.TempoSynced && p.BeatsPerSecond > 0f ? p.BeatsPerSecond : FallbackBeatsPerSecond;
        return Beats(p.EffectSpeed) / bps;
    }
}
