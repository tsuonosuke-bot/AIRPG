using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.Systems.Guild;

/// <summary>
/// 所持している遺物の効果を集計する。
/// <see cref="GameFeatures.RelicsEnabled"/> が false の間（＝凍結中）は、
/// 所持一覧を保持したまま効果だけを完全に打ち消す。
/// </summary>
public static class RelicSystem
{
    static List<RelicMasterData>? _relics;

    /// <summary>凍結中でも所持一覧そのものは受け取る（復活時にそのまま効き始める）。</summary>
    public static void SetRelics(List<RelicMasterData> relics) => _relics = relics;

    /// <summary>凍結中は遺物の報酬エントリを抽選・付与の対象から外すための判定。</summary>
    public static bool IsFrozenRelicReward(RewardEntryData entry) =>
        entry.type == RewardType.Relic && !GameFeatures.RelicsEnabled;

    // 同カテゴリの倍率レリックは「倍率-1（＝ボーナス分）」を全レリックで加算してから+1する。
    // 単純に掛け算で連鎖させると所持数が増えるほど指数的に膨れ上がるため、
    // 複数所持時は掛け算より緩やかな伸びになるようにする（単体所持時の数値は変わらない）。
    public static void GetUnitModifiers(out StatBlock add, out StatMultiplier mul)
    {
        add = default;
        StatMultiplier bonusSum = default;
        mul = StatMultiplier.One;
        if (!GameFeatures.RelicsEnabled || _relics == null) return;
        foreach (var r in _relics)
        {
            if (r.effectType == RelicEffectType.Unit_AddFlat) add += r.add;
            else if (r.effectType == RelicEffectType.Unit_Multiply) bonusSum = AddBonus(bonusSum, r.mul);
        }
        mul.hp = CombineRate(bonusSum.hp);
        mul.san = CombineRate(bonusSum.san);
        mul.heal = CombineRate(bonusSum.heal);
    }

    public static float GetGoldRewardMultiplier() => CombineRateSum(RelicEffectType.GoldReward_Multiply);

    public static float GetUpkeepMultiplier() => CombineRateSum(RelicEffectType.Upkeep_Multiply);

    public static float GetRestHealMultiplier() => CombineRateSum(RelicEffectType.RestHeal_Multiply);

    static float CombineRateSum(RelicEffectType type)
    {
        if (!GameFeatures.RelicsEnabled || _relics == null) return 1f;
        float bonus = 0f;
        foreach (var r in _relics)
            if (r.effectType == type) bonus += Math.Max(0f, r.rate) - 1f;
        return CombineRate(bonus);
    }

    static float CombineRate(float bonusSum) => Math.Max(0f, 1f + bonusSum);

    static StatMultiplier AddBonus(StatMultiplier sum, StatMultiplier m)
    {
        sum.hp += m.hp - 1f;
        sum.san += m.san - 1f;
        sum.heal += m.heal - 1f;
        return sum;
    }
}
