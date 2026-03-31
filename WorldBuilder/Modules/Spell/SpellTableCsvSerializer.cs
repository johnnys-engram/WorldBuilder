using System.Globalization;
using System.Text;
using DatReaderWriter.Enums;
using SpellRow = DatReaderWriter.Types.SpellBase;

namespace WorldBuilder.Modules.Spell;

internal static class SpellTableCsvSerializer {
    internal const int CsvFormatVersion = 1;

    private static readonly string[] HeaderColumns = [
        "formatVersion",
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

    public static string Serialize(IReadOnlyDictionary<uint, SpellRow> spells) {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', HeaderColumns.Select(Escape)));

        foreach (var kvp in spells.OrderBy(x => x.Key)) {
            var d = SpellExportDto.FromSpell(kvp.Key, kvp.Value);
            var row = new[] {
                CsvFormatVersion.ToString(inv),
                d.Id.ToString(inv),
                Escape(d.Name),
                Escape(d.Description),
                Escape(d.School.ToString()),
                Escape(d.MetaSpellType.ToString()),
                Escape(d.Category.ToString()),
                d.Icon.ToString(inv),
                d.BaseMana.ToString(inv),
                d.Power.ToString(inv),
                d.BaseRangeConstant.ToString(inv),
                d.BaseRangeMod.ToString(inv),
                d.SpellEconomyMod.ToString(inv),
                d.FormulaVersion.ToString(inv),
                d.ComponentLoss.ToString(inv),
                d.Bitfield.ToString(inv),
                d.MetaSpellId.ToString(inv),
                d.Duration.ToString(inv),
                d.DegradeModifier.ToString(inv),
                d.DegradeLimit.ToString(inv),
                d.PortalLifetime.ToString(inv),
                Escape(d.CasterEffect.ToString()),
                Escape(d.TargetEffect.ToString()),
                Escape(d.FizzleEffect.ToString()),
                d.RecoveryInterval.ToString(inv),
                d.RecoveryAmount.ToString(inv),
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
        if (header.Length != HeaderColumns.Length)
            throw new FormatException($"Expected {HeaderColumns.Length} columns in header, found {header.Length}.");

        for (int c = 0; c < HeaderColumns.Length; c++) {
            if (!string.Equals(header[c].Trim(), HeaderColumns[c], StringComparison.OrdinalIgnoreCase))
                throw new FormatException($"Unexpected header column {c + 1}: expected '{HeaderColumns[c]}', got '{header[c]}'. Re-export from the spell editor for the correct layout.");
        }

        var spells = new List<SpellExportDto>();
        for (var r = 1; r < rows.Count; r++) {
            var row = rows[r];
            if (IsRowBlank(row))
                continue;
            if (row.Length != HeaderColumns.Length)
                throw new FormatException($"Row {r + 1}: expected {HeaderColumns.Length} columns, found {row.Length}.");

            spells.Add(ParseDataRow(row, r + 1));
        }

        var ids = spells.Select(s => s.Id).ToList();
        if (ids.Count != ids.Distinct().Count())
            throw new FormatException("Duplicate spell IDs in the CSV file.");

        return new SpellTableExportFile {
            FormatVersion = SpellTableImportExport.CurrentFormatVersion,
            Spells = spells,
        };
    }

    private static SpellExportDto ParseDataRow(string[] row, int rowNumber) {
        var inv = CultureInfo.InvariantCulture;
        int fv = int.Parse(row[0].Trim(), inv);
        if (fv != CsvFormatVersion)
            throw new FormatException($"Row {rowNumber}: unsupported formatVersion {fv} (expected {CsvFormatVersion}).");

        var components = new List<uint>();
        foreach (var part in row[29].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            if (!uint.TryParse(part, inv, out var cid))
                throw new FormatException($"Row {rowNumber}: invalid component id '{part}' in components list.");
            components.Add(cid);
        }

        return new SpellExportDto {
            Id = ParseUInt(row[1], "id", rowNumber, inv),
            Name = row[2],
            Description = row[3],
            School = ParseEnum<MagicSchool>(row[4], "school", rowNumber),
            MetaSpellType = ParseEnum<SpellType>(row[5], "metaSpellType", rowNumber),
            Category = ParseEnum<SpellCategory>(row[6], "category", rowNumber),
            Icon = ParseUInt(row[7], "icon", rowNumber, inv),
            BaseMana = ParseUInt(row[8], "baseMana", rowNumber, inv),
            Power = ParseUInt(row[9], "power", rowNumber, inv),
            BaseRangeConstant = ParseFloat(row[10], "baseRangeConstant", rowNumber, inv),
            BaseRangeMod = ParseFloat(row[11], "baseRangeMod", rowNumber, inv),
            SpellEconomyMod = ParseFloat(row[12], "spellEconomyMod", rowNumber, inv),
            FormulaVersion = ParseUInt(row[13], "formulaVersion", rowNumber, inv),
            ComponentLoss = ParseFloat(row[14], "componentLoss", rowNumber, inv),
            Bitfield = ParseUInt(row[15], "bitfield", rowNumber, inv),
            MetaSpellId = ParseUInt(row[16], "metaSpellId", rowNumber, inv),
            Duration = ParseDouble(row[17], "duration", rowNumber, inv),
            DegradeModifier = ParseFloat(row[18], "degradeModifier", rowNumber, inv),
            DegradeLimit = ParseFloat(row[19], "degradeLimit", rowNumber, inv),
            PortalLifetime = ParseDouble(row[20], "portalLifetime", rowNumber, inv),
            CasterEffect = ParseEnum<PlayScript>(row[21], "casterEffect", rowNumber),
            TargetEffect = ParseEnum<PlayScript>(row[22], "targetEffect", rowNumber),
            FizzleEffect = ParseEnum<PlayScript>(row[23], "fizzleEffect", rowNumber),
            RecoveryInterval = ParseDouble(row[24], "recoveryInterval", rowNumber, inv),
            RecoveryAmount = ParseFloat(row[25], "recoveryAmount", rowNumber, inv),
            DisplayOrder = ParseUInt(row[26], "displayOrder", rowNumber, inv),
            NonComponentTargetType = ParseUInt(row[27], "nonComponentTargetType", rowNumber, inv),
            ManaMod = ParseUInt(row[28], "manaMod", rowNumber, inv),
            Components = components,
        };
    }

    private static uint ParseUInt(string s, string col, int row, IFormatProvider inv) {
        if (!uint.TryParse(s.Trim(), NumberStyles.Integer, inv, out var v))
            throw new FormatException($"Row {row}: invalid {col} '{s}'.");
        return v;
    }

    private static float ParseFloat(string s, string col, int row, IFormatProvider inv) {
        if (!float.TryParse(s.Trim(), NumberStyles.Float | NumberStyles.AllowThousands, inv, out var v))
            throw new FormatException($"Row {row}: invalid {col} '{s}'.");
        return v;
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
