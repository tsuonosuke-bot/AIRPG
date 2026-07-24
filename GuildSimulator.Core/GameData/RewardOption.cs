using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.GameData;

public class RewardOption
{
    public RewardType type;
    public RelicMasterData? relic;
    public EquipmentMasterData? equipment;
    public SkillMasterData? skill;
    public ConsumableMasterData? consumable;
    public int gold;
    public int quantity = 1;

    public string Title => type switch
    {
        RewardType.Relic => relic != null ? $"遺物：{relic.relicName}" : "遺物",
        RewardType.Equipment => equipment != null
            ? $"装備：{equipment.displayName}{(quantity > 1 ? $" x{quantity}" : "")}"
            : "装備",
        RewardType.Skill => skill != null ? $"スキル：{skill.skillName}" : "スキル",
        RewardType.Consumable => consumable != null ? $"消費アイテム：{consumable.displayName}" : "消費アイテム",
        RewardType.Gold => $"資金 +{gold}G",
        _ => type.ToString(),
    };

    /// <summary>選択画面で Title の下に出す効果説明。空文字なら表示しない。</summary>
    public string Detail => type switch
    {
        RewardType.Relic => relic != null ? DescribeRelic(relic) : "",
        RewardType.Equipment => equipment != null ? DescribeEquipment(equipment, quantity) : "",
        RewardType.Skill => skill != null ? DescribeSkill(skill) : "",
        RewardType.Consumable => consumable?.description ?? "",
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
            RelicEffectType.GoldReward_Multiply => $"クエスト報酬の資金が {r.rate:0.##}倍",
            RelicEffectType.Upkeep_Multiply => $"維持費が {r.rate:0.##}倍",
            RelicEffectType.RestHeal_Multiply => $"休息時の回復量が {r.rate:0.##}倍",
            _ => "",
        };
    }

    static string DescribeEquipment(EquipmentMasterData e, int quantity)
    {
        var parts = new List<string>();
        if (e.type == EquipmentType.Weapon)
        {
            if (e.physicalCoeff > 0f && Math.Abs(e.physicalCoeff - 1f) > 0.001f)
                parts.Add($"物理係数 x{e.physicalCoeff:0.##}");
            if (e.magicCoeff > 0f && Math.Abs(e.magicCoeff - 1f) > 0.001f)
                parts.Add($"魔法係数 x{e.magicCoeff:0.##}");
            if (e.healCoeff > 0f && Math.Abs(e.healCoeff - 1f) > 0.001f)
                parts.Add($"回復係数 x{e.healCoeff:0.##}");
        }
        AddSigned(parts, "物理攻撃", e.flatPhysicalAtk);
        AddSigned(parts, "魔法攻撃", e.flatMagicAtk);
        AddSigned(parts, "回復力", e.flatHeal);
        string bonus = DescribeStatBlock(e.bonus);
        if (bonus.Length > 0) parts.Add(bonus);
        parts.Add($"重量{e.weight}");
        parts.Add($"商店価格計 {e.price * Math.Max(1, quantity)}G");
        return string.Join(" / ", parts);
    }

    static void AddSigned(List<string> parts, string label, int value)
    {
        if (value != 0) parts.Add($"{label}{(value > 0 ? "+" : "")}{value}");
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
        Add("HP", b.hp); Add("士気", b.san);
        Add("物理攻撃", b.pAtk); Add("物理防御", b.pDef);
        Add("魔法攻撃", b.mAtk); Add("魔法防御", b.mDef);
        Add("命中", b.hit); Add("回避", b.evade); Add("回復力", b.heal);
        return string.Join(" ", parts);
    }

    static string DescribeStatMul(StatMultiplier m)
    {
        var parts = new List<string>();
        void Add(string label, float v) { if (v != 0f && Math.Abs(v - 1f) > 0.001f) parts.Add($"{label} x{v:0.##}"); }
        Add("HP", m.hp); Add("士気", m.san);
        Add("物理攻撃", m.pAtk); Add("物理防御", m.pDef);
        Add("魔法攻撃", m.mAtk); Add("魔法防御", m.mDef);
        Add("命中", m.hit); Add("回避", m.evade); Add("回復力", m.heal);
        return string.Join(" ", parts);
    }
}
