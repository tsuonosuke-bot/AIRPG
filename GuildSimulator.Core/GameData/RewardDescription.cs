using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.GameData;

/// <summary>報酬・戦利品を画面に出すための説明文づくり。</summary>
public static class RewardDescription
{
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
