using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Lib;

/// <summary>
/// Spell list rows use <see cref="DatReaderWriter.Types.SpellBase.Icon"/> when set; otherwise the magic-school
/// glyph from the matching row in <see cref="SkillTable"/> (same numeric IDs as <see cref="MagicSchool"/>).
/// </summary>
public static class SpellSchoolListIconMap {
    /// <summary>Retail Life Magic school glyph (0x60007F34) if SkillTable has no icon for school 0x21.</summary>
    public const uint LifeMagicIconFallback = 100668260;

    public static Dictionary<MagicSchool, uint> ResolveSchoolIcons(IDatReaderWriter dats) {
        var map = new Dictionary<MagicSchool, uint>();
        if (dats.Portal.TryGet<SkillTable>(0x0E000004, out var table) && table.Skills != null) {
            foreach (var kvp in table.Skills) {
                int sid = (int)kvp.Key;
                if (!Enum.IsDefined(typeof(MagicSchool), sid)) continue;
                var icon = (uint)kvp.Value.IconId;
                if (icon != 0)
                    map[(MagicSchool)sid] = icon;
            }
        }

        map.TryAdd(MagicSchool.LifeMagic, LifeMagicIconFallback);
        return map;
    }

    public static uint ListIconId(uint spellIcon, MagicSchool school, IReadOnlyDictionary<MagicSchool, uint> schoolIcons) {
        if (spellIcon != 0) return spellIcon;
        return schoolIcons.TryGetValue(school, out var id) ? id : 0u;
    }
}
