using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;

namespace GuildSimulator.Game.Data;

/// <summary>
/// マスタの数値が MASTER_DATA.md「ランク帯の物差し」の帯に収まっているかを見る。
///
/// <see cref="MasterValidator"/> と違い、ここで出るのは<b>エラーではなく警告</b>。
/// 帯は作りたいゲームの形を先に決めたものなので、C〜Sのデータを入れ終えるまでは
/// 既存データのほうが帯の外にいる。警告が0になったときが、そのランク帯が揃ったとき。
/// </summary>
public static class MasterBandChecker
{
    public static List<string> Check(GameMasterData db)
    {
        var warnings = new List<string>();

        CheckEnemies(db, warnings);
        CheckEquipment(db, warnings);
        CheckQuests(db, warnings);
        CheckRankCoverage(db, warnings);
        CheckReachability(db, warnings);

        return warnings;
    }

    static void CheckEnemies(GameMasterData db, List<string> warnings)
    {
        foreach (var master in db.enemies.Values)
        {
            var band = RankBandTable.ForThreat(master.threat);
            if (band == null) continue;

            // 実際の戦闘値で見る。マスタの素の数値は装備や生来の装甲を含まないため。
            var enemy = new EnemyData(master);
            var stats = enemy.GetBaseCombatStats() + enemy.GetEquipmentBonus();
            int pv = enemy.WeaponBasePv + Math.Min(enemy.AttackStatModifier, enemy.MaxStatBonus);
            string where = $"{master.id}（{RankBandTable.BandLabel(master.threat)}）";

            Report(warnings, where, "HP", stats.hp, band.Hp);
            Report(warnings, where, "AV", stats.av, band.Av);
            Report(warnings, where, "DV", stats.dv, band.Dv);
            Report(warnings, where, "PV", pv, band.Pv);
            Report(warnings, where, "exp", master.exp, band.Exp);
        }
    }

    static void CheckEquipment(GameMasterData db, List<string> warnings)
    {
        foreach (var e in db.equipment.Values)
        {
            var band = RankBandTable.ForShopTier(e.shopTier);
            if (band == null)
            {
                warnings.Add($"{e.id}: shopTier {e.shopTier} は帯の外です"
                    + $"（1〜{RankBandTable.MaxEquipmentTier}）");
                continue;
            }

            string where = $"{e.id}（Tier{e.shopTier}）";
            if (e.type == EquipmentType.Weapon)
                Report(warnings, where, "basePv", e.basePv, band.WeaponBasePv);
            else if (e.IsShield)
            {
                Report(warnings, where, "blockChance", e.blockChance, band.BlockChance);
                Report(warnings, where, "blockAv", e.blockAv, band.BlockAv);
            }
            else if (e.type == EquipmentType.Armor)
            {
                // 頭防具は胴より一段低い帯なので、胴だけを見る。
                if (e.GetAllowedSlots().Contains(EquipSlot.Head)) continue;
                Report(warnings, where, "bonus.av", e.bonus.av, band.ArmorAv);
                Report(warnings, where, "bonus.dv", e.bonus.dv, band.ArmorDv);
            }
        }
    }

    static void CheckQuests(GameMasterData db, List<string> warnings)
    {
        foreach (var q in db.allQuests)
        {
            var band = RankBandTable.ForQuestRank(q.rank);
            if (band == null) continue;

            string where = $"{q.id}（{RankBandTable.BandLabel(q.rank)}）";
            Report(warnings, where, "rewardGold", q.rewardGold, band.RewardGold);
            Report(warnings, where, "rewardExp", q.rewardExp, band.RewardExp);
            Report(warnings, where, "rewardGuildPoints", q.rewardGuildPoints, band.GuildPoints);
            Report(warnings, where, "totalPhases", q.totalPhases, band.TotalPhases);
        }

        CheckGatherQuests(db, warnings);
    }

    /// <summary>
    /// 採取クエストが予定フェーズ内に目標数へ届くか。届かなくても即撤退にはならず、
    /// 延長するか引き上げるかの判断になるが、それが毎回起きるようでは予定が予定にならない。
    /// 「戦闘で負けた」ではなく「抽選が渋かった」で行程が延びるのはプレイヤーに手の打ちようが
    /// ないので、通常は予定内で収まる余裕を数値の側に持たせておく。
    /// </summary>
    static void CheckGatherQuests(GameMasterData db, List<string> warnings)
    {
        foreach (var q in db.allQuests)
        {
            if (!q.IsGatherQuest) continue;
            string where = $"{q.id}（採取）";

            if (q.gatherMinPerEvent < 0 || q.gatherMaxPerEvent < q.gatherMinPerEvent)
            {
                warnings.Add($"{where}: gatherMinPerEvent({q.gatherMinPerEvent}) と "
                    + $"gatherMaxPerEvent({q.gatherMaxPerEvent}) が逆転しています");
                continue;
            }

            if (q.gatherChance < RankBandTable.MinGatherChance)
                warnings.Add($"{where}: gatherChance が {q.gatherChance} で、"
                    + $"下限 {RankBandTable.MinGatherChance} を下回っています"
                    + "（採取判定の回数が少なすぎて、期待量に余裕があってもブレで未達になります）");

            float expected = RankBandTable.ExpectedGatherYield(
                q.totalPhases, q.bossPhase, q.BossEnemy != null,
                q.gatherChance, q.gatherMinPerEvent, q.gatherMaxPerEvent);
            float needed = q.gatherTargetCount * RankBandTable.GatherSurplusFactor;
            if (expected < needed)
                warnings.Add($"{where}: 採取の期待量が {expected:0.#} しかなく、"
                    + $"gatherTargetCount {q.gatherTargetCount} の"
                    + $"{RankBandTable.GatherSurplusFactor:0.#}倍（{needed:0.#}）に届きません"
                    + "（予定フェーズ内に終わらず、延長判断が頻発する設定です）");
        }
    }

    /// <summary>ランク帯そのものが開通しているか。ここが欠けると、その帯の冒険者は伸びなくなる。</summary>
    static void CheckRankCoverage(GameMasterData db, List<string> warnings)
    {
        for (int rank = Rank.Min; rank <= Rank.Max; rank++)
        {
            if (!db.allQuests.Any(q => q.rank == rank))
                warnings.Add($"{RankBandTable.BandLabel(rank)}: rank {rank} のクエストが1本もありません"
                    + "（この帯の冒険者は昇格も習熟もできません）");
        }

        // ギルドの昇格試験は各段に1本ずつ要る。1つ欠けるとそこから上へ進めない。
        for (int from = Rank.Min; from < Rank.Max; from++)
        {
            if (!db.allQuests.Any(q => q.isEmergencyQuest && q.rankUpOnClear > 0 && q.rank == from))
                warnings.Add($"ギルド昇格 {Rank.Label(from)}→{Rank.Label(from + 1)}: "
                    + $"rank {from} の昇格試験（isEmergencyQuest かつ rankUpOnClear > 0）がありません");
        }
    }

    /// <summary>プレイヤーの手に届く経路があるか。どこからも参照されないデータは存在しないのと同じ。</summary>
    static void CheckReachability(GameMasterData db, List<string> warnings)
    {
        // 施設をすべて建てたときの商店レベル。これでも届かない装備は買えない。
        int maxShopLevel = ShopService.BaseShopLevel + db.facilities.Values.Sum(f => f.shopLevelBonus);

        var droppable = new HashSet<string>();
        foreach (var d in db.dungeons.Values)
            foreach (var t in d.treasureTable)
                if (t.Equipment != null) droppable.Add(t.Equipment.id);
        foreach (var q in db.allQuests)
            foreach (var t in q.bossDrops)
                if (t.Equipment != null) droppable.Add(t.Equipment.id);
        foreach (var e in db.enemies.Values)
            foreach (var t in e.dropTable)
                if (t.Equipment != null) droppable.Add(t.Equipment.id);

        foreach (var e in db.equipment.Values)
        {
            bool dropOnlyById = e.id.StartsWith(ShopService.DropOnlyIdPrefix, StringComparison.Ordinal);
            bool buyable = !dropOnlyById && e.shopTier <= maxShopLevel;
            if (buyable || droppable.Contains(e.id)) continue;
            string why = dropOnlyById
                ? "ドロップ専用の命名なのに"
                : $"shopTier {e.shopTier} が商店の上限({maxShopLevel})を超えていて、";
            warnings.Add($"{e.id}: {why}宝箱・ボスドロップ・敵ドロップのどこにも載っていません"
                + "（入手経路がありません）");
        }

        var usedEvents = db.dungeons.Values.SelectMany(d => d.turnEndEvents).Select(e => e.id).ToHashSet();
        foreach (var ev in db.choiceEvents.Values)
            if (!usedEvents.Contains(ev.id))
                warnings.Add($"{ev.id}: どのダンジョンの turnEndEventIds にも入っていないので一度も発生しません");

        var usedUnits = db.dungeons.Values.SelectMany(d => d.encounterTable).Select(e => e.unitId)
            .Concat(db.allQuests.Where(q => q.BossEnemy != null).Select(q => q.BossEnemy!.id))
            .ToHashSet();
        foreach (var unit in db.enemyUnits.Values)
            if (!usedUnits.Contains(unit.id))
                warnings.Add($"{unit.id}: どの encounterTable にもボスにも使われていないので一度も出てきません");

        // 遭遇表がフェーズ全域を覆っていないと、その深さだけ戦闘が起きない。
        foreach (var d in db.dungeons.Values)
        {
            int deepest = db.allQuests.Where(q => q.Dungeon == d).Select(q => q.totalPhases).DefaultIfEmpty(0).Max();
            if (deepest <= 0) continue;
            for (int phase = 1; phase <= deepest; phase++)
            {
                if (d.encounterTable.Any(e => e.IsEligible(phase))) continue;
                warnings.Add($"{d.id}: フェーズ {phase} に出せる敵が encounterTable にありません"
                    + $"（このダンジョンの最大 totalPhases は {deepest}）");
                break;
            }
        }
    }

    static void Report(List<string> warnings, string where, string field, int value, Band band)
    {
        if (band.Contains(value)) return;
        warnings.Add($"{where}: {field} が {value} で帯（{band}）の外です");
    }
}
