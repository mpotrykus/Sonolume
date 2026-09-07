using System.Text.Json;
using System.Text.Json.Serialization;
using Sonolume.Engine.Core;
using Sonolume.Engine.Model;

namespace Sonolume.Persist;

/// <summary>Project (preset) JSON. Human-readable, schema-versioned, stable across engine refactors.</summary>
public static class ProjectJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            new ParamSetConverter(),
            new Rgb8Converter(),
        },
    };

    public static string Serialize(Project project) => JsonSerializer.Serialize(project, Options);

    public static Project Deserialize(string json)
    {
        var project = JsonSerializer.Deserialize<Project>(json, Options)
            ?? throw new InvalidDataException("Project JSON is empty.");
        if (project.SchemaVersion > Project.CurrentSchemaVersion)
            throw new InvalidDataException($"Project schema {project.SchemaVersion} is newer than supported {Project.CurrentSchemaVersion}.");
        Validate(project);
        return project;
    }

    private static void Validate(Project project)
    {
        var zoneIds = new HashSet<string>();
        foreach (var z in project.Zones)
        {
            if (string.IsNullOrWhiteSpace(z.Id)) throw new InvalidDataException("A zone has no id.");
            if (!zoneIds.Add(z.Id)) throw new InvalidDataException($"Duplicate zone id '{z.Id}'.");
        }
        var groupIds = new HashSet<string>();
        foreach (var g in project.Groups)
        {
            if (string.IsNullOrWhiteSpace(g.Id)) throw new InvalidDataException("A group has no id.");
            if (!groupIds.Add(g.Id)) throw new InvalidDataException($"Duplicate group id '{g.Id}'.");
        }
        foreach (var m in project.Mappings)
        {
            bool known = m.Target.Kind == Engine.Mappings.TargetKind.Zone ? zoneIds.Contains(m.Target.Id) : groupIds.Contains(m.Target.Id);
            if (!known) throw new InvalidDataException($"Mapping '{m.Id}' targets unknown {m.Target}.");
        }
    }

    /// <summary>Serializes only non-default parameters as {"hue": 0.5}. Raw units.</summary>
    private sealed class ParamSetConverter : JsonConverter<ParamSet>
    {
        public override ParamSet Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var set = new ParamSet();
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected object for params.");
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                string name = reader.GetString() ?? throw new JsonException();
                reader.Read();
                if (Enum.TryParse<ParamId>(name, ignoreCase: true, out var id))
                    set[id] = reader.GetSingle();
            }
            return set;
        }

        public override void Write(Utf8JsonWriter writer, ParamSet value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var (id, raw) in value.Entries)
            {
                if (value.IsDefault(id)) continue;
                writer.WriteNumber(JsonNamingPolicy.CamelCase.ConvertName(id.ToString()), MathF.Round(raw, 5));
            }
            writer.WriteEndObject();
        }
    }

    private sealed class Rgb8Converter : JsonConverter<Rgb8>
    {
        public override Rgb8 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.GetString() ?? "";
            return Rgb8.TryParseHex(text, out var c) ? c : throw new JsonException($"Bad color '{text}'.");
        }

        public override void Write(Utf8JsonWriter writer, Rgb8 value, JsonSerializerOptions options) =>
            writer.WriteStringValue("#" + value.ToHex());
    }
}
