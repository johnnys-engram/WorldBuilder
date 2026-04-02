using System.Globalization;
using System.Text;

namespace WorldBuilder.Lib;

/// <summary>Shared CSV parsing/escaping for portal table import/export.</summary>
internal static class PortalTableCsv {
    public static string NormalizeColumnKey(string raw) {
        var s = raw.Trim();
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s) {
            if (ch is '_' or ' ' or '\t' or '-')
                continue;
            sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString();
    }

    public static string FormatFloat(float v, IFormatProvider inv) => v.ToString("G9", inv);

    public static string FormatDouble(double v, IFormatProvider inv) => v.ToString("G17", inv);

    public static List<string[]> ParseCsv(string text) {
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

    public static bool IsRowBlank(string[] row) => row.All(static c => string.IsNullOrWhiteSpace(c));

    public static string Escape(string? value) {
        var s = value ?? "";
        if (s.Length == 0)
            return "";

        if (s.AsSpan().IndexOfAny("\",\r\n".AsSpan()) < 0)
            return s;

        return '"' + s.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }

    public static Dictionary<string, int> BuildColumnMap(string[] header) {
        var colMap = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var c = 0; c < header.Length; c++) {
            var name = header[c].Trim();
            if (string.IsNullOrEmpty(name))
                throw new FormatException($"Header column {c + 1} is empty; remove extra commas or name every column.");
            var key = NormalizeColumnKey(name);
            if (key.Length == 0)
                throw new FormatException($"Header column {c + 1} has no usable name after normalizing '{name}'.");
            if (colMap.ContainsKey(key))
                throw new FormatException(
                    $"Duplicate header column '{name}' (normalized as '{key}'). Each logical column may appear once.");
            colMap[key] = c;
        }

        return colMap;
    }

    public static string GetCell(string[] row, IReadOnlyDictionary<string, int> colMap, string logicalColumnName) {
        var key = NormalizeColumnKey(logicalColumnName);
        if (!colMap.TryGetValue(key, out var idx))
            throw new InvalidOperationException($"Column '{logicalColumnName}' missing from map after validation.");
        return idx < row.Length ? row[idx] : "";
    }

    public static uint ParseUInt(string s, string col, int row, IFormatProvider inv) {
        if (!uint.TryParse(s.Trim(), NumberStyles.Integer, inv, out var v))
            throw new FormatException($"Row {row}: invalid {col} '{s}'.");
        return v;
    }

    public static ulong ParseULong(string s, string col, int row, IFormatProvider inv) {
        if (!ulong.TryParse(s.Trim(), NumberStyles.Integer, inv, out var v))
            throw new FormatException($"Row {row}: invalid {col} '{s}'.");
        return v;
    }

    public static int ParseInt(string s, string col, int row, IFormatProvider inv) {
        if (!int.TryParse(s.Trim(), NumberStyles.Integer, inv, out var v))
            throw new FormatException($"Row {row}: invalid {col} '{s}'.");
        return v;
    }

    public static float ParseFloat(string s, string col, int row, IFormatProvider inv) {
        var t = s.Trim();
        if (!double.TryParse(t, NumberStyles.Float | NumberStyles.AllowThousands, inv, out var d))
            throw new FormatException($"Row {row}: invalid {col} '{s}'.");
        return (float)d;
    }

    public static double ParseDouble(string s, string col, int row, IFormatProvider inv) {
        if (!double.TryParse(s.Trim(), NumberStyles.Float | NumberStyles.AllowThousands, inv, out var v))
            throw new FormatException($"Row {row}: invalid {col} '{s}'.");
        return v;
    }

    public static TEnum ParseEnum<TEnum>(string s, string col, int row) where TEnum : struct, Enum {
        var t = s.Trim();
        if (Enum.TryParse(t, ignoreCase: true, out TEnum v))
            return v;
        if (Enum.TryParse(t.Replace(" ", "", StringComparison.Ordinal), ignoreCase: true, out v))
            return v;
        throw new FormatException($"Row {row}: invalid {col} '{s}'.");
    }

    public static bool ParseBool(string s, string col, int row) {
        var t = s.Trim();
        if (bool.TryParse(t, out var b))
            return b;
        if (t.Equals("1", StringComparison.OrdinalIgnoreCase) || t.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || t.Equals("true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (t.Equals("0", StringComparison.OrdinalIgnoreCase) || t.Equals("no", StringComparison.OrdinalIgnoreCase)
            || t.Equals("false", StringComparison.OrdinalIgnoreCase))
            return false;
        throw new FormatException($"Row {row}: invalid {col} '{s}'.");
    }
}
