using System.Globalization;
using System.Linq;
using System.Text;
using DatReaderWriter.Enums;
using WorldBuilder.Lib;
using SpellRow = DatReaderWriter.Types.SpellBase;

namespace WorldBuilder.Modules.Spell;

internal static class SpellTableCsvSerializer {
    /// <summary>Canonical spell column names (export order). Import matches via <see cref="PortalTableCsv.NormalizeColumnKey"/>.</summary>
    private static readonly string[] ExportColumnOrder = [
        "id",
        "name",
        "description",
        "school",
        "metaSpellType",
        "category",
        "icon",
        "baseMana",
        "power",
        "baseRangeConstant",
        "baseRangeMod",
        "spellEconomyMod",
        "formulaVersion",
        "componentLoss",
        "bitfield",
        "metaSpellId",
        "duration",
        "degradeModifier",
        "degradeLimit",
        "portalLifetime",
        "casterEffect",
        "targetEffect",
        "fizzleEffect",
        "recoveryInterval",
        "recoveryAmount",
        "displayOrder",
        "nonComponentTargetType",
        "manaMod",
        "components",
    ];

    /// <summary>CSV import always sets <see cref="SpellExportDto.FormulaVersion"/> to 1; column is optional in the file.</summary>
    private static readonly string[] RequiredImportColumns = ExportColumnOrder.Where(static c => c != "formulaVersion").ToArray();

    public static string Serialize(IReadOnlyDictionary<uint, SpellRow> spells) {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', ExportColumnOrder.Select(PortalTableCsv.Escape)));

        foreach (var kvp in spells.OrderBy(x => x.Key)) {
            var d = SpellExportDto.FromSpell(kvp.Key, kvp.Value);
            var row = new[] {
                d.Id.ToString(inv),
                PortalTableCsv.Escape(d.Name),
                PortalTableCsv.Escape(d.Description),
                PortalTableCsv.Escape(d.School.ToString()),
                PortalTableCsv.Escape(d.MetaSpellType.ToString()),
                PortalTableCsv.Escape(d.Category.ToString()),
                d.Icon.ToString(inv),
                d.BaseMana.ToString(inv),
                d.Power.ToString(inv),
                PortalTableCsv.FormatFloat(d.BaseRangeConstant, inv),
                PortalTableCsv.FormatFloat(d.BaseRangeMod, inv),
                PortalTableCsv.FormatFloat(d.SpellEconomyMod, inv),
                d.FormulaVersion.ToString(inv),
                PortalTableCsv.FormatFloat(d.ComponentLoss, inv),
                d.Bitfield.ToString(inv),
                d.MetaSpellId.ToString(inv),
                PortalTableCsv.FormatDouble(d.Duration, inv),
                PortalTableCsv.FormatFloat(d.DegradeModifier, inv),
                PortalTableCsv.FormatFloat(d.DegradeLimit, inv),
                PortalTableCsv.FormatDouble(d.PortalLifetime, inv),
                PortalTableCsv.Escape(d.CasterEffect.ToString()),
                PortalTableCsv.Escape(d.TargetEffect.ToString()),
                PortalTableCsv.Escape(d.FizzleEffect.ToString()),
                PortalTableCsv.FormatDouble(d.RecoveryInterval, inv),
                PortalTableCsv.FormatFloat(d.RecoveryAmount, inv),
                d.DisplayOrder.ToString(inv),
                d.NonComponentTargetType.ToString(inv),
                d.ManaMod.ToString(inv),
                PortalTableCsv.Escape(string.Join(';', d.Components)),
            };
            sb.AppendLine(string.Join(',', row));
        }

        return sb.ToString();
    }

    public static SpellTableExportFile Parse(string csvText) {
        var text = csvText.TrimStart('\uFEFF');
        var rows = PortalTableCsv.ParseCsv(text);
        if (rows.Count < 1)
            throw new FormatException("CSV is empty.");

        var header = rows[0];
        var colMap = PortalTableCsv.BuildColumnMap(header);

        var missing = RequiredImportColumns
            .Where(c => !colMap.ContainsKey(PortalTableCsv.NormalizeColumnKey(c)))
            .ToList();
        if (missing.Count > 0)
            throw new FormatException(
                "CSV header is missing required spell columns: " + string.Join(", ", missing) + ".");

        var spells = new List<SpellExportDto>();
        for (var r = 1; r < rows.Count; r++) {
            var row = rows[r];
            if (PortalTableCsv.IsRowBlank(row))
                continue;

            spells.Add(ParseDataRow(row, r + 1, colMap));
        }

        var ids = spells.Select(s => s.Id).ToList();
        if (ids.Count != ids.Distinct().Count())
            throw new FormatException("Duplicate spell IDs in the CSV file.");

        return new SpellTableExportFile {
            FormatVersion = SpellTableImportExport.CurrentFormatVersion,
            Spells = spells,
        };
    }

    private static SpellExportDto ParseDataRow(string[] row, int rowNumber, IReadOnlyDictionary<string, int> colMap) {
        var inv = CultureInfo.InvariantCulture;

        var componentsRaw = PortalTableCsv.GetCell(row, colMap, "components");
        var components = new List<uint>();
        foreach (var part in componentsRaw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            if (!uint.TryParse(part, inv, out var cid))
                throw new FormatException($"Row {rowNumber}: invalid component id '{part}' in components list.");
            components.Add(cid);
        }

        return new SpellExportDto {
            Id = PortalTableCsv.ParseUInt(PortalTableCsv.GetCell(row, colMap, "id"), "id", rowNumber, inv),
            Name = PortalTableCsv.GetCell(row, colMap, "name"),
            Description = PortalTableCsv.GetCell(row, colMap, "description"),
            School = PortalTableCsv.ParseEnum<MagicSchool>(PortalTableCsv.GetCell(row, colMap, "school"), "school", rowNumber),
            MetaSpellType = PortalTableCsv.ParseEnum<SpellType>(PortalTableCsv.GetCell(row, colMap, "metaSpellType"), "metaSpellType", rowNumber),
            Category = PortalTableCsv.ParseEnum<SpellCategory>(PortalTableCsv.GetCell(row, colMap, "category"), "category", rowNumber),
            Icon = PortalTableCsv.ParseUInt(PortalTableCsv.GetCell(row, colMap, "icon"), "icon", rowNumber, inv),
            BaseMana = PortalTableCsv.ParseUInt(PortalTableCsv.GetCell(row, colMap, "baseMana"), "baseMana", rowNumber, inv),
            Power = PortalTableCsv.ParseUInt(PortalTableCsv.GetCell(row, colMap, "power"), "power", rowNumber, inv),
            BaseRangeConstant = PortalTableCsv.ParseFloat(PortalTableCsv.GetCell(row, colMap, "baseRangeConstant"), "baseRangeConstant", rowNumber, inv),
            BaseRangeMod = PortalTableCsv.ParseFloat(PortalTableCsv.GetCell(row, colMap, "baseRangeMod"), "baseRangeMod", rowNumber, inv),
            SpellEconomyMod = PortalTableCsv.ParseFloat(PortalTableCsv.GetCell(row, colMap, "spellEconomyMod"), "spellEconomyMod", rowNumber, inv),
            FormulaVersion = 1,
            ComponentLoss = PortalTableCsv.ParseFloat(PortalTableCsv.GetCell(row, colMap, "componentLoss"), "componentLoss", rowNumber, inv),
            Bitfield = PortalTableCsv.ParseUInt(PortalTableCsv.GetCell(row, colMap, "bitfield"), "bitfield", rowNumber, inv),
            MetaSpellId = PortalTableCsv.ParseUInt(PortalTableCsv.GetCell(row, colMap, "metaSpellId"), "metaSpellId", rowNumber, inv),
            Duration = PortalTableCsv.ParseDouble(PortalTableCsv.GetCell(row, colMap, "duration"), "duration", rowNumber, inv),
            DegradeModifier = PortalTableCsv.ParseFloat(PortalTableCsv.GetCell(row, colMap, "degradeModifier"), "degradeModifier", rowNumber, inv),
            DegradeLimit = PortalTableCsv.ParseFloat(PortalTableCsv.GetCell(row, colMap, "degradeLimit"), "degradeLimit", rowNumber, inv),
            PortalLifetime = PortalTableCsv.ParseDouble(PortalTableCsv.GetCell(row, colMap, "portalLifetime"), "portalLifetime", rowNumber, inv),
            CasterEffect = PortalTableCsv.ParseEnum<PlayScript>(PortalTableCsv.GetCell(row, colMap, "casterEffect"), "casterEffect", rowNumber),
            TargetEffect = PortalTableCsv.ParseEnum<PlayScript>(PortalTableCsv.GetCell(row, colMap, "targetEffect"), "targetEffect", rowNumber),
            FizzleEffect = PortalTableCsv.ParseEnum<PlayScript>(PortalTableCsv.GetCell(row, colMap, "fizzleEffect"), "fizzleEffect", rowNumber),
            RecoveryInterval = PortalTableCsv.ParseDouble(PortalTableCsv.GetCell(row, colMap, "recoveryInterval"), "recoveryInterval", rowNumber, inv),
            RecoveryAmount = PortalTableCsv.ParseFloat(PortalTableCsv.GetCell(row, colMap, "recoveryAmount"), "recoveryAmount", rowNumber, inv),
            DisplayOrder = PortalTableCsv.ParseUInt(PortalTableCsv.GetCell(row, colMap, "displayOrder"), "displayOrder", rowNumber, inv),
            NonComponentTargetType = PortalTableCsv.ParseUInt(PortalTableCsv.GetCell(row, colMap, "nonComponentTargetType"), "nonComponentTargetType", rowNumber, inv),
            ManaMod = PortalTableCsv.ParseUInt(PortalTableCsv.GetCell(row, colMap, "manaMod"), "manaMod", rowNumber, inv),
            Components = components,
        };
    }
}
