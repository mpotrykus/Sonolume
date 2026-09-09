namespace Sonolume.Engine.Effects;

/// <summary>Effect factory keyed by type id. New effects register here; nothing else in the pipeline changes.</summary>
public sealed class EffectRegistry
{
    private readonly Dictionary<string, Func<IEffect>> factories = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> order = new();

    /// <summary>Registration order - the effect-type CC (see Engine.SelectEffect) quantizes its 0..1 value into
    /// an index into this list, so it must stay stable rather than following Dictionary's unordered Keys.</summary>
    public IReadOnlyList<string> Ids => order;

    public void Register(string typeId, Func<IEffect> factory)
    {
        if (!factories.ContainsKey(typeId)) order.Add(typeId);
        factories[typeId] = factory;
    }

    public bool Contains(string typeId) => factories.ContainsKey(typeId);

    public IEffect Create(string typeId)
    {
        if (!factories.TryGetValue(typeId, out var factory))
            throw new KeyNotFoundException($"Unknown effect '{typeId}'. Registered: {string.Join(", ", factories.Keys)}");
        return factory();
    }

    public static EffectRegistry CreateDefault()
    {
        var registry = new EffectRegistry();
        registry.Register(SolidEffect.TypeName, static () => new SolidEffect());
        registry.Register(FlashEffect.TypeName, static () => new FlashEffect());
        registry.Register(WaveEffect.TypeName, static () => new WaveEffect());
        registry.Register(PulseEffect.TypeName, static () => new PulseEffect());
        registry.Register(StrobeEffect.TypeName, static () => new StrobeEffect());
        registry.Register(ChaseEffect.TypeName, static () => new ChaseEffect());
        registry.Register(RippleEffect.TypeName, static () => new RippleEffect());
        registry.Register(SparkleEffect.TypeName, static () => new SparkleEffect());
        registry.Register(RainbowEffect.TypeName, static () => new RainbowEffect());
        return registry;
    }
}
