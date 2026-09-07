using System.Diagnostics;

namespace Sonolume.Engine.Core;

public static class Clock
{
    public static long Now() => Stopwatch.GetTimestamp();

    public static double TicksToSeconds(long ticks) => ticks / (double)Stopwatch.Frequency;

    public static double TicksToMilliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    public static long SecondsToTicks(double seconds) => (long)(seconds * Stopwatch.Frequency);
}
