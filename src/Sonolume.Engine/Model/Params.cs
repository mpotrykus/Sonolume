namespace Sonolume.Engine.Model;

public enum ParamId
{
    Brightness,
    Hue,
    Saturation,
    Active,
    EffectIntensity,
    EffectSpeed,
    EffectDecay,
    PosX,
    PosY,
    PaletteIndex,
    /// <summary>Not a real param - only used to tag a Select-mode mapping as blend-select (see
    /// <see cref="Mappings.MappingMode.Select"/> and <see cref="Engine.Engine.SelectBlend"/>), the same way
    /// <see cref="EffectIntensity"/> tags the effect-type select. Never read through <see cref="ParamSet"/>.</summary>
    Blend,
}

/// <summary>Raw range and default of a parameter. Mappings write normalized 0..1 values that are denormalized into this range.</summary>
public readonly record struct ParamInfo(ParamId Id, float Min, float Max, float Default, string Unit);

public static class ParamInfos
{
    private static readonly ParamInfo[] Table = BuildTable();

    public static int Count => Table.Length;

    public static ParamInfo Of(ParamId id) => Table[(int)id];

    public static IReadOnlyList<ParamInfo> All => Table;

    public static float Denormalize(ParamId id, float x01)
    {
        var info = Of(id);
        return info.Min + Math.Clamp(x01, 0f, 1f) * (info.Max - info.Min);
    }

    public static float Normalize(ParamId id, float raw)
    {
        var info = Of(id);
        return info.Max == info.Min ? 0f : Math.Clamp((raw - info.Min) / (info.Max - info.Min), 0f, 1f);
    }

    private static ParamInfo[] BuildTable()
    {
        var ids = Enum.GetValues<ParamId>();
        var table = new ParamInfo[ids.Length];
        foreach (var id in ids)
        {
            table[(int)id] = id switch
            {
                ParamId.Brightness => new(id, 0f, 1f, 1f, ""),
                ParamId.Hue => new(id, 0f, 1f, 0f, "turns"),
                ParamId.Saturation => new(id, 0f, 1f, 1f, ""),
                ParamId.Active => new(id, 0f, 1f, 1f, ""),
                ParamId.EffectIntensity => new(id, 0f, 1f, 1f, ""),
                ParamId.EffectSpeed => new(id, 0f, 1f, 0.5f, ""),
                ParamId.EffectDecay => new(id, 0.01f, 5f, 0.25f, "s"),
                ParamId.PosX => new(id, 0f, 1f, 0.5f, ""),
                ParamId.PosY => new(id, 0f, 1f, 0.5f, ""),
                ParamId.PaletteIndex => new(id, 0f, 1f, 0f, ""),
                ParamId.Blend => new(id, 0f, 1f, 0f, ""),
                _ => throw new InvalidOperationException($"No ParamInfo for {id}"),
            };
        }
        return table;
    }
}

/// <summary>Persistent parameter values of a zone or group, stored in raw units.</summary>
public sealed class ParamSet
{
    private readonly float[] values;

    public ParamSet()
    {
        values = new float[ParamInfos.Count];
        foreach (var info in ParamInfos.All) values[(int)info.Id] = info.Default;
    }

    public float this[ParamId id]
    {
        get => values[(int)id];
        set
        {
            var info = ParamInfos.Of(id);
            values[(int)id] = Math.Clamp(value, info.Min, info.Max);
        }
    }

    public float GetNormalized(ParamId id) => ParamInfos.Normalize(id, this[id]);

    public void SetNormalized(ParamId id, float x01) => this[id] = ParamInfos.Denormalize(id, x01);

    public bool IsDefault(ParamId id) => this[id] == ParamInfos.Of(id).Default;

    public ParamSet Clone()
    {
        var copy = new ParamSet();
        values.CopyTo(copy.values, 0);
        return copy;
    }

    public IEnumerable<KeyValuePair<ParamId, float>> Entries
    {
        get
        {
            foreach (var info in ParamInfos.All) yield return new(info.Id, values[(int)info.Id]);
        }
    }
}
