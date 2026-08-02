using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;

namespace GuildSimulator.Core.Systems.Quest;

/// <summary>
/// 戦闘の外に効くスキルを、パーティ単位で合計したもの。
///
/// 戦闘中の数値（<see cref="Battle.UnitCalculator"/>）と違って隊列も生死も関係ない。
/// 「その遠征に誰を連れて行ったか」だけで決まるので、道中で倒れても効果は消えない
/// （荷運びの目利きも罠の勘も、倒れた本人ではなく隊そのものに残る、という扱い）。
///
/// 同じスキルを複数人が持てばそのぶん積み上がる。ただしゴールドと経験値の減少は
/// 元手を割らないよう -100% で下げ止まる。
/// </summary>
public readonly record struct PartySkillEffects(
    int goldPercent,
    int expPercent,
    int treasureChancePercent,
    int trapChancePercent,
    int enemyEncounterChancePercent,
    int healEventChancePercent,
    int restHealPercent,
    int enemyDropChancePercent,
    int rareDropChancePercent)
{
    public static readonly PartySkillEffects None = default;

    /// <summary>報酬が消し飛ばないための下限（%）。</summary>
    public const int MinRewardPercent = -100;

    public static PartySkillEffects Of(IEnumerable<AdventurerData?>? formation)
    {
        if (formation == null) return None;

        int gold = 0, exp = 0, treasure = 0, trap = 0;
        int encounter = 0, healEvent = 0, restHeal = 0, enemyDrop = 0, rareDrop = 0;
        foreach (var a in formation)
        {
            if (a == null) continue;
            foreach (var sk in a.Skills)
            {
                var e = sk.expedition;
                if (e.IsEmpty) continue;

                // 遠征効果でも「構え」の条件は見る。重鎧を脱いだ者に重鎧の目利きは働かない。
                if (!UnitCalculator.MeetsGearRequirements(sk, a)) continue;

                gold += e.goldPercent;
                exp += e.expPercent;
                treasure += e.treasureChancePercent;
                trap += e.trapChancePercent;
                encounter += e.enemyEncounterChancePercent;
                healEvent += e.healEventChancePercent;
                restHeal += e.restHealPercent;
                enemyDrop += e.enemyDropChancePercent;
                rareDrop += e.rareDropChancePercent;
            }
        }

        return new PartySkillEffects(
            Math.Max(MinRewardPercent, gold),
            Math.Max(MinRewardPercent, exp),
            treasure,
            trap,
            encounter,
            healEvent,
            restHeal,
            enemyDrop,
            rareDrop);
    }

    /// <summary>報酬にかける倍率。0を下回らない。</summary>
    public float GoldMultiplier => Math.Max(0f, 1f + goldPercent / 100f);
    public float ExpMultiplier => Math.Max(0f, 1f + expPercent / 100f);

    /// <summary>ダンジョンイベントの重みにかける倍率。0未満にはならない（＝完全には消せない）。</summary>
    public float ChanceMultiplierFor(DungeonEventType type) => type switch
    {
        DungeonEventType.Treasure => Math.Max(0f, 1f + treasureChancePercent / 100f),
        DungeonEventType.Trap => Math.Max(0f, 1f + trapChancePercent / 100f),
        DungeonEventType.EnemyEncounter => Math.Max(0f, 1f + enemyEncounterChancePercent / 100f),
        DungeonEventType.Heal => Math.Max(0f, 1f + healEventChancePercent / 100f),
        _ => 1f,
    };

    /// <summary>休息で回復するHPへの倍率。減少効果を足しても0未満にはしない。</summary>
    public float RestHealMultiplier => Math.Max(0f, 1f + restHealPercent / 100f);

    /// <summary>
    /// 敵ドロップ1件の最終抽選率。
    /// 基礎ドロップ補正に加え、装備ならCommonから離れたレアリティ段階ぶんだけ
    /// rareDropChancePercentを重ね、希少品ほど解体術の恩恵を大きくする。
    /// </summary>
    public float EnemyDropChanceFor(RewardEntryData entry)
    {
        float dropMultiplier = Math.Max(0f, 1f + enemyDropChancePercent / 100f);
        int raritySteps = entry.type == RewardType.Equipment && entry.Equipment != null
            ? Math.Max(0, (int)entry.Equipment.rarity - (int)Rarity.Common)
            : 0;
        float rarityMultiplier = Math.Max(0f, 1f + rareDropChancePercent * raritySteps / 100f);
        return Math.Clamp(entry.chance * dropMultiplier * rarityMultiplier, 0f, 1f);
    }
}
