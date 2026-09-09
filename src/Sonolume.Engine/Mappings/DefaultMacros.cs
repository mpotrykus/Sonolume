using System.Linq;
using Sonolume.Engine.Input;
using Sonolume.Engine.Model;

namespace Sonolume.Engine.Mappings;

/// <summary>The fixed macro-slot-to-param convention every zone/group is wired with, matching the zone/group
/// editor's row order top to bottom (see SonolumeView.RebuildParamsPanel). Unlike a starting point, this is now
/// permanent - nothing in the UI can repoint a param to a different macro; the editor just labels each row with
/// its slot (see SonolumeView.BuildMacroLabel).</summary>
public static class DefaultMacros
{
    /// <summary>The macro slot dedicated to effect-type switching (a Select-mode mapping, see
    /// <see cref="MappingMode.Select"/>) - the "Type" row's CC, always first.</summary>
    public const int EffectTypeSlot = 0;

    /// <summary>The macro slot dedicated to blend switching (a Select-mode mapping tagged <see cref="ParamId.Blend"/>)
    /// - the "Blend" row's CC. Zones only: groups have no blend mode of their own.</summary>
    public const int BlendSlot = 1;

    private const int ParamsStartSlot = 2;

    /// <summary>The continuous Set-mode params, in the same top-to-bottom order as the color/modulation/position
    /// rows below the Type and Blend rows.</summary>
    private static readonly ParamId[] Params =
    {
        ParamId.Hue,
        ParamId.Saturation,
        ParamId.Brightness,
        ParamId.EffectIntensity,
        ParamId.EffectSpeed,
        ParamId.EffectDecay,
        ParamId.PosX,
        ParamId.PosY,
    };

    /// <summary>The macro slot that always drives <paramref name="param"/>'s Set-mode mapping.</summary>
    public static int SlotFor(ParamId param) => ParamsStartSlot + Array.IndexOf(Params, param);

    /// <summary>The label the plugin's host parameter for <paramref name="slot"/> is declared with (see
    /// SonolumePlugin.Initialize) - static, baked in at declare time.</summary>
    public static string LabelFor(int slot) => slot switch
    {
        EffectTypeSlot => "Effect Type",
        BlendSlot => "Blend",
        _ => Params[slot - ParamsStartSlot].ToString(),
    };

    public static IEnumerable<Mapping> For(TargetRef target)
    {
        yield return new Mapping
        {
            Id = $"effect-type-{target.Kind}-{target.Id}",
            Source = SourceAddress.HostMacro(EffectTypeSlot),
            Target = target,
            Param = ParamId.EffectIntensity,
            Mode = MappingMode.Select,
            Transform = Transform.Identity,
        };

        // Groups have no blend mode of their own - only a zone's Blend row gets this slot.
        if (target.Kind == TargetKind.Zone)
        {
            yield return new Mapping
            {
                Id = $"blend-{target.Kind}-{target.Id}",
                Source = SourceAddress.HostMacro(BlendSlot),
                Target = target,
                Param = ParamId.Blend,
                Mode = MappingMode.Select,
                Transform = Transform.Identity,
            };
        }

        for (int i = 0; i < Params.Length; i++)
        {
            yield return new Mapping
            {
                Id = $"macro-{ParamsStartSlot + i}-{target.Kind}-{target.Id}",
                Source = SourceAddress.HostMacro(ParamsStartSlot + i),
                Target = target,
                Param = Params[i],
                Mode = MappingMode.Set,
                Transform = Transform.Identity,
            };
        }
    }

    /// <summary>One-time migration for projects saved before this convention existed (a REAPER session's saved
    /// plugin state, an older .sonolume file, ...): if the whole project has zero host-macro mappings anywhere,
    /// seeds every zone and group with the fixed convention. No-op the moment even one HostMacro mapping exists
    /// anywhere, so this never fights a project that's already been migrated. Called on every project load.</summary>
    public static void BackfillIfMissing(Project project)
    {
        if (project.Mappings.Any(m => m.Source.Kind == SourceKind.HostMacro)) return;
        foreach (var zone in project.Zones) project.Mappings.AddRange(For(TargetRef.Zone(zone.Id)));
        foreach (var group in project.Groups) project.Mappings.AddRange(For(TargetRef.Group(group.Id)));
    }
}
