using Sonolume.Engine.Core;
using Sonolume.Engine.Effects;
using Xunit;

namespace Sonolume.Engine.Tests;

public class RippleEffectTests
{
    private static ResolvedParams Params(float posX = 0.5f, float posY = 0.5f, float decay = 5f) =>
        new(1f, 0f, 1f, true, 1f, 0.5f, decay, posX, posY, 0f);

    private static int Brightest(Span<Rgb8> cells)
    {
        int best = 0;
        for (int i = 1; i < cells.Length; i++)
            if (cells[i].R > cells[best].R) best = i;
        return best;
    }

    [Fact]
    public void Render_RingExpandsOutwardFromOriginOverTime()
    {
        var ripple = new RippleEffect();
        ripple.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params());

        ripple.Update(0.05f, Params());
        Span<Rgb8> cellsEarly = stackalloc Rgb8[9];
        ripple.Render(cellsEarly, 9, 1, Params());
        float distEarly = MathF.Abs(Brightest(cellsEarly) / 8f - 0.5f);

        for (int i = 0; i < 30; i++) ripple.Update(1f / 60f, Params());
        Span<Rgb8> cellsLater = stackalloc Rgb8[9];
        ripple.Render(cellsLater, 9, 1, Params());
        float distLater = MathF.Abs(Brightest(cellsLater) / 8f - 0.5f);

        Assert.True(distLater > distEarly, $"expected the ring to move further from the center ({distEarly}), got {distLater}");
    }

    [Fact]
    public void Trigger_CapturesOriginFromPosParams()
    {
        var ripple = new RippleEffect();
        ripple.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params(posX: 0.2f));
        ripple.Update(0.01f, Params(posX: 0.2f));

        Span<Rgb8> cells = stackalloc Rgb8[9];
        ripple.Render(cells, 9, 1, Params(posX: 0.2f));

        float peakPos = Brightest(cells) / 8f;
        Assert.InRange(peakPos, 0f, 0.4f);
    }

    [Fact]
    public void Decay_EventuallyStops()
    {
        var ripple = new RippleEffect();
        ripple.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params(decay: 0.1f));
        for (int i = 0; i < 600; i++) ripple.Update(1f / 120f, Params(decay: 0.1f));
        Assert.False(ripple.IsActive);
    }
}
