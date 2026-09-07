using Sonolume.Engine.Input;
using Sonolume.Engine.Model;

namespace Sonolume.Engine.Mappings;

/// <summary>What the user selected before moving a controller: "Kick / Brightness / [Learn]".</summary>
public sealed record LearnRequest(TargetRef Target, ParamId Param, MappingMode Mode, string? EffectId = null, bool MatchChannel = false);

public static class MidiLearn
{
    public static bool Accepts(LearnRequest request, in ControlEvent e) => request.Mode switch
    {
        MappingMode.Set => e.Type == ControlEventType.Set,
        _ => e.Type == ControlEventType.Trigger,
    };

    public static Mapping CreateMapping(LearnRequest request, in ControlEvent e, string id) => new()
    {
        Id = id,
        Source = new SourceAddress(e.Source.Kind, request.MatchChannel ? e.Source.Channel : SourceAddress.Any, e.Source.Number),
        Target = request.Target,
        Param = request.Param,
        Mode = request.Mode,
        EffectId = request.Mode == MappingMode.Set ? null : request.EffectId,
        Transform = Transform.Identity,
    };
}
