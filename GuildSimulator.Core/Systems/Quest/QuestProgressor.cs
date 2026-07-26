using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Guild;

namespace GuildSimulator.Core.Systems.Quest;

public class QuestProgressor
{
    public void AdvanceOnePhase(QuestRun q, int currentTurn, IReadOnlyCollection<RelicMasterData> ownedRelics)
    {
        int phase = q.currentPhase + 1;
        var qe = ResolveQuestEvent(q.def, phase);
        bool isBoss = (q.def.BossEnemy != null && phase == q.def.bossPhase)
                   || qe == QuestEventType.ForceBossEncounter;

        DungeonEventType ev = RollDungeonEvent(q.def.Dungeon, phase);

        // 採取クエストは自前の抽選を先に回し、当たればダンジョン抽選を上書きする。
        if (q.def.IsGatherQuest && !isBoss && !q.GatherFulfilled
            && GameRandom.NextFloat() < q.def.gatherChance)
            ev = DungeonEventType.Gather;

        if (qe == QuestEventType.ForceEnemyEncounter) ev = DungeonEventType.EnemyEncounter;
        else if (qe == QuestEventType.ForceHeal) ev = DungeonEventType.Heal;
        else if (qe == QuestEventType.ForceTrap) ev = DungeonEventType.Trap;
        else if (qe == QuestEventType.ForceTreasure) ev = DungeonEventType.Treasure;
        else if (qe == QuestEventType.ForceGather && q.def.IsGatherQuest) ev = DungeonEventType.Gather;
        if (isBoss) ev = DungeonEventType.EnemyEncounter;

        string evTitle = ev.ToString();
        string evResult = "";

        switch (ev)
        {
            case DungeonEventType.EnemyEncounter:
                {
                    EnemyUnitTemplate? enemyTpl = isBoss ? q.def.BossEnemy : PickEncounter(q.def.Dungeon, phase);
                    if (enemyTpl == null)
                    {
                        evTitle = "敵遭遇"; evResult = "敵テーブルが空（スキップ）"; break;
                    }

                    // ボスも通常エンカと同じ深部スケーリングを受ける。ボスは常に bossPhase の深さで固定し、
                    // 通常エンカだけが遭遇したフェーズ相応にスケールする。
                    int enemyLevel = enemyTpl.baseLevel;
                    if (q.def.Dungeon != null)
                    {
                        int scalePhase = isBoss ? q.def.bossPhase : phase;
                        enemyLevel += (int)Math.Floor((scalePhase - 1) * q.def.Dungeon.enemyLevelPerPhase);
                    }
                    enemyLevel = Math.Max(1, enemyLevel);

                    evTitle = isBoss ? $"ボス遭遇：{enemyTpl.unitName}" : $"敵遭遇：{enemyTpl.unitName}(Lv{enemyLevel})";

                    var enemyMembers = CreateEnemyMembers(enemyTpl, enemyLevel);
                    var advI = q.formation.Cast<IUnitMember?>().ToArray();
                    var enemyI = enemyMembers.Cast<IUnitMember?>().ToArray();

                    // 敵は毎回フレッシュな個体なのでここでHPを初期化する。冒険者側はクエスト開始時に
                    // 初期化済みで、フェーズを跨いで持ち越す（QuestManager.TryStartQuest参照）。
                    foreach (var (m, s) in UnitCalculator.CalcPerMember(enemyI, isAllySide: false))
                    {
                        m.CombatHpMax = s.hp;
                        m.CombatHp = s.hp;
                    }

                    var result = BattleResolver.Resolve(
                        advI, enemyI, q.logs, currentTurn, phase, q.morale, q.policy);

                    bool advWiped = !q.formation.Any(a => a != null && a.isAlive);
                    bool enemyWiped = !enemyMembers.Any(e => e != null && e.isAlive);

                    if (result.adventurersRetreated)
                    {
                        q.retreated = true;
                        evResult = $"士気崩壊で撤退（HP {q.unitHpCurrent}/{q.unitHpMax}）→ 引き上げ";
                    }
                    else if (enemyWiped)
                    {
                        int rally = q.morale.RestoreRate(MoraleState.VictoryRecoverRate);
                        evResult = $"勝利（HP {q.unitHpCurrent}/{q.unitHpMax} 士気 {q.morale.Current}/{q.morale.Max}）";
                        if (rally > 0) q.logs.Add($"  Phase {phase}: 勝利で士気を持ち直した（+{rally}）");
                        int totalExp = (int)Math.Floor(
                            enemyMembers.Sum(e => e?.master.exp ?? 0)
                            * (1f + q.expRewardBonusPercent / 100f));
                        if (isBoss)
                        {
                            q.bossDefeated = true;
                            q.logs.Add($"  Phase {phase}: ボス撃破！");
                            RollBossDrops(q, phase);
                        }
                        foreach (var a in q.formation)
                        {
                            if (a == null || !a.isAlive) continue;
                            if (a.AddExperience(totalExp, out var ups))
                                q.logs.Add($"  {a.name} 経験値 +{totalExp}（レベルアップ +{ups}）");
                        }
                        RollEnemyDrops(q, enemyMembers, phase);
                    }
                    else if (advWiped)
                    {
                        q.failed = true;
                        evResult = "全滅 → 失敗";
                    }
                    break;
                }

            case DungeonEventType.Heal:
                {
                    evTitle = "休息";
                    int before = q.unitHpCurrent;
                    float restMul = RelicSystem.GetRestHealMultiplier();
                    var perMember = UnitCalculator.CalcPerMember(q.formation.Cast<IUnitMember?>().ToArray(), isAllySide: true);
                    foreach (var (m, s) in perMember)
                    {
                        int baseHeal = (int)Math.Ceiling(Math.Max(1, m.CombatHpMax) * 0.5f);
                        int bonusHeal = (int)Math.Ceiling(s.heal * 0.5f);
                        int cap = (int)Math.Ceiling(Math.Max(1, m.CombatHpMax) * 0.5f);
                        int heal = (int)Math.Floor(Math.Min(baseHeal + bonusHeal, cap) * restMul);
                        m.CombatHp = Math.Min(m.CombatHpMax, m.CombatHp + heal);
                    }
                    int rallied = q.morale.RestoreRate(MoraleState.RestRecoverRate);
                    evResult = $"回復 +{q.unitHpCurrent - before}（{q.unitHpCurrent}/{q.unitHpMax}）"
                             + (rallied > 0 ? $" 士気 +{rallied}（{q.morale.Current}/{q.morale.Max}）" : "");
                    break;
                }

            case DungeonEventType.Trap:
                {
                    evTitle = "罠";
                    var alive = q.formation.Where(a => a != null && a.isAlive).Select(a => a!).ToList();
                    var victim = alive.Count > 0 ? alive[GameRandom.Range(0, alive.Count)] : null;
                    if (victim == null)
                    {
                        evResult = "何も起きなかった";
                    }
                    else
                    {
                        int rawDamage = (int)Math.Ceiling(Math.Max(1, victim.CombatHpMax) * 0.15f);
                        int dmg = Math.Max(1, (int)Math.Ceiling(
                            rawDamage * (1f - q.trapDamageReductionPercent / 100f)));
                        int applied = Math.Min(dmg, victim.CombatHp);
                        victim.CombatHp = Math.Max(0, victim.CombatHp - dmg);
                        q.morale.DrainFromDamage(applied, q.unitHpMax);
                        evResult = $"{victim.name} がダメージ -{dmg}（{q.unitHpCurrent}/{q.unitHpMax}）";
                        if (victim.CombatHp <= 0)
                        {
                            victim.isAlive = false;
                            q.morale.DrainAllyDown();
                            evResult += $" → {victim.name} 戦闘不能";
                        }
                        if (!q.formation.Any(a => a != null && a.isAlive))
                        {
                            q.failed = true;
                            evResult += " → 全滅失敗";
                        }
                        else if (q.morale.IsBroken)
                        {
                            q.retreated = true;
                            evResult += " → 士気崩壊で撤退";
                        }
                    }
                    break;
                }

            case DungeonEventType.Treasure:
                {
                    evTitle = "宝箱";
                    var loot = PickTreasure(q.def.Dungeon, ownedRelics);
                    if (loot == null)
                    {
                        evResult = "空っぽだった";
                    }
                    else
                    {
                        q.pendingLoot.Add(loot);
                        evResult = $"{RewardDescription.DescribeLoot(loot)} を発見（クリアで獲得）";
                    }
                    break;
                }

            case DungeonEventType.Gather:
                {
                    evTitle = "採取";
                    int got = GameRandom.Range(q.def.gatherMinPerEvent, q.def.gatherMaxPerEvent + 1);
                    got = Math.Max(0, got);
                    q.gatheredCount += got;
                    evResult = got <= 0
                        ? $"{q.def.gatherItemName} は見つからなかった（{q.gatheredCount}/{q.def.gatherTargetCount}）"
                        : $"{q.def.gatherItemName} を {got} 個採取（{q.gatheredCount}/{q.def.gatherTargetCount}）";
                    break;
                }

            default:
                evTitle = "進行"; evResult = "何も起きなかった"; break;
        }

        q.currentPhase = phase;
        q.logs.Add($"[Turn {currentTurn}] Phase {q.currentPhase}/{q.def.totalPhases}: {evTitle} - {evResult}");
        q.AddReportEvent(
            currentTurn,
            phase,
            ToReportKind(ev),
            evTitle,
            evResult,
            important: isBoss || ev is DungeonEventType.Trap or DungeonEventType.Treasure);

        // 最終フェーズに達しても目標数に届かない採取クエストは、残りをまとめて採取して達成扱いにする。
        if (!q.failed && q.def.IsGatherQuest && !q.GatherFulfilled && q.currentPhase >= q.def.totalPhases)
        {
            int rest = q.def.gatherTargetCount - q.gatheredCount;
            q.gatheredCount = q.def.gatherTargetCount;
            q.logs.Add($"[Turn {currentTurn}] 引き上げ間際に {q.def.gatherItemName} を {rest} 個かき集めた（{q.gatheredCount}/{q.def.gatherTargetCount}）");
        }

        if (!q.failed && q.def.IsGatherQuest && q.GatherFulfilled)
            q.logs.Add($"[Turn {currentTurn}] {q.def.gatherItemName} の必要数を確保。ギルドへ帰還できます");
        else if (!q.failed && q.currentPhase >= q.def.totalPhases)
            q.logs.Add($"[Turn {currentTurn}] クエスト完了！報酬を受け取れます");
    }

    static ExpeditionEventKind ToReportKind(DungeonEventType type) => type switch
    {
        DungeonEventType.EnemyEncounter => ExpeditionEventKind.Encounter,
        DungeonEventType.Heal => ExpeditionEventKind.Rest,
        DungeonEventType.Trap => ExpeditionEventKind.Trap,
        DungeonEventType.Treasure => ExpeditionEventKind.Treasure,
        DungeonEventType.Gather => ExpeditionEventKind.Gather,
        _ => ExpeditionEventKind.Progress,
    };

    // ダンジョンの重み表からフェーズごとに1イベント抽選。未設定なら Nothing。
    static DungeonEventType RollDungeonEvent(DungeonMasterData? d, int phase)
    {
        if (d == null || d.eventTable.Count == 0) return DungeonEventType.Nothing;

        int total = 0;
        foreach (var w in d.eventTable.Values) if (w > 0) total += w;
        if (total <= 0) return DungeonEventType.Nothing;

        int roll = GameRandom.Range(0, total);
        foreach (var kv in d.eventTable)
        {
            if (kv.Value <= 0) continue;
            roll -= kv.Value;
            if (roll < 0) return kv.Key;
        }
        return DungeonEventType.Nothing;
    }

    static QuestEventType ResolveQuestEvent(QuestMasterData q, int phase)
    {
        foreach (var e in q.fixedEvents)
            if (e.phase == phase) return e.type;
        return QuestEventType.None;
    }

    // フェーズ帯で絞った候補から重み付き抽選。
    static EnemyUnitTemplate? PickEncounter(DungeonMasterData? d, int phase)
    {
        if (d == null || d.encounterTable.Count == 0) return null;

        int total = 0;
        foreach (var e in d.encounterTable)
            if (e.Unit != null && e.weight > 0 && e.IsEligible(phase)) total += e.weight;
        if (total <= 0) return null;

        int roll = GameRandom.Range(0, total);
        foreach (var e in d.encounterTable)
        {
            if (e.Unit == null || e.weight <= 0 || !e.IsEligible(phase)) continue;
            roll -= e.weight;
            if (roll < 0) return e.Unit;
        }
        return null;
    }

    // 宝箱の重み表から戦利品を1件抽選。未設定なら null。
    // 所持済みの遺物は拾っても捨てるだけなので、抽選の対象から外す。
    // 選択イベントの宝箱もここを通す（QuestManager.ResolveChoice）。
    public static RewardEntryData? PickTreasure(DungeonMasterData? d, IReadOnlyCollection<RelicMasterData> ownedRelics)
    {
        if (d == null || d.treasureTable.Count == 0) return null;

        var candidates = d.treasureTable
            .Where(e => e.weight > 0 && !IsOwnedRelic(e, ownedRelics))
            .ToList();

        int total = 0;
        foreach (var e in candidates) total += e.weight;
        if (total <= 0) return null;

        int roll = GameRandom.Range(0, total);
        foreach (var e in candidates)
        {
            roll -= e.weight;
            if (roll < 0) return e;
        }
        return null;
    }

    static bool IsOwnedRelic(RewardEntryData e, IReadOnlyCollection<RelicMasterData> ownedRelics) =>
        e.type == RewardType.Relic && e.Relic != null && ownedRelics.Contains(e.Relic);

    static void RollEnemyDrops(QuestRun q, IEnumerable<EnemyData?> enemies, int phase)
    {
        foreach (var enemy in enemies.Where(e => e != null))
        {
            foreach (var entry in enemy!.master.dropTable)
            {
                if (!Hits(entry)) continue;
                var drop = CopyLoot(entry);
                q.pendingLoot.Add(drop);
                q.logs.Add($"  Phase {phase}: レアドロップ！ {enemy.master.baseName}から"
                    + $"{RewardDescription.DescribeLoot(drop)}{RewardDescription.DescribeQuantity(drop)}（帰還時に確定）");
            }
        }
    }

    // ボス撃破時の取り分。宝箱や敵ドロップと同じく pendingLoot に積み、帰還できたぶんだけ持ち帰る。
    // 物語進行に必要な報酬を落とすクエストは bossDropsAreGuaranteed で抽選を飛ばす。
    static void RollBossDrops(QuestRun q, int phase)
    {
        bool guaranteed = q.def.bossDropsAreGuaranteed;
        foreach (var entry in q.def.bossDrops)
        {
            if (!guaranteed && !Hits(entry)) continue;
            var drop = CopyLoot(entry);
            q.pendingLoot.Add(drop);
            q.logs.Add($"  Phase {phase}: ボスドロップ！ {RewardDescription.DescribeLoot(drop)}{RewardDescription.DescribeQuantity(drop)}（帰還時に確定）");
        }
    }

    static bool Hits(RewardEntryData entry) =>
        entry.chance > 0f && GameRandom.NextFloat() < entry.chance;

    static RewardEntryData CopyLoot(RewardEntryData entry) => new()
    {
        type = entry.type,
        relicId = entry.relicId,
        equipmentId = entry.equipmentId,
        skillId = entry.skillId,
        consumableId = entry.consumableId,
        gold = entry.gold,
        quantity = Math.Max(1, entry.quantity),
        unique = entry.unique,
        Relic = entry.Relic,
        Equipment = entry.Equipment,
        Skill = entry.Skill,
        Consumable = entry.Consumable,
    };

    static EnemyData?[] CreateEnemyMembers(EnemyUnitTemplate tpl, int level)
    {
        var arr = new EnemyData?[6];
        int lv = Math.Max(1, level);
        for (int i = 0; i < 6 && i < tpl.Formation.Count; i++)
        {
            var m = tpl.Formation[i];
            arr[i] = m != null ? new EnemyData(m, lv) : null;
        }
        return arr;
    }
}
