using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Guild;

namespace GuildSimulator.Core.Systems.Quest;

public class QuestProgressor
{
    readonly GuildManager? guild;

    public QuestProgressor(GuildManager? guild = null) => this.guild = guild;

    public void AdvanceOnePhase(QuestRun q, int currentTurn)
    {
        int phase = q.currentPhase + 1;
        var qe = ResolveQuestEvent(q.def, phase);
        bool isBoss = (q.def.BossEnemy != null && phase == q.def.bossPhase)
                   || qe == QuestEventType.ForceBossEncounter;
        var partySkills = PartySkillEffects.Of(q.formation);

        DungeonEventType ev = RollDungeonEvent(
            q.def.Dungeon,
            phase,
            partySkills,
            q.treasureFromNothingPercent,
            q.enemyFromNothingPercent);

        if (qe == QuestEventType.ForceEnemyEncounter) ev = DungeonEventType.EnemyEncounter;
        else if (qe == QuestEventType.ForceHeal) ev = DungeonEventType.Heal;
        else if (qe == QuestEventType.ForceTrap) ev = DungeonEventType.Trap;
        else if (qe == QuestEventType.ForceTreasure) ev = DungeonEventType.Treasure;
        if (isBoss) ev = DungeonEventType.EnemyEncounter;

        // 採取判定はダンジョンイベントとは別枠。同じエリアで戦闘や宝箱と同時に起きる。
        bool gathers = q.def.IsGatherQuest && !q.GatherFulfilled && !isBoss
            && (qe == QuestEventType.ForceGather || GameRandom.NextFloat() < q.def.gatherChance);

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

                    // 敵の強さは倍率ではなくマスタの個体そのもの。深部で強い敵を出したいときは
                    // encounterTable の minPhase / maxPhase で置き場所を分ける。
                    evTitle = isBoss
                        ? $"ボス遭遇：{enemyTpl.unitName}"
                        : $"敵遭遇：{enemyTpl.unitName}(脅威度{Rank.Label(enemyTpl.Threat)})";

                    var enemyMembers = CreateEnemyMembers(enemyTpl);
                    var newlyDiscovered = enemyMembers
                        .Where(enemy => enemy != null)
                        .Select(enemy => enemy!.master)
                        .DistinctBy(enemy => enemy.id)
                        .Where(enemy => guild?.DiscoverEnemy(enemy) == true)
                        .Select(enemy => enemy.baseName)
                        .ToList();
                    if (newlyDiscovered.Count > 0)
                        q.logs.Add($"  エリア {phase}: モンスター図鑑に「{string.Join("」「", newlyDiscovered)}」を登録");

                    var advI = q.formation.Cast<IUnitMember?>().ToArray();
                    var enemyI = enemyMembers.Cast<IUnitMember?>().ToArray();

                    // 敵は毎回フレッシュな個体なのでここでHPを初期化する。冒険者側はクエスト開始時に
                    // 初期化済みで、エリアを跨いで持ち越す（QuestManager.TryStartQuest参照）。
                    foreach (var (m, s) in UnitCalculator.CalcPerMember(enemyI, isAllySide: false))
                    {
                        m.CombatHpMax = s.hp;
                        m.CombatHp = s.hp;
                    }

                    var result = BattleResolver.Resolve(
                        advI,
                        enemyI,
                        q.logs,
                        currentTurn,
                        phase,
                        q.morale,
                        q.policy,
                        q.ConsumableCombatBonusFor,
                        q.emergencyRetreatHpPercent,
                        q.recorder);

                    bool advWiped = !q.formation.Any(a => a != null && a.isAlive && !a.isIncapacitated);
                    bool enemyWiped = !enemyMembers.Any(e => e != null && e.isAlive);

                    if (result.adventurersRetreated)
                    {
                        q.retreated = true;
                        q.retreatReason = result.retreatReason;
                        evResult = $"{RetreatReasonText(result.retreatReason)}（HP {q.unitHpCurrent}/{q.unitHpMax} 士気 {q.morale.Current}/{q.morale.Max}）→ 引き上げ";
                    }
                    else if (advWiped)
                    {
                        q.failed = true;
                        evResult = "全員戦闘不能 → 失敗";
                    }
                    else if (enemyWiped)
                    {
                        int rally = q.morale.RestoreRate(MoraleState.VictoryRecoverRate);
                        evResult = $"勝利（HP {q.unitHpCurrent}/{q.unitHpMax} 士気 {q.morale.Current}/{q.morale.Max}）";
                        if (rally > 0) q.logs.Add($"  エリア {phase}: 勝利で士気を持ち直した（+{rally}）");
                        int totalExp = (int)Math.Floor(
                            enemyMembers.Sum(e => e?.master.exp ?? 0)
                            * (1f + q.expRewardBonusPercent / 100f)
                            * (1f + q.battleExpBonusPercent / 100f)
                            * partySkills.ExpMultiplier);
                        if (isBoss)
                        {
                            q.bossDefeated = true;
                            q.bossFinisherAdventurerId = result.finishingAdventurerId;
                            var finisher = q.formation.FirstOrDefault(
                                adventurer => adventurer?.id == q.bossFinisherAdventurerId);
                            string finisherText = finisher == null ? "" : $"（とどめ: {finisher.name}）";
                            q.logs.Add($"  エリア {phase}: ボス撃破！{finisherText}");
                            AddChest(q, TreasureChestKind.Boss, phase);
                        }
                        var participants = q.formation
                            .Where(a => a != null && a.isAlive && !a.isIncapacitated)
                            .Select(a => a!)
                            .ToList();
                        var levelUpSummaries = new List<string>();
                        for (int participantIndex = 0; participantIndex < participants.Count; participantIndex++)
                        {
                            var a = participants[participantIndex];
                            int earnedExp = ExperienceRewardSplitter.ShareFor(
                                totalExp, participants.Count, participantIndex);
                            int levelBefore = a.level;
                            if (a.AddExperience(earnedExp, out var ups, out var grownStats))
                            {
                                if (ups > 0) q.RecordLevelGrowth(a.id, grownStats);
                                string levelUpText = ups > 0
                                    ? $"（レベルアップ {levelBefore}lv→{a.level}lv、{QuestManager.FormatGrownStats(grownStats)}）"
                                    : "";
                                q.logs.Add($"  {a.name} 経験値 +{earnedExp}{levelUpText}");
                                if (ups > 0)
                                    levelUpSummaries.Add($"{a.name} Lv{levelBefore}→{a.level} {QuestManager.FormatGrownStats(grownStats)}");
                            }
                        }
                        // ターン進行サマリーはこのイベント結果を表示するため、成長をここにも含める。
                        if (levelUpSummaries.Count > 0)
                            evResult += $" / 成長: {string.Join("、", levelUpSummaries)}";
                        RollEnemyDrops(q, enemyMembers, phase, partySkills);
                    }
                    break;
                }

            case DungeonEventType.Heal:
                {
                    evTitle = "休息";
                    int before = q.unitHpCurrent;
                    float restMul = RelicSystem.GetRestHealMultiplier()
                        * FacilitySystem.GetRestHealMultiplier()
                        * (1f + q.restHealBonusPercent / 100f)
                        * partySkills.RestHealMultiplier;
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
                    var alive = q.formation
                        .Where(a => a != null && a.isAlive && !a.isIncapacitated)
                        .Select(a => a!).ToList();
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
                            victim.RegisterKnockout(severity: 2);
                            q.morale.DrainAllyDown();
                            evResult += $" → {victim.name} 戦闘不能（帰還時に負傷判定）";
                        }
                        if (!q.formation.Any(a => a != null && a.isAlive && !a.isIncapacitated))
                        {
                            q.failed = true;
                            evResult += " → 全員戦闘不能で失敗";
                        }
                        else if (q.morale.IsBroken)
                        {
                            q.retreated = true;
                            q.retreatReason = ExpeditionRetreatReason.MoraleBroken;
                            evResult += " → 士気崩壊で撤退";
                        }
                        else if (q.IsEmergencyRetreatThresholdReached)
                        {
                            q.retreated = true;
                            q.retreatReason = ExpeditionRetreatReason.SmokeBomb;
                            evResult += " → 機関の煙玉を展開して撤退";
                        }
                    }
                    break;
                }

            case DungeonEventType.Treasure:
                {
                    evTitle = "宝箱";
                    AddChest(q, TreasureChestKind.Dungeon, phase);
                    evResult = "封の固い宝箱を担ぎ上げた（帰還後に開封）";
                    break;
                }

            default:
                evTitle = "進行"; evResult = "何も起きなかった"; break;
        }

        q.currentPhase = phase;
        q.logs.Add($"[Turn {currentTurn}] エリア {q.currentPhase}/{q.PhaseLimit}: {evTitle} - {evResult}");
        q.AddReportEvent(
            currentTurn,
            phase,
            ToReportKind(ev),
            evTitle,
            evResult,
            important: isBoss || ev is DungeonEventType.Trap or DungeonEventType.Treasure);

        // 道中で何が起きたかに関わらず、生きて動けていれば採取そのものは進む。
        if (gathers && !q.failed && !q.retreated)
        {
            int got = Math.Max(0, GameRandom.Range(q.def.gatherMinPerEvent, q.def.gatherMaxPerEvent + 1));
            q.gatheredCount += got;
            string gatherResult = got <= 0
                ? $"{q.def.gatherItemName} は見つからなかった（{q.gatheredCount}/{q.def.gatherTargetCount}）"
                : $"{q.def.gatherItemName} を {got} 個採取（{q.gatheredCount}/{q.def.gatherTargetCount}）";
            q.logs.Add($"[Turn {currentTurn}] エリア {q.currentPhase}/{q.PhaseLimit}: 採取 - {gatherResult}");
            q.AddReportEvent(currentTurn, phase, ExpeditionEventKind.Gather, "採取", gatherResult);
        }

        // 予定のエリアを使い切っても素材が足りないとき、パーティは勝手に引き返さず判断を仰ぐ。
        // 延ばすか引くかはプレイヤーが決める。延長の代価は「もう1ターン帰ってこない」ことそのもの
        // （そのぶんの維持費と、余分に踏むエリアぶんの遭遇リスク）。
        if (!q.failed && !q.retreated && q.def.IsGatherQuest
            && !q.GatherFulfilled && q.currentPhase >= q.PhaseLimit)
        {
            q.gatherDecisionPending = true;
            q.gatherDecisionTurn = currentTurn;
            q.logs.Add($"[Turn {currentTurn}] {q.def.gatherItemName} が目標数に届かないまま予定のエリアを使い切った"
                + $"（{q.gatheredCount}/{q.def.gatherTargetCount}）→ 続行するか引き上げるかの指示待ち");
            q.AddReportEvent(
                currentTurn,
                q.currentPhase,
                ExpeditionEventKind.Decision,
                "指示待ち",
                $"{q.def.gatherItemName} が {q.gatheredCount}/{q.def.gatherTargetCount}。"
                    + "パーティは現地に留まり、続行の可否を待っている。",
                important: true);
        }

        if (!q.failed && q.def.IsGatherQuest && q.GatherFulfilled)
            q.logs.Add($"[Turn {currentTurn}] {q.def.gatherItemName} の必要数を確保。ギルドへ帰還できます");
        else if (!q.failed && !q.retreated && !q.def.IsGatherQuest && q.currentPhase >= q.PhaseLimit)
            q.logs.Add($"[Turn {currentTurn}] クエスト完了！報酬を受け取れます");
    }

    static ExpeditionEventKind ToReportKind(DungeonEventType type) => type switch
    {
        DungeonEventType.EnemyEncounter => ExpeditionEventKind.Encounter,
        DungeonEventType.Heal => ExpeditionEventKind.Rest,
        DungeonEventType.Trap => ExpeditionEventKind.Trap,
        DungeonEventType.Treasure => ExpeditionEventKind.Treasure,
        _ => ExpeditionEventKind.Progress,
    };

    static string RetreatReasonText(ExpeditionRetreatReason reason) => reason switch
    {
        ExpeditionRetreatReason.MoraleBroken => "士気崩壊で撤退",
        ExpeditionRetreatReason.SurvivalPolicy => "生還優先の方針により撤退",
        ExpeditionRetreatReason.BattleStalemate => "長期戦を打ち切って撤退",
        ExpeditionRetreatReason.GatherTargetMissed => "採取目標未達で撤退",
        ExpeditionRetreatReason.SmokeBomb => "機関の煙玉で撤退",
        _ => "戦闘から撤退",
    };

    // ダンジョンの重み表からエリアごとに1イベント抽選。未設定なら Nothing。
    // 宝探しや罠の勘といったスキルは、この重みそのものを歪める。
    // 重みが0のイベント（そのダンジョンには存在しないもの）はスキルでも生やせない。
    static DungeonEventType RollDungeonEvent(
        DungeonMasterData? d,
        int phase,
        PartySkillEffects partySkills,
        int treasureFromNothingPercent,
        int enemyFromNothingPercent)
    {
        var weights = CalculateEventWeights(
            d, partySkills, treasureFromNothingPercent, enemyFromNothingPercent);
        if (weights.Count == 0) return DungeonEventType.Nothing;

        float total = weights.Values.Sum();
        if (total <= 0) return DungeonEventType.Nothing;

        float roll = GameRandom.NextFloat() * total;
        foreach (var kv in weights)
        {
            if (kv.Value <= 0) continue;
            roll -= kv.Value;
            if (roll < 0) return kv.Key;
        }
        return DungeonEventType.Nothing;
    }

    /// <summary>
    /// イベント抽選に使う重み。振り子と角笛は総量を変えず、Nothingだけを移し替える。
    /// そのため振り子を持っても敵・罠・休息の実確率は下がらない。
    /// </summary>
    public static IReadOnlyDictionary<DungeonEventType, float> CalculateEventWeights(
        DungeonMasterData? d,
        PartySkillEffects partySkills,
        int treasureFromNothingPercent,
        int enemyFromNothingPercent)
    {
        var weights = new Dictionary<DungeonEventType, float>();
        if (d == null) return weights;

        foreach (var kv in d.eventTable)
        {
            if (kv.Value <= 0) continue;
            weights[kv.Key] = kv.Value * partySkills.ChanceMultiplierFor(kv.Key);
        }

        float nothing = weights.GetValueOrDefault(DungeonEventType.Nothing);
        if (nothing <= 0) return weights;

        float treasureRequested = weights.ContainsKey(DungeonEventType.Treasure)
            ? nothing * Math.Clamp(treasureFromNothingPercent, 0, 100) / 100f
            : 0f;
        float enemyRequested = weights.ContainsKey(DungeonEventType.EnemyEncounter)
            ? nothing * Math.Clamp(enemyFromNothingPercent, 0, 100) / 100f
            : 0f;
        float requested = treasureRequested + enemyRequested;
        if (requested <= 0) return weights;

        float scale = requested > nothing ? nothing / requested : 1f;
        float treasureShift = treasureRequested * scale;
        float enemyShift = enemyRequested * scale;
        weights[DungeonEventType.Nothing] = Math.Max(0f, nothing - treasureShift - enemyShift);
        if (treasureShift > 0)
            weights[DungeonEventType.Treasure] += treasureShift;
        if (enemyShift > 0)
            weights[DungeonEventType.EnemyEncounter] += enemyShift;
        return weights;
    }

    static QuestEventType ResolveQuestEvent(QuestMasterData q, int phase)
    {
        foreach (var e in q.fixedEvents)
            if (e.phase == phase) return e.type;
        return QuestEventType.None;
    }

    // エリア帯で絞った候補から重み付き抽選。
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

    // 見つけた宝箱は中身を決めずに積む。開封は帰還後（QuestRewardService.OpenChests）。
    static void AddChest(QuestRun q, TreasureChestKind kind, int phase)
    {
        var chest = new TreasureChest { kind = kind, foundPhase = phase };
        q.chests.Add(chest);
        if (kind == TreasureChestKind.Boss)
            q.logs.Add($"  エリア {phase}: ボスの宝箱を手に入れた（帰還後に開封）");
    }

    static void RollEnemyDrops(
        QuestRun q,
        IEnumerable<EnemyData?> enemies,
        int phase,
        PartySkillEffects partySkills)
    {
        foreach (var enemy in enemies.Where(e => e != null))
        {
            foreach (var entry in enemy!.master.dropTable)
            {
                if (RelicSystem.IsFrozenRelicReward(entry)) continue;
                float finalChance = partySkills.EnemyDropChanceFor(entry);
                if (finalChance <= 0f || GameRandom.NextDropFloat() >= finalChance) continue;
                var drop = entry.Copy();
                q.pendingLoot.Add(drop);
                string skillNote = Math.Abs(finalChance - entry.chance) > 0.0001f
                    ? $"（解体補正 {entry.chance:P0}→{finalChance:P0}）"
                    : "";
                q.logs.Add($"  エリア {phase}: レアドロップ！ {enemy.master.baseName}から"
                    + $"{RewardDescription.DescribeLoot(drop)}{RewardDescription.DescribeQuantity(drop)}"
                    + $"{skillNote}（帰還時に確定）");
            }
        }
    }

    static EnemyData?[] CreateEnemyMembers(EnemyUnitTemplate tpl)
    {
        var arr = new EnemyData?[6];
        for (int i = 0; i < 6 && i < tpl.Formation.Count; i++)
        {
            var m = tpl.Formation[i];
            arr[i] = m != null ? new EnemyData(m) : null;
        }
        return arr;
    }
}
