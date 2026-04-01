using System.Globalization;
using System.Text;
using DatReaderWriter.Enums;
using SpellRow = DatReaderWriter.Types.SpellBase;

namespace WorldBuilder.Modules.Spell;

internal static class SpellTableCsvSerializer {
    /// <summary>Canonical spell column names (export order). Import matches via <see cref="NormalizeSpellColumnKey"/>.</summary>
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

    private static string FormatFloat(float v, IFormatProvider inv) => v.ToString("G9", inv);

    private static string FormatDouble(double v, IFormatProvider inv) => v.ToString("G17", inv);

    /// <summary>
    /// Maps headers like <c>Degrade Limit</c>, <c>degrade_limit</c>, and <c>degradeLimit</c> to the same key so values
    /// land in the correct field (mis-labeled columns often produced huge bogus floats).
    /// </summary>
    private static string NormalizeSpellColumnKey(string raw) {
        var s = raw.Trim();
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s) {
            if (ch is '_' or ' ' or '\t' or '-')
                continue;
            sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString();
    }

    public static string Serialize(IReadOnlyDictionary<uint, SpellRow> spells) {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', ExportColumnOrder.Select(Escape)));

        foreach (var kvp in spells.OrderBy(x => x.Key)) {
            var d = SpellExportDto.FromSpell(kvp.Key, kvp.Value);
            var row = new[] {
                d.Id.ToString(inv),
                Escape(d.Name),
                Escape(d.Description),
                Escape(d.School.ToString()),
                Escape(d.MetaSpellType.ToString()),
                Escape(d.Category.ToString()),
                d.Icon.ToString(inv),
                d.BaseMana.ToString(inv),
                d.Power.ToString(inv),
                FormatFloat(d.BaseRangeConstant, inv),
                FormatFloat(d.BaseRangeMod, inv),
                FormatFloat(d.SpellEconomyMod, inv),
                d.FormulaVersion.ToString(inv),
                FormatFloat(d.ComponentLoss, inv),
                d.Bitfield.ToString(inv),
                d.MetaSpellId.ToString(inv),
                FormatDouble(d.Duration, inv),
                FormatFloat(d.DegradeModifier, inv),
                FormatFloat(d.DegradeLimit, inv),
                FormatDouble(d.PortalLifetime, inv),
                Escape(d.CasterEffect.ToString()),
                Escape(d.TargetEffect.ToString()),
                Escape(d.FizzleEffect.ToString()),
                FormatDouble(d.RecoveryInterval, inv),
                FormatFloat(d.RecoveryAmount, inv),
                d.DisplayOrder.ToString(inv),
                d.NonComponentTargetType.ToString(inv),
                d.ManaMod.ToString(inv),
                Escape(string.Join(';', d.Components)),
            };
            sb.AppendLine(string.Join(',', row));
        }

        return sb.ToString();
    }

    public static SpellTableExportFile Parse(string csvText) {
        var text = csvText.TrimStart('\uFEFF');
        var rows = ParseCsv(text);
        if (rows.Count < 1)
            throw new FormatException("CSV is empty.");

        var header = rows[0];
        var colMap = BuildColumnMap(header);

        var missing = RequiredImportColumns
            .Where(c => !colMap.ContainsKey(NormalizeSpellColumnKey(c)))
            .ToList();
        if (missing.Count > 0)
            throw new FormatException(
                "CSV header is missing required spell columns: " + string.Join(", ", missing) + ".");

        var spells = new List<SpellExportDto>();
        for (var r = 1; r < rows.Count; r++) {
            var row = rows[r];
            if (IsRowBlank(row))
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

    private static Dictionary<string, int> BuildColumnMap(string[] header) {
        var colMap = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var c = 0; c < header.Length; c++) {
            var name = header[c].Trim();
            if (string.IsNullOrEmpty(name))
                throw new FormatException($"Header column {c + 1} is empty; remove extra commas or name every column.");
            var key = NormalizeSpellColumnKey(name);
            if (key.Length == 0)
                throw new FormatException($"Header column {c + 1} has no usable name after normalizing '{name}'.");
            if (colMap.ContainsKey(key))
                throw new FormatException(
                    $"Duplicate header column '{name}' (normalized as '{key}'). Each logical spell column may appear once.");
            colMap[key] = c;
        }

        return colMap;
    }

    private static string GetCell(string[] row, IReadOnlyDictionary<string, int> colMap, string logicalColumnName) {
        var key = NormalizeSpellColumnKey(logicalColumnName);
        if (!colMap.TryGetValue(key, out var idx))
            throw new InvalidOperationException($"Column '{logicalColumnName}' missing from map after validation.");
        return idx < row.Length ? row[idx] : "";
    }

    private static SpellExportDto ParseDataRow(string[] row, int rowNumber, IReadOnlyDictionary<string, int> colMap) {
        var inv = CultureInfo.InvariantCulture;

        var componentsRaw = GetCell(row, colMap, "components");
        var components = new List<uint>();
        foreach (var part in componentsRaw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            if (!uint.TryParse(part, inv, out var cid))
                throw new FormatException($"Row {rowNumber}: invalid component id '{part}' in components list.");
            components.Add(cid);
        }

        return new SpellExportDto {
            Id = ParseUInt(GetCell(row, colMap, "id"), "id", rowNumber, inv),
            Name = GetCell(row, colMap, "name"),
            Description = GetCell(row, colMap, "description"),
            School = ParseEnum<MagicSchool>(GetCell(row, colMap, "school"), "school", rowNumber),
            MetaSpellType = ParseEnum<SpellType>(GetCell(row, colMap, "metaSpellType"), "metaSpellType", rowNumber),
            Category = ParseEnum<SpellCategory>(GetCell(row, colMap, "category"), "category", rowNumber),
            Icon = ParseUInt(GetCell(row, colMap, "icon"), "icon", rowNumber, inv),
            BaseMana = ParseUInt(GetCell(row, colMap, "baseMana"), "baseMana", rowNumber, inv),
            Power = ParseUInt(GetCell(row, colMap, "power"), "power", rowNumber, inv),
            BaseRangeConstant = ParseFloat(GetCell(row, colMap, "baseRangeConstant"), "baseRangeConstant", rowNumber, inv),
            BaseRangeMod = ParseFloat(GetCell(row, colMap, "baseRangeMod"), "baseRangeMod", rowNumber, inv),
            SpellEconomyMod = ParseFloat(GetCell(row, colMap, "spellEconomyMod"), "spellEconomyMod", rowNumber, inv),
            FormulaVersion = 1,
            ComponentLoss = ParseFloat(GetCell(row, colMap, "componentLoss"), "componentLoss", rowNumber, inv),
            Bitfield = ParseUInt(GetCell(row, colMap, "bitfield"), "bitfield", rowNumber, inv),
            MetaSpellId = ParseUInt(GetCell(row, colMap, "metaSpellId"), "metaSpellId", rowNumber, inv),
            Duration = ParseDouble(GetCell(row, colMap, "duration"), "duration", rowNumber, inv),
            DegradeModifier = ParseFloat(GetCell(row, colMap, "degradeModifier"), "degradeModifier", rowNumber, inv),
            DegradeLimit = ParseFloat(GetCell(row, colMap, "degradeLimit"), "degradeLimit", rowNumber, inv),
            PortalLifetime = ParseDouble(GetCell(row, colMap, "portalLifetime"), "portalLifetime", rowNumber, inv),
            CasterEffect = ParseEnum<PlayScript>(GetCell(row, colMap, "casterEffect"), "casterEffect", rowNumber),
            TargetEffect = ParseEnum<PlayScript>(GetCell(row, colMap, "targetEffect"), "targetEffect", rowNumber),
            FizzleEffect = ParseEnum<PlayScript>(GetCell(row, colMap, "fizzleEffect"), "fizzleEffect", rowNumber),
            RecoveryInterval = ParseDouble(GetCell(row, colMap, "recoveryInterval"), "recoveryInterval", rowNumber, inv),
            RecoveryAmount = ParseFloat(GetCell(row, colMap, "recoveryAmount"), "recoveryAmount", rowNumber, inv),
            DisplayOrder = ParseUInt(GetCell(row, colMap, "displayOrder"), "displayOrder", rowNumber, inv),
            NonComponentTargetType = ParseUInt(GetCell(row, colMap, "nonComponentTargetType"), "nonComponentTargetType", rowNumber, inv),
            ManaMod = ParseUInt(GetCell(row, colMap, "manaMod"), "manaMod", rowNumber, inv),
            Components = components,
        };
    }

    private static uint ParseUInt(string s, string col, int row, IFormatProvider inv) {
        if (!uint.TryParse(s.Trim(), NumberStyles.Integer, inv, out var v))
            throw new FormatException($"Row {row}: invalid {col} '{s}'.");
        return v;
    }

    private static float ParseFloat(string s, string col, int row, IFormatProvider inv) {
        var t = s.Trim();
        if (!double.TryParse(t, NumberStyles.Float | NumberStyles.AllowThousands, inv, out var d))
            throw new FormatException($"Row {row}: invalid {col} '{s}'.");
        return (float)d;
    }

    private static double ParseDouble(string s, string col, int row, IFormatProvider inv) {
        if (!double.TryParse(s.Trim(), NumberStyles.Float | NumberStyles.AllowThousands, inv, out var v))
            throw new FormatException($"Row {row}: invalid {col} '{s}'.");
        return v;
    }

    private static TEnum ParseEnum<TEnum>(string s, string col, int row) where TEnum : struct, Enum {
        var t = s.Trim();
        if (Enum.TryParse(t, ignoreCase: true, out TEnum v))
            return v;
        if (Enum.TryParse(t.Replace(" ", "", StringComparison.Ordinal), ignoreCase: true, out v))
            return v;
        throw new FormatException($"Row {row}: invalid {col} '{s}'.");
    }

    private static bool IsRowBlank(string[] row) => row.All(static c => string.IsNullOrWhiteSpace(c));

    private static string Escape(string? value) {
        var s = value ?? "";
        if (s.Length == 0)
            return "";

        if (s.AsSpan().IndexOfAny("\",\r\n".AsSpan()) < 0)
            return s;

        return '"' + s.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }

    private static List<string[]> ParseCsv(string text) {
        var rows = new List<string[]>();
        var rowFields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++) {
            var c = text[i];
            if (inQuotes) {
                if (c == '"') {
                    if (i + 1 < text.Length && text[i + 1] == '"') {
                        field.Append('"');
                        i++;
                    }
                    else {
                        inQuotes = false;
                    }
                }
                else {
                    field.Append(c);
                }
            }
            else {
                switch (c) {
                    case '"':
                        inQuotes = true;
                        break;
                    case ',':
                        rowFields.Add(field.ToString());
                        field.Clear();
                        break;
                    case '\r':
                        break;
                    case '\n':
                        rowFields.Add(field.ToString());
                        field.Clear();
                        rows.Add(rowFields.ToArray());
                        rowFields.Clear();
                        break;
                    default:
                        field.Append(c);
                        break;
                }
            }
        }

        if (field.Length > 0 || rowFields.Count > 0) {
            rowFields.Add(field.ToString());
            rows.Add(rowFields.ToArray());
        }

        return rows;
    }
}
