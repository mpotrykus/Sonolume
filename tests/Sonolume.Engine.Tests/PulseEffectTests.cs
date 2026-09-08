using Sonolume.Engine.Core;
using Sonolume.Engine.Effects;
using Xunit;

namespace Sonolume.Engine.Tests;

public class PulseEffectTests
{
    private static ResolvedParams Params(float decay = 5f) =>
        new(1f, 0f, 1f, true, 1f, 0.5f, decay, 0.5f, 0.5f, 0f);

    [Fact]
    public void Trigger_SetsActive()
    {
        var pulse = new PulseEffect();
        pulse.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params());
        Assert.True(pulse.IsActive);
    }

    [Fact]
    public void Render_BreathesOverTime()
    {
        var pulse = new PulseEffect();
        pulse.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params());

        Span<Rgb8> cellsAtStart = stackalloc Rgb8[1];
        pulse.Render(cellsAtStart, 1, 1, Params());

        for (int i = 0; i < 30; i++) pulse.Update(1f / 120f, Params());
        Span<Rgb8> cellsLater = stackalloc Rgb8[1];
        pulse.Render(cellsLater, 1, 1, Params());

        Assert.NotEqual(cellsAtStart[0], cellsLater[0]);
    }

    [Fact]
    public void Decay_EventuallyStops()
    {
        var pulse = new PulseEffect();
        pulse.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params(decay: 0.1f));
        for (int i = 0; i < 600; i++) pulse.Update(1f / 120f, Params(decay: 0.1f));
        Assert.False(pulse.IsActive);
    }
}
