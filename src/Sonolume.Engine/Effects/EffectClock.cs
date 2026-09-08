namespace Sonolume.Engine.Effects;

/// <summary>Shared cycle-phase math for effects that loop (Wave, Pulse, Strobe, Chase). When the host reports a
/// tempo, the cycle locks to <see cref="ResolvedParams.SongBeats"/> - a musical subdivision picked by EffectSpeed -
/// so every synced zone stays in phase with the song regardless of when it was triggered. Free-running (standalone,
/// or no host tempo), EffectSpeed instead selects a continuous period and phase accumulates from the effect's own
/// elapsed time.</summary>
internal static class EffectClock
{
    /// <summary>Advances a free-running phase accumulator, or recomputes the tempo-locked phase from
    /// <see cref="ResolvedParams.SongBeats"/>. Returns the new 0..1 phase (wraps).</summary>
    public static float Advance(float phase, float dt, in ResolvedParams p)
    {
        if (p.TempoSynced && p.BeatsPerSecond > 0f)
        {
            float beats = (float)(p.SongBeats / BeatsPerCycle(p.EffectSpeed));
            return beats - MathF.Floor(beats);
        }

        float period = FreeRunningPeriodSeconds(p.EffectSpeed);
        float next = phase + dt / period;
        return next - MathF.Floor(next);
    }

    /// <summary>EffectSpeed 0..1 quantized to a musical rate: whole down to 1/32 note (in beats), the way a DAW's
    /// LFO/delay rate selector would.</summary>
    private static float BeatsPerCycle(float effectSpeed01) => effectSpeed01 switch
    {
        < 1f / 6f => 4f,
        < 2f / 6f => 2f,
        < 3f / 6f => 1f,
        < 4f / 6f => 0.5f,
        < 5f / 6f => 0.25f,
        _ => 0.125f,
    };

    /// <summary>EffectSpeed 0..1 mapped to a period from slow (2.2s) to fast (~0.2s) when there's no tempo to lock to.</summary>
    private static float FreeRunningPeriodSeconds(float effectSpeed01) => MathF.Max(0.05f, 2.2f - effectSpeed01 * 2f);
}
