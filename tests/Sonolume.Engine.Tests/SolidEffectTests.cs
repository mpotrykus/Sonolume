using Sonolume.Engine.Core;
using Sonolume.Engine.Effects;
using Xunit;

namespace Sonolume.Engine.Tests;

public class SolidEffectTests
{
    private static ResolvedParams Params(float intensity = 1f) =>
        new(1f, 0f, 1f, true, intensity, 0.5f, 0.25f, 0.5f, 0.5f, 0f);

    [Fact]
    public void Trigger_TurnsOnAtFullZoneColor()
    {
        var solid = new SolidEffect();
        solid.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params());
        Assert.True(solid.IsActive);

        Span<Rgb8> cells = stackalloc Rgb8[1];
        solid.Render(cells, 1, 1, Params());
        Assert.Equal(new Rgb8(255, 0, 0), cells[0]);
    }

    [Fact]
    public void Update_NeverDecays()
    {
        var solid = new SolidEffect();
        solid.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params());
        for (int i = 0; i < 600; i++) solid.Update(1f / 120f, Params());
        Assert.True(solid.IsActive);
    }

    [Fact]
    public void Release_TurnsOff()
    {
        var solid = new SolidEffect();
        solid.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params());
        solid.Release();
        Assert.False(solid.IsActive);
    }
}
