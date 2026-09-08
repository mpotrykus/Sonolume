using Sonolume.Engine.Effects;
using Xunit;

namespace Sonolume.Engine.Tests;

public class FlashEffectTests
{
    private static ResolvedParams Params(float decay = 0.25f, float intensity = 1f) =>
        new(1f, 0f, 1f, true, intensity, 0.5f, decay, 0.5f, 0.5f, 0f);

    [Fact]
    public void Trigger_SetsLevelToIntensity()
    {
        var flash = new FlashEffect();
        flash.Trigger(new TriggerInfo(0.8f, 36, 0.8f, 0), Params());
        Assert.Equal(0.8f, flash.Level, 4);
        Assert.True(flash.IsActive);
    }

    [Fact]
    public void Decay_ReachesFivePercentAtDecayTime()
    {
        var flash = new FlashEffect();
        flash.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params(decay: 0.25f));

        for (int i = 0; i < 15; i++) flash.Update(1f / 120f, Params(decay: 0.25f));
        Assert.Equal(0.2236f, flash.Level, 3);

        for (int i = 0; i < 15; i++) flash.Update(1f / 120f, Params(decay: 0.25f));
        Assert.Equal(0.05f, flash.Level, 3);
    }

    [Fact]
    public void Decay_EventuallyFinishes()
    {
        var flash = new FlashEffect();
        flash.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params());
        for (int i = 0; i < 240; i++) flash.Update(1f / 120f, Params());
        Assert.False(flash.IsActive);
        Assert.Equal(0f, flash.Level);
    }

    [Fact]
    public void EffectIntensityParam_ScalesPeak()
    {
        var flash = new FlashEffect();
        flash.Trigger(new TriggerInfo(1f, 36, 1f, 0), Params(intensity: 0.5f));
        Assert.Equal(0.5f, flash.Level, 4);
    }

    [Fact]
    public void Sustain_HoldsLevelUntilRelease()
    {
        var flash = new FlashEffect();
        flash.Trigger(new TriggerInfo(1f, 36, 1f, 0, Sustain: true), Params(decay: 0.05f));

        // Held well past what would normally be a full decay - level must not have moved.
        for (int i = 0; i < 240; i++) flash.Update(1f / 120f, Params(decay: 0.05f));
        Assert.Equal(1f, flash.Level, 4);
        Assert.True(flash.IsActive);

        flash.Release();
        for (int i = 0; i < 240; i++) flash.Update(1f / 120f, Params(decay: 0.05f));
        Assert.False(flash.IsActive);
    }

    [Fact]
    public void NoSustain_DecaysImmediatelyEvenThoughNeverReleased()
    {
        var flash = new FlashEffect();
        flash.Trigger(new TriggerInfo(1f, 36, 1f, 0, Sustain: false), Params(decay: 0.05f));
        for (int i = 0; i < 240; i++) flash.Update(1f / 120f, Params(decay: 0.05f));
        Assert.False(flash.IsActive);
    }
}
