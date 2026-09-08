using System.Linq;
using Sonolume.Engine.Input;
using Sonolume.Engine.Model;

namespace Sonolume.Engine.Mappings;

/// <summary>The fixed Macro-slot-to-param convention every new zone/group is pre-wired with, so the plugin's host
/// parameters ("Macro 1".."Macro 8" - see <see cref="SourceAddress.MacroCount"/>) are useful the moment a target
/// exists, with no per-param setup. Users can still repoint any single param to a different macro (or none) from
/// the zone/group editor; this only supplies the starting point.</summary>
public static class DefaultMacros
{
    private static readonly ParamId[] Params =
    {
        ParamId.Brightness,
        ParamId.Hue,
        ParamId.Saturation,
        ParamId.EffectIntensity,
        ParamId.EffectSpeed,
        ParamId.EffectDecay,
        ParamId.PosX,
        ParamId.PosY,
    };

    /// <summary>The param each macro slot drives by default, in order - the single source of truth for both
    /// <see cref="For"/> and the plugin's static host-parameter labels (see SonolumePlugin.Initialize). Only a
    /// starting label: the host has no API to rename a parameter after declaration, so this won't follow a zone
    /// that's since been repointed to a different macro via the picker.</summary>
    public static IReadOnlyList<ParamId> Convention => Params;

    public static IEnumerable<Mapping> For(TargetRef target)
    {
        for (int i = 0; i < Params.Length; i++)
        {
            yield return new Mapping
            {
                Id = $"macro-{i}-{target.Kind}-{target.Id}",
                Source = SourceAddress.HostMacro(i),
                Target = target,
                Param = Params[i],
                Mode = MappingMode.Set,
                Transform = Transform.Identity,
            };
        }
    }

    /// <summary>One-time migration for projects saved before this convention existed (a REAPER session's saved
    /// plugin state, an older .sonolume file, ...): if the whole project has zero host-macro mappings anywhere,
    /// seeds every zone with the default convention. No-op the moment even one HostMacro mapping exists anywhere -
    /// including a project where the user deliberately cleared some via the macro picker - so this never fights a
    /// real choice once the project has engaged with the macro system at all. Called on every project load.</summary>
    public static void BackfillIfMissing(Project project)
    {
        if (project.Mappings.Any(m => m.Source.Kind == SourceKind.HostMacro)) return;
        foreach (var zone in project.Zones) project.Mappings.AddRange(For(TargetRef.Zone(zone.Id)));
    }
}
