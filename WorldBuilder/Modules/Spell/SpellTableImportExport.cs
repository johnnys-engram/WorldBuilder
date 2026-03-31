using System.Text.Json;
using DatReaderWriter.Enums;
using SpellItemType = DatReaderWriter.Enums.ItemType;
using SpellRow = DatReaderWriter.Types.SpellBase;

namespace WorldBuilder.Modules.Spell;

internal static class SpellTableImportExport {
    public const int CurrentFormatVersion = 1;
}

internal sealed class SpellTableExportFile {
    public int FormatVersion { get; set; } = SpellTableImportExport.CurrentFormatVersion;
    public List<SpellExportDto> Spells { get; set; } = new();
}

internal sealed class SpellExportDto {
    public uint Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public MagicSchool School { get; set; }
    public SpellType MetaSpellType { get; set; }
    public SpellCategory Category { get; set; }
    public uint Icon { get; set; }
    public uint BaseMana { get; set; }
    public uint Power { get; set; }
    public float BaseRangeConstant { get; set; }
    public float BaseRangeMod { get; set; }
    public float SpellEconomyMod { get; set; }
    public uint FormulaVersion { get; set; }
    public float ComponentLoss { get; set; }
    public uint Bitfield { get; set; }
    public uint MetaSpellId { get; set; }
    public double Duration { get; set; }
    public float DegradeModifier { get; set; }
    public float DegradeLimit { get; set; }
    public double PortalLifetime { get; set; }
    public PlayScript CasterEffect { get; set; }
    public PlayScript TargetEffect { get; set; }
    public PlayScript FizzleEffect { get; set; }
    public double RecoveryInterval { get; set; }
    public float RecoveryAmount { get; set; }
    public uint DisplayOrder { get; set; }
    public uint NonComponentTargetType { get; set; }
    public uint ManaMod { get; set; }
    public List<uint> Components { get; set; } = new();

    public static SpellExportDto FromSpell(uint id, SpellRow s) => new() {
        Id = id,
        Name = s.Name?.ToString() ?? "",
        Description = s.Description?.ToString() ?? "",
        School = s.School,
        MetaSpellType = s.MetaSpellType,
        Category = s.Category,
        Icon = s.Icon,
        BaseMana = s.BaseMana,
        Power = s.Power,
        BaseRangeConstant = s.BaseRangeConstant,
        BaseRangeMod = s.BaseRangeMod,
        SpellEconomyMod = s.SpellEconomyMod,
        FormulaVersion = s.FormulaVersion,
        ComponentLoss = s.ComponentLoss,
        Bitfield = (uint)s.Bitfield,
        MetaSpellId = s.MetaSpellId,
        Duration = s.Duration,
        DegradeModifier = s.DegradeModifier,
        DegradeLimit = s.DegradeLimit,
        PortalLifetime = s.PortalLifetime,
        CasterEffect = s.CasterEffect,
        TargetEffect = s.TargetEffect,
        FizzleEffect = s.FizzleEffect,
        RecoveryInterval = s.RecoveryInterval,
        RecoveryAmount = s.RecoveryAmount,
        DisplayOrder = s.DisplayOrder,
        NonComponentTargetType = (uint)s.NonComponentTargetType,
        ManaMod = s.ManaMod,
        Components = s.Components != null ? new List<uint>(s.Components) : new List<uint>(),
    };

    public SpellRow ToSpell() => new() {
        Name = Name,
        Description = Description,
        School = School,
        MetaSpellType = MetaSpellType,
        Category = Category,
        Icon = Icon,
        BaseMana = BaseMana,
        Power = Power,
        BaseRangeConstant = BaseRangeConstant,
        BaseRangeMod = BaseRangeMod,
        SpellEconomyMod = SpellEconomyMod,
        FormulaVersion = FormulaVersion,
        ComponentLoss = ComponentLoss,
        Bitfield = (SpellIndex)Bitfield,
        MetaSpellId = MetaSpellId,
        Duration = Duration,
        DegradeModifier = DegradeModifier,
        DegradeLimit = DegradeLimit,
        PortalLifetime = PortalLifetime,
        CasterEffect = CasterEffect,
        TargetEffect = TargetEffect,
        FizzleEffect = FizzleEffect,
        RecoveryInterval = RecoveryInterval,
        RecoveryAmount = RecoveryAmount,
        DisplayOrder = DisplayOrder,
        NonComponentTargetType = (SpellItemType)NonComponentTargetType,
        ManaMod = ManaMod,
        Components = Components is { Count: > 0 } ? new List<uint>(Components) : new List<uint>(),
    };
}

internal static class SpellTableJsonSerializer {
    public static string Serialize(SpellTableExportFile file) =>
        JsonSerializer.Serialize(file, SpellTableJsonSerializerContext.Default.SpellTableExportFile);

    public static SpellTableExportFile Deserialize(string json) =>
        JsonSerializer.Deserialize(json, SpellTableJsonSerializerContext.Default.SpellTableExportFile)
        ?? throw new JsonException("Root JSON value was null.");

    public static SpellTableExportFile FromSpells(Dictionary<uint, SpellRow> spells) => new() {
        FormatVersion = SpellTableImportExport.CurrentFormatVersion,
        Spells = spells.OrderBy(kvp => kvp.Key).Select(kvp => SpellExportDto.FromSpell(kvp.Key, kvp.Value)).ToList(),
    };

    public static void ReplaceSpellTable(DatReaderWriter.DBObjs.SpellTable table, SpellTableExportFile file) {
        var rows = file.Spells ?? new List<SpellExportDto>();
        table.Spells.Clear();
        foreach (var dto in rows.OrderBy(s => s.Id))
            table.Spells[dto.Id] = dto.ToSpell();
    }
}
