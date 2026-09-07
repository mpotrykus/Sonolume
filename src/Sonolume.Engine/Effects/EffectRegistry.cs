namespace Sonolume.Engine.Effects;

/// <summary>Effect factory keyed by type id. New effects register here; nothing else in the pipeline changes.</summary>
public sealed class EffectRegistry
{
    private readonly Dictionary<string, Func<IEffect>> factories = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Ids => factories.Keys;

    public void Register(string typeId, Func<IEffect> factory) => factories[typeId] = factory;

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
        registry.Register(FlashEffect.TypeName, static () => new FlashEffect());
        return registry;
    }
}
