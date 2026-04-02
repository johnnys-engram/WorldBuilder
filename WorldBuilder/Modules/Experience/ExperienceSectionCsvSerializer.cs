using System.Globalization;
using System.Linq;
using System.Text;
using WorldBuilder.Lib;

namespace WorldBuilder.Modules.Experience;

internal enum ExperienceCsvSection {
    Levels = 0,
    Attributes = 1,
    Vitals = 2,
    TrainedSkills = 3,
    SpecializedSkills = 4,
}

internal static class ExperienceSectionCsvSerializer {
    private static readonly string[] LevelColumns = ["level", "xpRequired", "skillCredits"];
    private static readonly string[] RankColumns = ["index", "xp"];

    internal static bool TryGetSection(int selectedTabIndex, out ExperienceCsvSection section) {
        if (selectedTabIndex < 0 || selectedTabIndex > 4) {
            section = default;
            return false;
        }

        section = (ExperienceCsvSection)selectedTabIndex;
        return true;
    }

    internal static string SectionFileStem(ExperienceCsvSection section) => section switch {
        ExperienceCsvSection.Levels => "experience-levels",
        ExperienceCsvSection.Attributes => "experience-attributes",
        ExperienceCsvSection.Vitals => "experience-vitals",
        ExperienceCsvSection.TrainedSkills => "experience-trained-skills",
        ExperienceCsvSection.SpecializedSkills => "experience-specialized-skills",
        _ => "experience-section",
    };

    internal static string SectionDisplayName(ExperienceCsvSection section) => section switch {
        ExperienceCsvSection.Levels => "Levels (XP per level & skill credits)",
        ExperienceCsvSection.Attributes => "Attribute rank XP costs",
        ExperienceCsvSection.Vitals => "Vital rank XP costs",
        ExperienceCsvSection.TrainedSkills => "Trained skill rank XP costs",
        ExperienceCsvSection.SpecializedSkills => "Specialized skill rank XP costs",
        _ => "Experience section",
    };

    public static string SerializeLevels(IReadOnlyList<LevelRow> levels) {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', LevelColumns.Select(PortalTableCsv.Escape)));
        for (var i = 0; i < levels.Count; i++) {
            var row = levels[i];
            sb.AppendLine(string.Join(',', new[] {
                row.Level.ToString(inv),
                PortalTableCsv.Escape(row.XpRequired),
                PortalTableCsv.Escape(row.SkillCredits),
            }));
        }

        return sb.ToString();
    }

    public static (ulong[] Levels, uint[] SkillCredits) ParseLevels(string csvText) {
        var inv = CultureInfo.InvariantCulture;
        var text = csvText.TrimStart('\uFEFF');
        var rows = PortalTableCsv.ParseCsv(text);
        if (rows.Count < 1)
            throw new FormatException("CSV is empty.");

        var colMap = PortalTableCsv.BuildColumnMap(rows[0]);
        foreach (var c in LevelColumns) {
            if (!colMap.ContainsKey(PortalTableCsv.NormalizeColumnKey(c)))
                throw new FormatException($"CSV header is missing required column '{c}'.");
        }

        var entries = new List<(int Level, ulong Xp, uint Credits)>();
        for (var r = 1; r < rows.Count; r++) {
            var row = rows[r];
            if (PortalTableCsv.IsRowBlank(row))
                continue;

            var level = PortalTableCsv.ParseInt(PortalTableCsv.GetCell(row, colMap, "level"), "level", r + 1, inv);
            var xpRaw = PortalTableCsv.GetCell(row, colMap, "xpRequired");
            var crRaw = PortalTableCsv.GetCell(row, colMap, "skillCredits");
            if (!ulong.TryParse(xpRaw.Trim(), NumberStyles.Integer, inv, out var xp))
                throw new FormatException($"Row {r + 1}: invalid xpRequired '{xpRaw}'.");
            if (!uint.TryParse(crRaw.Trim(), NumberStyles.Integer, inv, out var credits))
                throw new FormatException($"Row {r + 1}: invalid skillCredits '{crRaw}'.");
            entries.Add((level, xp, credits));
        }

        ValidateContiguousIndices(entries.Select(e => e.Level).ToList(), "level", "levels");
        entries.Sort((a, b) => a.Level.CompareTo(b.Level));

        var n = entries.Count;
        var levelsArr = new ulong[n];
        var creditsArr = new uint[n];
        for (var i = 0; i < n; i++) {
            if (entries[i].Level != i)
                throw new FormatException($"Levels must be contiguous starting at 0; expected level {i}, found {entries[i].Level}.");
            levelsArr[i] = entries[i].Xp;
            creditsArr[i] = entries[i].Credits;
        }

        return (levelsArr, creditsArr);
    }

    public static string SerializeRankSection(IReadOnlyList<XpRow> rows) {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', RankColumns.Select(PortalTableCsv.Escape)));
        for (var i = 0; i < rows.Count; i++) {
            var row = rows[i];
            sb.AppendLine(string.Join(',', new[] {
                row.Index.ToString(inv),
                PortalTableCsv.Escape(row.Value),
            }));
        }

        return sb.ToString();
    }

    public static uint[] ParseRankSection(string csvText) {
        var inv = CultureInfo.InvariantCulture;
        var text = csvText.TrimStart('\uFEFF');
        var rows = PortalTableCsv.ParseCsv(text);
        if (rows.Count < 1)
            throw new FormatException("CSV is empty.");

        var colMap = PortalTableCsv.BuildColumnMap(rows[0]);
        foreach (var c in RankColumns) {
            if (!colMap.ContainsKey(PortalTableCsv.NormalizeColumnKey(c)))
                throw new FormatException($"CSV header is missing required column '{c}'.");
        }

        var entries = new List<(int Index, uint Xp)>();
        for (var r = 1; r < rows.Count; r++) {
            var row = rows[r];
            if (PortalTableCsv.IsRowBlank(row))
                continue;

            var idx = PortalTableCsv.ParseInt(PortalTableCsv.GetCell(row, colMap, "index"), "index", r + 1, inv);
            var xp = PortalTableCsv.ParseUInt(PortalTableCsv.GetCell(row, colMap, "xp"), "xp", r + 1, inv);
            entries.Add((idx, xp));
        }

        ValidateContiguousIndices(entries.Select(e => e.Index).ToList(), "index", "rows");
        entries.Sort((a, b) => a.Index.CompareTo(b.Index));

        var n = entries.Count;
        var arr = new uint[n];
        for (var i = 0; i < n; i++) {
            if (entries[i].Index != i)
                throw new FormatException($"Indices must be contiguous starting at 0; expected index {i}, found {entries[i].Index}.");
            arr[i] = entries[i].Xp;
        }

        return arr;
    }

    private static void ValidateContiguousIndices(List<int> indices, string columnName, string what) {
        if (indices.Count == 0)
            throw new FormatException($"CSV has no data {what}.");
        if (indices.Count != indices.Distinct().Count())
            throw new FormatException($"Duplicate {columnName} values in CSV.");
        var max = indices.Max();
        if (max != indices.Count - 1)
            throw new FormatException($"{columnName} values must be contiguous from 0 to {indices.Count - 1}.");
    }
}
