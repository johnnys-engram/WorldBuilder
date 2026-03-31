using System.Text.Json;
using System.Text.Json.Serialization;
using DatReaderWriter.Enums;

namespace WorldBuilder.Modules.Spell;

internal sealed class SpellJsonCamelCaseEnumConverter<TEnum> : JsonStringEnumConverter<TEnum>
    where TEnum : struct, Enum {
    public SpellJsonCamelCaseEnumConverter() : base(JsonNamingPolicy.CamelCase) { }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    Converters = new Type[] {
        typeof(SpellJsonCamelCaseEnumConverter<MagicSchool>),
        typeof(SpellJsonCamelCaseEnumConverter<SpellType>),
        typeof(SpellJsonCamelCaseEnumConverter<SpellCategory>),
        typeof(SpellJsonCamelCaseEnumConverter<PlayScript>),
    })]
[JsonSerializable(typeof(SpellTableExportFile))]
[JsonSerializable(typeof(SpellExportDto))]
[JsonSerializable(typeof(List<SpellExportDto>))]
[JsonSerializable(typeof(List<uint>))]
[JsonSerializable(typeof(MagicSchool))]
[JsonSerializable(typeof(SpellType))]
[JsonSerializable(typeof(SpellCategory))]
[JsonSerializable(typeof(PlayScript))]
internal partial class SpellTableJsonSerializerContext : JsonSerializerContext {
}
