namespace Sonolume.Engine.Effects;

/// <summary>Shared cycle-phase math for effects that loop (Wave, Pulse, Strobe, Chase). When the host reports a
/// tempo, the cycle locks to <see cref="ResolvedParams.SongBeats"/> - the note value <see cref="NoteDuration"/>
/// picks from EffectSpeed - so every synced zone stays in phase with the song regardless of when it was triggered.
/// Free-running (standalone, or no host tempo), the same note value instead sets a period against
/// <see cref="NoteDuration.FallbackBeatsPerSecond"/>, and phase accumulates from the effect's own elapsed time.</summary>
internal static class EffectClock
{
    /// <summary>Advances a free-running phase accumulator, or recomputes the tempo-locked phase from
    /// <see cref="ResolvedParams.SongBeats"/>. Returns the new 0..1 phase (wraps).</summary>
    public static float Advance(float phase, float dt, in ResolvedParams p)
    {
        float beats = NoteDuration.Beats(p.EffectSpeed);

        if (p.TempoSynced && p.BeatsPerSecond > 0f)
        {
            float cycles = (float)(p.SongBeats / beats);
            return cycles - MathF.Floor(cycles);
        }

        float periodSeconds = beats / NoteDuration.FallbackBeatsPerSecond;
        float next = phase + dt / periodSeconds;
        return next - MathF.Floor(next);
    }
}
