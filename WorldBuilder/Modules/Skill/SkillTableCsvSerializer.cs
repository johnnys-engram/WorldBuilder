using System.Globalization;
using System.Linq;
using System.Text;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using WorldBuilder.Lib;

namespace WorldBuilder.Modules.Skill;

internal static class SkillTableCsvSerializer {
    private static readonly string[] ExportColumnOrder = [
        "id",
        "name",
        "description",
        "iconId",
        "trainedCost",
        "specializedCost",
        "category",
        "chargenUse",
        "minLevel",
        "upperBound",
        "lowerBound",
        "learnMod",
        "formulaDivisor",
        "formulaAttribute1",
        "formulaAttribute2",
        "formulaUseFormula",
        "formulaHasSecondAttribute",
        "formulaAdditiveBonus",
    ];

    private static readonly string[] RequiredImportColumns = ExportColumnOrder;

    public static string Serialize(IReadOnlyDictionary<SkillId, SkillBase> skills) {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', ExportColumnOrder.Select(PortalTableCsv.Escape)));

        foreach (var kvp in skills.OrderBy(x => (int)x.Key)) {
            var d = SkillExportDto.FromSkill(kvp.Key, kvp.Value);
            var attr2Int = d.FormulaHasSecondAttribute && d.FormulaAttribute2.HasValue
                ? (int)d.FormulaAttribute2.Value
                : 0;
            var row = new[] {
                d.Id.ToString(inv),
                PortalTableCsv.Escape(d.Name),
                PortalTableCsv.Escape(d.Description),
                d.IconId.ToString(inv),
                d.TrainedCost.ToString(inv),
                d.SpecializedCost.ToString(inv),
                ((int)d.Category).ToString(inv),
                d.ChargenUse ? "true" : "false",
                d.MinLevel.ToString(inv),
                PortalTableCsv.FormatDouble(d.UpperBound, inv),
                PortalTableCsv.FormatDouble(d.LowerBound, inv),
                PortalTableCsv.FormatDouble(d.LearnMod, inv),
                d.FormulaDivisor.ToString(inv),
                ((int)d.FormulaAttribute1).ToString(inv),
                attr2Int.ToString(inv),
                d.FormulaUseFormula ? "true" : "false",
                d.FormulaHasSecondAttribute ? "true" : "false",
                d.FormulaAdditiveBonus.ToString(inv),
            };
            sb.AppendLine(string.Join(',', row));
        }

        return sb.ToString();
    }

    public static SkillTableExportFile Parse(string csvText) {
        var text = csvText.TrimStart('\uFEFF');
        var rows = PortalTableCsv.ParseCsv(text);
        if (rows.Count < 1)
            throw new FormatException("CSV is empty.");

        var colMap = PortalTableCsv.BuildColumnMap(rows[0]);

        var missing = RequiredImportColumns
            .Where(c => !colMap.ContainsKey(PortalTableCsv.NormalizeColumnKey(c)))
            .ToList();
        if (missing.Count > 0)
            throw new FormatException(
                "CSV header is missing required skill columns: " + string.Join(", ", missing) + ".");

        var list = new List<SkillExportDto>();
        for (var r = 1; r < rows.Count; r++) {
            var row = rows[r];
            if (PortalTableCsv.IsRowBlank(row))
                continue;
            list.Add(ParseDataRow(row, r + 1, colMap));
        }

        var ids = list.Select(s => s.Id).ToList();
        if (ids.Count != ids.Distinct().Count())
            throw new FormatException("Duplicate skill IDs in the CSV file.");

        return new SkillTableExportFile {
            FormatVersion = SkillTableImportExport.CurrentFormatVersion,
            Skills = list,
        };
    }

    private static SkillExportDto ParseDataRow(string[] row, int rowNumber, IReadOnlyDictionary<string, int> colMap) {
        var inv = CultureInfo.InvariantCulture;

        var hasSecond = PortalTableCsv.ParseBool(
            PortalTableCsv.GetCell(row, colMap, "formulaHasSecondAttribute"), "formulaHasSecondAttribute", rowNumber);
        var attr2Raw = PortalTableCsv.GetCell(row, colMap, "formulaAttribute2").Trim();
        var attr2Int = PortalTableCsv.ParseInt(
            string.IsNullOrEmpty(attr2Raw) ? "0" : attr2Raw, "formulaAttribute2", rowNumber, inv);
        AttributeId? attr2 = hasSecond ? (AttributeId)attr2Int : null;

        var categoryInt = PortalTableCsv.ParseInt(PortalTableCsv.GetCell(row, colMap, "category"), "category", rowNumber, inv);
        var attr1Int = PortalTableCsv.ParseInt(PortalTableCsv.GetCell(row, colMap, "formulaAttribute1"), "formulaAttribute1", rowNumber, inv);

        return new SkillExportDto {
            Id = PortalTableCsv.ParseInt(PortalTableCsv.GetCell(row, colMap, "id"), "id", rowNumber, inv),
            Name = PortalTableCsv.GetCell(row, colMap, "name"),
            Description = PortalTableCsv.GetCell(row, colMap, "description"),
            IconId = PortalTableCsv.ParseUInt(PortalTableCsv.GetCell(row, colMap, "iconId"), "iconId", rowNumber, inv),
            TrainedCost = PortalTableCsv.ParseInt(PortalTableCsv.GetCell(row, colMap, "trainedCost"), "trainedCost", rowNumber, inv),
            SpecializedCost = PortalTableCsv.ParseInt(PortalTableCsv.GetCell(row, colMap, "specializedCost"), "specializedCost", rowNumber, inv),
            Category = (SkillCategory)categoryInt,
            ChargenUse = PortalTableCsv.ParseBool(PortalTableCsv.GetCell(row, colMap, "chargenUse"), "chargenUse", rowNumber),
            MinLevel = PortalTableCsv.ParseUInt(PortalTableCsv.GetCell(row, colMap, "minLevel"), "minLevel", rowNumber, inv),
            UpperBound = PortalTableCsv.ParseDouble(PortalTableCsv.GetCell(row, colMap, "upperBound"), "upperBound", rowNumber, inv),
            LowerBound = PortalTableCsv.ParseDouble(PortalTableCsv.GetCell(row, colMap, "lowerBound"), "lowerBound", rowNumber, inv),
            LearnMod = PortalTableCsv.ParseDouble(PortalTableCsv.GetCell(row, colMap, "learnMod"), "learnMod", rowNumber, inv),
            FormulaDivisor = PortalTableCsv.ParseInt(PortalTableCsv.GetCell(row, colMap, "formulaDivisor"), "formulaDivisor", rowNumber, inv),
            FormulaAttribute1 = (AttributeId)attr1Int,
            FormulaAttribute2 = attr2,
            FormulaUseFormula = PortalTableCsv.ParseBool(PortalTableCsv.GetCell(row, colMap, "formulaUseFormula"), "formulaUseFormula", rowNumber),
            FormulaHasSecondAttribute = hasSecond,
            FormulaAdditiveBonus = PortalTableCsv.ParseInt(PortalTableCsv.GetCell(row, colMap, "formulaAdditiveBonus"), "formulaAdditiveBonus", rowNumber, inv),
        };
    }
}
