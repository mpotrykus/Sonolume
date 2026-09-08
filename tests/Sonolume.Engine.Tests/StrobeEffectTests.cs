using Sonolume.Engine.Core;
using Sonolume.Engine.Effects;
using Xunit;

namespace Sonolume.Engine.Tests;

public class StrobeEffectTests
{
    private static ResolvedParams Params(float decay = 5f) =>
        new(1f, 0f, 1f, true, 1f, 0.5f, decay, 0.5f, 0.5f, 0f);

    [Fact]
    public void Render_IsAlwaysFullyOnOrFullyOff()
    {
        var strobe = new StrobeEffect();
        strobe.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params());

        bool sawOn = false, sawOff = false;
        Span<Rgb8> cells = stackalloc Rgb8[1];
        for (int i = 0; i < 300; i++)
        {
            strobe.Update(1f / 60f, Params());
            strobe.Render(cells, 1, 1, Params());
            if (cells[0] == Rgb8.Black) sawOff = true;
            else { Assert.True(cells[0].R > 0 && cells[0].G == 0 && cells[0].B == 0); sawOn = true; }
        }

        Assert.True(sawOn && sawOff);
    }

    [Fact]
    public void Decay_EventuallyStops()
    {
        var strobe = new StrobeEffect();
        strobe.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params(decay: 0.1f));
        for (int i = 0; i < 600; i++) strobe.Update(1f / 120f, Params(decay: 0.1f));
        Assert.False(strobe.IsActive);
    }
}
