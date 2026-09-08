using Sonolume.Engine.Core;
using Sonolume.Engine.Effects;
using Xunit;

namespace Sonolume.Engine.Tests;

public class RainbowEffectTests
{
    private static ResolvedParams Params() => new(1f, 0f, 1f, true, 1f, 0.5f, 0.25f, 0.5f, 0.5f, 0f);

    [Fact]
    public void Trigger_SetsActive()
    {
        var rainbow = new RainbowEffect();
        rainbow.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params());
        Assert.True(rainbow.IsActive);
    }

    [Fact]
    public void Release_TurnsOff()
    {
        var rainbow = new RainbowEffect();
        rainbow.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params());
        rainbow.Release();
        Assert.False(rainbow.IsActive);
    }

    [Fact]
    public void Render_HueShiftsWhileActive()
    {
        var rainbow = new RainbowEffect();
        rainbow.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params());

        Span<Rgb8> cellsAtStart = stackalloc Rgb8[1];
        rainbow.Render(cellsAtStart, 1, 1, Params());

        for (int i = 0; i < 60; i++) rainbow.Update(1f / 60f, Params());
        Span<Rgb8> cellsLater = stackalloc Rgb8[1];
        rainbow.Render(cellsLater, 1, 1, Params());

        Assert.NotEqual(cellsAtStart[0], cellsLater[0]);
    }

    [Fact]
    public void Update_DoesNothingWhileInactive()
    {
        var rainbow = new RainbowEffect();
        rainbow.Update(1f / 60f, Params());
        Assert.False(rainbow.IsActive);
    }
}
