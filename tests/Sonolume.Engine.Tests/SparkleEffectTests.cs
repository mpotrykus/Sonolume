using Sonolume.Engine.Core;
using Sonolume.Engine.Effects;
using Xunit;

namespace Sonolume.Engine.Tests;

public class SparkleEffectTests
{
    private static ResolvedParams Params(float decay = 5f) =>
        new(1f, 0f, 1f, true, 1f, 0.9f, decay, 0.5f, 0.5f, 0f);

    [Fact]
    public void Trigger_SetsActive()
    {
        var sparkle = new SparkleEffect();
        sparkle.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params());
        Assert.True(sparkle.IsActive);
    }

    [Fact]
    public void Render_EventuallyLightsSomeCells()
    {
        var sparkle = new SparkleEffect();
        sparkle.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params());

        Span<Rgb8> cells = stackalloc Rgb8[64];
        bool sawLitCell = false;
        for (int i = 0; i < 50 && !sawLitCell; i++)
        {
            sparkle.Update(1f / 60f, Params());
            sparkle.Render(cells, 64, 1, Params());
            foreach (var c in cells)
                if (c.R > 0) { sawLitCell = true; break; }
        }

        Assert.True(sawLitCell, "expected at least one sparkle to light up within 50 renders across 64 cells");
    }

    [Fact]
    public void Decay_EventuallyStops()
    {
        var sparkle = new SparkleEffect();
        sparkle.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params(decay: 0.1f));
        for (int i = 0; i < 600; i++) sparkle.Update(1f / 120f, Params(decay: 0.1f));
        Assert.False(sparkle.IsActive);
    }
}
