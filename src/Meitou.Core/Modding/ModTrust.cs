using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meitou.Modding;

/// <summary>What a manifest's code is trusted to do. Native plugins are not a tier; they are unsupported.</summary>
[JsonConverter(typeof(ModTrustJsonConverter))]
public enum ModTrust { Data, Script, Plugin }

/// <summary>Reads and writes the lowercase manifest spellings <c>data</c>, <c>script</c> and <c>plugin</c>.</summary>
public sealed class ModTrustJsonConverter : JsonConverter<ModTrust>
{
    public override ModTrust Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is not JsonTokenType.String)
            throw new JsonException("Mod trust must be a string: data, script or plugin.");
        string? text = reader.GetString();
        return text switch
        {
            "data" => ModTrust.Data,
            "script" => ModTrust.Script,
            "plugin" => ModTrust.Plugin,
            _ => throw new JsonException($"Unknown mod trust '{text}'; expected data, script or plugin."),
        };
    }

    public override void Write(Utf8JsonWriter writer, ModTrust value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            ModTrust.Data => "data",
            ModTrust.Script => "script",
            ModTrust.Plugin => "plugin",
            _ => "data",
        });
}
