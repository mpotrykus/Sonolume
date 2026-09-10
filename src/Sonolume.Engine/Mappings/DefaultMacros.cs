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

    /// <summary>Re-derives every HostMacro mapping from the fixed convention, for every zone and group. Since
    /// the convention is now permanent (nothing in the UI can repoint a param to a different macro), there is no
    /// such thing as a project that has "already migrated" with a different-but-valid layout - so unlike a true
    /// one-time backfill, this discards and regenerates the HostMacro mappings every time rather than skipping
    /// when some already exist. That matters for a project saved under an earlier version of this convention (a
    /// different slot order, or a project from before Select-mode Effect Type/Blend slots existed): without this,
    /// its stale HostMacro mappings would silently keep driving the old params forever while the plugin's host
    /// parameter labels (regenerated fresh from the current convention on every load) claimed otherwise. Called on
    /// every project load.</summary>
    public static void Sync(Project project)
    {
        project.Mappings.RemoveAll(m => m.Source.Kind == SourceKind.HostMacro);
        foreach (var zone in project.Zones) project.Mappings.AddRange(For(TargetRef.Zone(zone.Id)));
        foreach (var group in project.Groups) project.Mappings.AddRange(For(TargetRef.Group(group.Id)));
    }
}
