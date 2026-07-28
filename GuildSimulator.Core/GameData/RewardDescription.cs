using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.GameData;

/// <summary>報酬・戦利品を画面に出すための説明文づくり。</summary>
public static class RewardDescription
{
    public static string DescribeLoot(RewardEntryData e) => e.type switch
    {
        RewardType.Gold => $"資金 {e.gold}G",
        RewardType.Relic => $"遺物「{e.Relic?.relicName ?? "?"}」",
        RewardType.Equipment => $"装備「{e.Equipment?.displayName ?? "?"}」",
        RewardType.Skill => $"スキル「{e.Skill?.skillName ?? "?"}」",
        RewardType.Consumable => $"消費アイテム「{e.Consumable?.displayName ?? "?"}」",
        _ => e.type.ToString(),
    };

    /// <summary>個数が1のときは省く数量表記。</summary>
    public static string DescribeQuantity(RewardEntryData e) => e.quantity > 1 ? $" x{e.quantity}" : "";

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

    static string DescribeStatBlock(StatBlock b)
    {
        var parts = new List<string>();
        void Add(string label, int v) { if (v != 0) parts.Add($"{label}{(v > 0 ? "+" : "")}{v}"); }
        Add("HP", b.hp); Add("士気", b.san);
        Add("物理装甲AV", b.av); Add("魔法装甲mAV", b.mav);
        Add("物理貫通PV", b.pv); Add("魔法貫通mPV", b.mpv);
        Add("回避DV", b.dv); Add("命中", b.toHit); Add("回復力", b.heal);
        return string.Join(" ", parts);
    }

    static string DescribeStatMul(StatMultiplier m)
    {
        var parts = new List<string>();
        void Add(string label, float v) { if (v != 0f && Math.Abs(v - 1f) > 0.001f) parts.Add($"{label} x{v:0.##}"); }
        Add("HP", m.hp); Add("士気", m.san); Add("回復力", m.heal);
        return string.Join(" ", parts);
    }
}
