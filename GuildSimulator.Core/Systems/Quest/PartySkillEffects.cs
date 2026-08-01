using GuildSimulator.Core.GameData;
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
    int goldPercent, int expPercent, int treasureChancePercent, int trapChancePercent)
{
    public static readonly PartySkillEffects None = default;

    /// <summary>報酬が消し飛ばないための下限（%）。</summary>
    public const int MinRewardPercent = -100;

    public static PartySkillEffects Of(IEnumerable<AdventurerData?>? formation)
    {
        if (formation == null) return None;

        int gold = 0, exp = 0, treasure = 0, trap = 0;
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
            }
        }

        return new PartySkillEffects(
            Math.Max(MinRewardPercent, gold),
            Math.Max(MinRewardPercent, exp),
            treasure,
            trap);
    }

    /// <summary>報酬にかける倍率。0を下回らない。</summary>
    public float GoldMultiplier => Math.Max(0f, 1f + goldPercent / 100f);
    public float ExpMultiplier => Math.Max(0f, 1f + expPercent / 100f);

    /// <summary>ダンジョンイベントの重みにかける倍率。0未満にはならない（＝完全には消せない）。</summary>
    public float ChanceMultiplierFor(Models.DungeonEventType type) => type switch
    {
        Models.DungeonEventType.Treasure => Math.Max(0f, 1f + treasureChancePercent / 100f),
        Models.DungeonEventType.Trap => Math.Max(0f, 1f + trapChancePercent / 100f),
        _ => 1f,
    };
}
