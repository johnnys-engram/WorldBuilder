using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;

namespace WorldBuilder.Modules.Skill;

internal static class SkillTableImportExport {
    public const int CurrentFormatVersion = 1;

    public static void ReplaceSkillTable(SkillTable table, SkillTableExportFile file) {
        var rows = file.Skills ?? new List<SkillExportDto>();
        table.Skills.Clear();
        foreach (var dto in rows.OrderBy(s => s.Id))
            table.Skills[(SkillId)dto.Id] = dto.ToSkill();
    }
}

internal sealed class SkillTableExportFile {
    public int FormatVersion { get; set; } = SkillTableImportExport.CurrentFormatVersion;
    public List<SkillExportDto> Skills { get; set; } = new();
}

internal sealed class SkillExportDto {
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public uint IconId { get; set; }
    public int TrainedCost { get; set; }
    public int SpecializedCost { get; set; }
    public SkillCategory Category { get; set; }
    public bool ChargenUse { get; set; }
    public uint MinLevel { get; set; }
    public double UpperBound { get; set; }
    public double LowerBound { get; set; }
    public double LearnMod { get; set; }
    public int FormulaDivisor { get; set; }
    public AttributeId FormulaAttribute1 { get; set; }
    public AttributeId? FormulaAttribute2 { get; set; }
    public bool FormulaUseFormula { get; set; }
    public bool FormulaHasSecondAttribute { get; set; }
    public int FormulaAdditiveBonus { get; set; }

    public static SkillExportDto FromSkill(SkillId skillId, SkillBase s) {
        var f = s.Formula ?? new SkillFormula();
        var useSecond = f.Attribute2Multiplier > 0;
        AttributeId? a2 = useSecond
            ? (Enum.IsDefined(typeof(AttributeId), f.Attribute2) ? f.Attribute2 : null)
            : null;

        return new SkillExportDto {
            Id = (int)skillId,
            Name = s.Name?.ToString() ?? "",
            Description = s.Description?.ToString() ?? "",
            IconId = s.IconId,
            TrainedCost = s.TrainedCost,
            SpecializedCost = s.SpecializedCost,
            Category = s.Category,
            ChargenUse = s.ChargenUse,
            MinLevel = s.MinLevel,
            UpperBound = s.UpperBound,
            LowerBound = s.LowerBound,
            LearnMod = s.LearnMod,
            FormulaDivisor = f.Divisor,
            FormulaAttribute1 = f.Attribute1,
            FormulaAttribute2 = a2,
            FormulaUseFormula = f.Attribute1Multiplier > 0,
            FormulaHasSecondAttribute = useSecond,
            FormulaAdditiveBonus = f.AdditiveBonus,
        };
    }

    public SkillBase ToSkill() {
        AttributeId attr2Stored = default;
        if (FormulaHasSecondAttribute && FormulaAttribute2.HasValue)
            attr2Stored = FormulaAttribute2.Value;

        return new SkillBase {
            Name = Name,
            Description = Description,
            IconId = IconId,
            TrainedCost = TrainedCost,
            SpecializedCost = SpecializedCost,
            Category = Category,
            ChargenUse = ChargenUse,
            MinLevel = MinLevel,
            UpperBound = UpperBound,
            LowerBound = LowerBound,
            LearnMod = LearnMod,
            Formula = new SkillFormula {
                Divisor = FormulaDivisor,
                Attribute1 = FormulaAttribute1,
                Attribute2 = attr2Stored,
                Attribute1Multiplier = FormulaUseFormula ? 1 : 0,
                Attribute2Multiplier = FormulaHasSecondAttribute ? 1 : 0,
                AdditiveBonus = FormulaAdditiveBonus,
            },
        };
    }
}
