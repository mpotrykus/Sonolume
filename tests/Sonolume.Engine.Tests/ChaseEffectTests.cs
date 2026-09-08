using Sonolume.Engine.Core;
using Sonolume.Engine.Effects;
using Xunit;

namespace Sonolume.Engine.Tests;

public class ChaseEffectTests
{
    private static ResolvedParams Params(float decay = 5f) =>
        new(1f, 0f, 1f, true, 1f, 0.5f, decay, 0.5f, 0.5f, 0f);

    private static int Brightest(Span<Rgb8> cells)
    {
        int best = 0;
        for (int i = 1; i < cells.Length; i++)
            if (cells[i].R > cells[best].R) best = i;
        return best;
    }

    [Fact]
    public void Render_HotCellMovesOverTime()
    {
        var chase = new ChaseEffect();
        chase.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params());
        chase.Update(0.05f, Params());
        Span<Rgb8> cellsEarly = stackalloc Rgb8[16];
        chase.Render(cellsEarly, 16, 1, Params());
        int posEarly = Brightest(cellsEarly);

        for (int i = 0; i < 30; i++) chase.Update(1f / 120f, Params());
        Span<Rgb8> cellsLater = stackalloc Rgb8[16];
        chase.Render(cellsLater, 16, 1, Params());
        int posLater = Brightest(cellsLater);

        Assert.NotEqual(posEarly, posLater);
    }

    [Fact]
    public void Decay_EventuallyStops()
    {
        var chase = new ChaseEffect();
        chase.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params(decay: 0.1f));
        for (int i = 0; i < 600; i++) chase.Update(1f / 120f, Params(decay: 0.1f));
        Assert.False(chase.IsActive);
    }
}
