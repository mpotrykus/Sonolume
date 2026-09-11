using Sonolume.Engine.Core;
using Sonolume.Engine.Effects;
using Xunit;

namespace Sonolume.Engine.Tests;

public class WaveEffectTests
{
    private static ResolvedParams Params(float decay = 5f, float rotation = 0f) =>
        new(1f, 0f, 1f, true, 1f, 0.5f, decay, 0.5f, 0.5f, 0f, rotation);

    private static int Brightest(Span<Rgb8> cells)
    {
        int best = 0;
        for (int i = 1; i < cells.Length; i++)
            if (cells[i].R > cells[best].R) best = i;
        return best;
    }

    [Fact]
    public void Trigger_SetsActive()
    {
        var wave = new WaveEffect();
        wave.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params());
        Assert.True(wave.IsActive);
    }

    [Fact]
    public void Render_VariesBrightnessAcrossCells()
    {
        var wave = new WaveEffect();
        wave.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params());
        wave.Update(0.2f, Params());

        Span<Rgb8> cells = stackalloc Rgb8[8];
        wave.Render(cells, 8, 1, Params());

        Assert.True(cells.ToArray().Distinct().Count() > 1);
    }

    [Fact]
    public void Render_BandTravelsFromStartToEnd_ThenStaysGone()
    {
        var wave = new WaveEffect();
        wave.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params());

        wave.Update(0.05f, Params());
        Span<Rgb8> cellsEarly = stackalloc Rgb8[9];
        wave.Render(cellsEarly, 9, 1, Params());
        int posEarly = Brightest(cellsEarly);

        for (int i = 0; i < 20; i++) wave.Update(1f / 60f, Params());
        Span<Rgb8> cellsMid = stackalloc Rgb8[9];
        wave.Render(cellsMid, 9, 1, Params());
        int posMid = Brightest(cellsMid);

        Assert.True(posMid > posEarly, $"expected the band to have moved further along ({posEarly}), got {posMid}");

        // Long after it should have run off the far edge, it must not reappear (no looping back to the start).
        for (int i = 0; i < 300; i++) wave.Update(1f / 60f, Params());
        Span<Rgb8> cellsLate = stackalloc Rgb8[9];
        wave.Render(cellsLate, 9, 1, Params());
        Assert.All(cellsLate.ToArray(), c => Assert.Equal(0, c.R));
    }

    [Fact]
    public void Rotation_TurnsTravelFromHorizontalToVertical()
    {
        // At rotation 0 the band should vary across columns but be uniform down any column.
        var horizontal = new WaveEffect();
        horizontal.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params());
        horizontal.Update(0.05f, Params());
        Span<Rgb8> hCells = stackalloc Rgb8[9];
        horizontal.Render(hCells, 3, 3, Params());
        Assert.Equal(hCells[0].R, hCells[3].R);
        Assert.Equal(hCells[0].R, hCells[6].R);

        // At rotation 90 degrees (quarter turn) the band should instead vary down rows but be uniform across any row.
        var vertical = new WaveEffect();
        vertical.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params(rotation: 90f));
        vertical.Update(0.05f, Params(rotation: 90f));
        Span<Rgb8> vCells = stackalloc Rgb8[9];
        vertical.Render(vCells, 3, 3, Params(rotation: 90f));
        Assert.Equal(vCells[0].R, vCells[1].R);
        Assert.Equal(vCells[0].R, vCells[2].R);
    }

    [Fact]
    public void Decay_EventuallyStops()
    {
        var wave = new WaveEffect();
        wave.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params(decay: 0.1f));
        for (int i = 0; i < 600; i++) wave.Update(1f / 120f, Params(decay: 0.1f));
        Assert.False(wave.IsActive);
    }

    [Fact]
    public void Sustain_HoldsLevelUntilRelease()
    {
        var wave = new WaveEffect();
        wave.Trigger(new TriggerInfo(1f, 36, 1f, 0, Sustain: true), Params(decay: 0.05f));

        for (int i = 0; i < 240; i++) wave.Update(1f / 120f, Params(decay: 0.05f));
        Assert.True(wave.IsActive);

        wave.Release();
        for (int i = 0; i < 240; i++) wave.Update(1f / 120f, Params(decay: 0.05f));
        Assert.False(wave.IsActive);
    }
}
