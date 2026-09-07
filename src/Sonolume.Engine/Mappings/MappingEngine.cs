using Sonolume.Engine.Input;

namespace Sonolume.Engine.Mappings;

/// <summary>A mapping that matched a control event, with the transformed value.</summary>
public readonly record struct MappingAction(Mapping Mapping, ControlEvent Event, float Value01);

/// <summary>Resolves control events against the active mappings. Pure and allocation-free on the hot path.</summary>
public sealed class MappingEngine
{
    private readonly Mapping[] mappings;

    public MappingEngine(IEnumerable<Mapping> mappings)
    {
        this.mappings = mappings.ToArray();
    }

    public int Count => mappings.Length;

    public int Resolve(in ControlEvent e, Span<MappingAction> output)
    {
        int count = 0;
        foreach (var m in mappings)
        {
            if (!m.Enabled || !m.AcceptsEventType(e.Type) || !m.Source.Matches(e.Source)) continue;
            output[count++] = new MappingAction(m, e, m.Transform.Apply(e.Value01));
            if (count == output.Length) break;
        }
        return count;
    }
}
