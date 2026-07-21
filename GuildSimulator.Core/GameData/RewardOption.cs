using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.GameData;

public class RewardOption
{
    public RewardType type;
    public RelicMasterData? relic;
    public EquipmentMasterData? equipment;
    public SkillMasterData? skill;
    public int gold;

    public string Title => type switch
    {
        RewardType.Relic => relic != null ? $"遺物：{relic.relicName}" : "遺物",
        RewardType.Equipment => equipment != null ? $"装備：{equipment.displayName}" : "装備",
        RewardType.Skill => skill != null ? $"スキル：{skill.skillName}" : "スキル",
        RewardType.Gold => $"Gold +{gold}",
        _ => type.ToString(),
    };

    /// <summary>選択画面で Title の下に出す効果説明。空文字なら表示しない。</summary>
    public string Detail => type switch
    {
        RewardType.Relic => relic != null ? DescribeRelic(relic) : "",
        RewardType.Equipment => equipment != null ? DescribeEquipment(equipment) : "",
        RewardType.Skill => skill != null ? DescribeSkill(skill) : "",
        _ => "",
    };

    public static string DescribeRelic(RelicMasterData r)
    {
        if (!string.IsNullOrWhiteSpace(r.description)) return r.description;

        // description 未設定のマスタは効果タイプから機械的に組み立てる。
        return r.effectType switch
        {
            RelicEffectType.Unit_AddFlat => $"ユニットに {DescribeStatBlock(r.add)}",
            RelicEffectType.Unit_Multiply => $"ユニットの {DescribeStatMul(r.mul)}",
            RelicEffectType.GoldReward_Multiply => $"クエスト報酬Goldが {r.rate:0.##}倍",
            RelicEffectType.Upkeep_Multiply => $"維持費が {r.rate:0.##}倍",
            RelicEffectType.RestHeal_Multiply => $"休息時の回復量が {r.rate:0.##}倍",
            _ => "",
        };
    }

    static string DescribeEquipment(EquipmentMasterData e)
    {
        var parts = new List<string>();
        if (e.physicalCoeff > 0f) parts.Add($"物理係数 x{e.physicalCoeff:0.##}");
        if (e.magicCoeff > 0f) parts.Add($"魔法係数 x{e.magicCoeff:0.##}");
        if (e.healCoeff > 0f) parts.Add($"回復係数 x{e.healCoeff:0.##}");
        if (e.flatPhysicalAtk != 0) parts.Add($"pAtk+{e.flatPhysicalAtk}");
        if (e.flatMagicAtk != 0) parts.Add($"mAtk+{e.flatMagicAtk}");
        if (e.flatHeal != 0) parts.Add($"heal+{e.flatHeal}");
        string bonus = DescribeStatBlock(e.bonus);
        if (bonus.Length > 0) parts.Add(bonus);
        parts.Add($"重量{e.weight}");
        return string.Join(" / ", parts);
    }

    static string DescribeSkill(SkillMasterData s)
    {
        var parts = new List<string>();
        parts.Add(s.scope == SkillScope.UnitAura ? "ユニット全体" : "自身");
        if (s.frontOnly) parts.Add("前衛のみ");
        if (s.backOnly) parts.Add("後衛のみ");
        if (s.requireWeaponType) parts.Add($"{s.requiredWeaponType}装備時");
        if (s.requireArmorType) parts.Add($"{s.requiredArmorType}装備時");
        string add = DescribeStatBlock(s.add);
        if (add.Length > 0) parts.Add(add);
        string mul = DescribeStatMul(s.mul);
        if (mul.Length > 0) parts.Add(mul);
        return string.Join(" / ", parts);
    }

    static string DescribeStatBlock(StatBlock b)
    {
        var parts = new List<string>();
        void Add(string label, int v) { if (v != 0) parts.Add($"{label}{(v > 0 ? "+" : "")}{v}"); }
        Add("HP", b.hp); Add("SAN", b.san);
        Add("pAtk", b.pAtk); Add("pDef", b.pDef);
        Add("mAtk", b.mAtk); Add("mDef", b.mDef);
        Add("hit", b.hit); Add("evade", b.evade); Add("heal", b.heal);
        return string.Join(" ", parts);
    }

    static string DescribeStatMul(StatMultiplier m)
    {
        var parts = new List<string>();
        void Add(string label, float v) { if (v != 0f && Math.Abs(v - 1f) > 0.001f) parts.Add($"{label} x{v:0.##}"); }
        Add("HP", m.hp); Add("SAN", m.san);
        Add("pAtk", m.pAtk); Add("pDef", m.pDef);
        Add("mAtk", m.mAtk); Add("mDef", m.mDef);
        Add("hit", m.hit); Add("evade", m.evade); Add("heal", m.heal);
        return string.Join(" ", parts);
    }
}
