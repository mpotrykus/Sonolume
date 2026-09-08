using Sonolume.Engine.Effects;
using Xunit;

namespace Sonolume.Engine.Tests;

public class EffectRegistryTests
{
    [Theory]
    [InlineData(SolidEffect.TypeName)]
    [InlineData(FlashEffect.TypeName)]
    [InlineData(WaveEffect.TypeName)]
    [InlineData(PulseEffect.TypeName)]
    [InlineData(StrobeEffect.TypeName)]
    [InlineData(ChaseEffect.TypeName)]
    [InlineData(RippleEffect.TypeName)]
    [InlineData(SparkleEffect.TypeName)]
    [InlineData(RainbowEffect.TypeName)]
    public void CreateDefault_RegistersEveryBuiltInEffect(string typeId)
    {
        var registry = EffectRegistry.CreateDefault();
        Assert.True(registry.Contains(typeId));
        var effect = registry.Create(typeId);
        Assert.Equal(typeId, effect.TypeId);
    }

    [Fact]
    public void Create_UnknownId_Throws()
    {
        var registry = EffectRegistry.CreateDefault();
        Assert.Throws<KeyNotFoundException>(() => registry.Create("nonexistent"));
    }
}
