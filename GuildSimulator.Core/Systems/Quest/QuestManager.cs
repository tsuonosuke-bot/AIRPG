using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Guild;
using System.Linq;

namespace GuildSimulator.Core.Systems.Quest;

public class QuestManager
{
    public List<QuestRun> activeQuests = new();
    public List<QuestRun> questHistory = new();
    public List<QuestBoardEntry> questBoard = new();

    /// <summary>施設のボーナスを含まない、通常クエストの掲示枠の基本数。</summary>
    public int BaseNormalBoardCapacity = 3;

    /// <summary>通常クエストの掲示枠数（施設による増加分を含む）。</summary>
    public int NormalBoardCapacity => BaseNormalBoardCapacity + FacilitySystem.GetQuestBoardBonus();

    /// <summary>通常枠とは別に掲示できる緊急クエストの最大枚数。</summary>
    public int EmergencyBoardCapacity = 1;
    public int BoardCapacity => NormalBoardCapacity + EmergencyBoardCapacity;

    /// <summary>受注されないまま掲示され続けた枠を差し替えるまでのターン数。</summary>
    public int BoardExpireTurns = 7;

    readonly GuildManager guild;
    readonly QuestProgressor progressor;
    readonly QuestRewardService rewardService = new();
    readonly HashSet<string> busyIds = new();

    // 一度きりのクエスト（緊急/昇格試験）はクリア後に再掲示しない。
    readonly HashSet<string> clearedOneShotIds = new();
    readonly HashSet<string> clearedQuestIds = new();
    readonly HashSet<string> discoveredClueIds = new();
    readonly HashSet<string> selectedBranchIds = new();

    public QuestManager(GuildManager guild)
    {
        this.guild = guild;
        progressor = new QuestProgressor(guild);
    }

    public bool IsAdventurerBusy(string id) => busyIds.Contains(id);
    public bool HasPendingChoices => activeQuests.Any(q => q.HasPendingChoice);

    /// <summary>
    /// 選択イベントと採取の続行判断をまとめたもの。どちらもパーティが現地で指示を待って
    /// 止まっている状態なので、ターンを進める前に片付けさせる。
    /// </summary>
    public bool HasPendingDecisions =>
        activeQuests.Any(q => q.HasPendingChoice || q.HasGatherDecision);

    // ---- クエストボード ----

    /// <summary>
    /// 期限切れの枠を捨て、空きをギルドランク以下のクエストで埋める。ターン開始時に呼ぶ。
    /// </summary>
    public void RefreshBoard(IEnumerable<QuestMasterData> pool, int currentTurn)
    {
        questBoard.RemoveAll(e => e.IsExpired(currentTurn, BoardExpireTurns));
        FillBoard(pool, currentTurn);
    }

    /// <summary>空きスロットだけを補充する（既存の掲示は据え置き）。</summary>
    public void FillBoard(IEnumerable<QuestMasterData> pool, int currentTurn)
    {
        var candidates = pool.Where(IsPostable).ToList();
        var normalCandidates = candidates.Where(q => !q.isEmergencyQuest).ToList();

        while (questBoard.Count(e => !e.quest.isEmergencyQuest) < NormalBoardCapacity
            && normalCandidates.Count > 0)
        {
            int i = GameRandom.Range(0, normalCandidates.Count);
            questBoard.Add(new QuestBoardEntry(normalCandidates[i], currentTurn));
            normalCandidates.RemoveAt(i);
        }

        var emergencyCandidates = candidates.Where(q => q.isEmergencyQuest).ToList();
        while (questBoard.Count(e => e.quest.isEmergencyQuest) < EmergencyBoardCapacity
            && emergencyCandidates.Count > 0)
        {
            int i = GameRandom.Range(0, emergencyCandidates.Count);
            questBoard.Add(new QuestBoardEntry(emergencyCandidates[i], currentTurn));
            emergencyCandidates.RemoveAt(i);
        }
    }

    // 掲示条件: 必要GP・ギルドランクを満たす / 掲示済みでも受注中でもない / クリア済みの一度きりクエストでない。
    bool IsPostable(QuestMasterData q)
    {
        if (q.rank > guild.GuildRank) return false;
        if (guild.GuildPoints < q.requiredGuildPoints) return false;
        if (!MeetsStoryRequirements(q)) return false;
        if (IsOneShot(q) && clearedOneShotIds.Contains(q.id)) return false;
        if (questBoard.Any(e => e.quest == q)) return false;
        if (activeQuests.Any(r => r.def == q)) return false;
        return true;
    }

    bool MeetsStoryRequirements(QuestMasterData q) =>
        q.requiredQuestIds.All(clearedQuestIds.Contains)
        && q.requiredClueIds.All(discoveredClueIds.Contains);

    static bool IsOneShot(QuestMasterData q) =>
        q.isEmergencyQuest || q.rankUpOnClear > 0 || q.isStoryQuest;

    public bool TryStartQuest(
        QuestMasterData def,
        AdventurerData?[] formation,
        int currentTurn,
        out string error,
        IReadOnlyList<ConsumableMasterData>? carriedConsumables = null,
        ExpeditionPolicy policy = ExpeditionPolicy.ObjectiveFirst)
        => TryStartQuestCore(
            def,
            formation,
            currentTurn,
            out error,
            (carriedConsumables ?? Array.Empty<ConsumableMasterData>())
                .Select(item => new ConsumableUse(item))
                .ToList(),
            policy);

    public bool TryStartQuestWithConsumables(
        QuestMasterData def,
        AdventurerData?[] formation,
        int currentTurn,
        out string error,
        IReadOnlyList<ConsumableUse> carriedConsumables,
        ExpeditionPolicy policy = ExpeditionPolicy.ObjectiveFirst)
        => TryStartQuestCore(
            def, formation, currentTurn, out error, carriedConsumables, policy);

    bool TryStartQuestCore(
        QuestMasterData def,
        AdventurerData?[] formation,
        int currentTurn,
        out string error,
        IReadOnlyList<ConsumableUse> carriedConsumables,
        ExpeditionPolicy policy)
    {
        error = "";
        var members = formation.Where(a => a != null).ToArray();
        if (members.Length == 0) { error = "編成が空です"; return false; }
        foreach (var a in members)
        {
            if (a == null) continue;
            if (!a.isAlive) { error = $"{a.name} は死亡しています"; return false; }
            if (IsAdventurerBusy(a.id)) { error = $"{a.name} は別のクエストに出発中です"; return false; }
        }
        foreach (var group in carriedConsumables.GroupBy(x => x.item))
            if (guild.GetConsumableCount(group.Key) < group.Count())
            {
                error = $"消費アイテムが不足しています: {group.Key.displayName}";
                return false;
            }
        foreach (var use in carriedConsumables)
        {
            if (!use.item.RequiresTarget) continue;
            if (use.target == null || !members.Contains(use.target))
            {
                error = $"{use.item.displayName}の対象が編成メンバーから選ばれていません";
                return false;
            }
        }

        var run = new QuestRun(def, currentTurn);
        run.policy = policy;
        Array.Copy(formation, run.formation, Math.Min(formation.Length, 6));
        run.guildUpkeepAtStart = guild.EffectiveUpkeepPerTurn;
        foreach (var member in run.EnumerateMembers())
            run.startingLevels[member.id] = member.level;

        // クエスト開始時、編成メンバーのHPを最大値まで初期化する（オーラ/遺物の加算込み）。
        var perMember = UnitCalculator.CalcPerMember(run.formation.Cast<IUnitMember?>().ToArray(), isAllySide: true);
        foreach (var (m, s) in perMember)
        {
            m.CombatHpMax = s.hp;
            m.CombatHp = s.hp;
        }

        // 士気の上限は編成の san 合計（＝mental の高さ）。粘り強さは編成で決まる。
        run.morale = new MoraleState(perMember.Sum(x => x.stats.san));
        foreach (var use in carriedConsumables)
        {
            if (!guild.TryConsumeConsumable(use.item))
            {
                error = $"消費アイテムを消費できませんでした: {use.item.displayName}";
                return false;
            }
            run.ApplyConsumable(use.item, use.target);
            string target = use.target == null ? "" : $"（対象: {use.target.name}）";
            run.logs.Add($"[出発準備] {use.item.displayName}{target}: {use.item.description}");
        }
        run.AddReportEvent(
            currentTurn,
            0,
            ExpeditionEventKind.Departure,
            "ギルドを出発",
            $"遠征方針は「{PolicyName(policy)}」。{run.EnumerateMembers().Count()}名で任務へ向かった。",
            important: true);

        activeQuests.Add(run);
        MarkBusy(run);
        questBoard.RemoveAll(e => e.quest == def);
        return true;
    }

    public void AdvanceAll(int currentTurn)
    {
        foreach (var q in activeQuests.ToList())
        {
            if (q.HasPendingChoice || q.HasGatherDecision) continue;
            int steps = q.def.phasesPerTurn;
            for (int i = 0; i < steps && q.IsInProgress; i++)
                progressor.AdvanceOnePhase(q, currentTurn);

            if (q.IsInProgress)
                AppearanceSystem.TryRunHumanEncounter(q, currentTurn);

            if (q.IsInProgress)
            {
                var choiceEvent = PickTurnEndEvent(q.def.Dungeon);
                if (choiceEvent != null)
                {
                    q.pendingChoice = new PendingQuestChoice { Event = choiceEvent, createdTurn = currentTurn };
                    q.logs.Add($"[Turn {currentTurn}] 選択イベント発生: {choiceEvent.title}");
                }
            }

            // 習熟度・昇格は正規クリアのみ。撤退では得られない。
            if (q.IsCleared && !q.clearProgressApplied)
            {
                q.clearProgressApplied = true;
                foreach (var a in q.EnumerateMembers())
                {
                    var mastery = a.OnClearQuest(q.def.rank);
                    if (mastery.PointsGained > 0)
                    {
                        string className = a.currentClass?.className ?? "職業";
                        q.logs.Add($"[職業習熟] {a.name} {className} +{mastery.PointsGained}"
                            + $"（合計 習熟度 {mastery.TotalPoints}）");
                    }
                    if (mastery.UnlockedSkills.Count > 0)
                    {
                        string names = string.Join("」「", mastery.UnlockedSkills.Select(skill => skill.skillName));
                        string className = a.currentClass?.className ?? "職業";
                        string message = $"{a.name}がスキル「{names}」を習得"
                            + $"（{className}習熟度 {a.CurrentClassMastery}）";
                        q.logs.Add($"[スキル習得] {message}");
                        q.AddReportEvent(
                            currentTurn,
                            q.currentPhase,
                            ExpeditionEventKind.Progress,
                            "スキル習得",
                            message,
                            important: true,
                            actorName: a.name);
                    }
                    a.RecordQuestClearForRank(q.def.rank);
                    if (a.CanRankUp)
                        q.logs.Add($"[昇格可能] {a.name} は {Rank.Label(a.rank)}→{Rank.Label(a.rank + 1)} の条件を満たした。冒険者一覧から昇格させられる。");
                }
                if (q.def.rankUpOnClear > 0)
                    guild.RankUp(q.def.rankUpOnClear, $"緊急クエスト完了: {q.def.questName}");
            }
        }
    }

    static QuestChoiceEventMasterData? PickTurnEndEvent(DungeonMasterData? dungeon)
    {
        if (dungeon == null) return null;
        if (GameRandom.NextFloat() >= Math.Clamp(dungeon.turnEndEventChance, 0f, 1f)) return null;
        int total = dungeon.turnEndEvents.Where(e => e.weight > 0 && e.options.Count > 0).Sum(e => e.weight);
        if (total <= 0) return null;
        int roll = GameRandom.Range(0, total);
        foreach (var ev in dungeon.turnEndEvents)
        {
            if (ev.weight <= 0 || ev.options.Count == 0) continue;
            roll -= ev.weight;
            if (roll < 0) return ev;
        }
        return null;
    }

    /// <summary>
    /// 採取が目標に届かないまま予定エリアを使い切ったときの二択を解決する。
    ///
    /// 続行を選ぶと <c>phasesPerTurn</c> ぶんエリアが伸びる。伸びたぶんが進むのは次のターンなので、
    /// 延長の代価は「もう1ターン帰ってこない」こと——余分な維持費と、踏み足すエリアぶんの
    /// 遭遇・罠・士気の消耗——になる。回数制限はなく、届くまで何度でも聞く。
    /// </summary>
    public bool ResolveGatherDecision(QuestRun q, bool keepSearching, out string result)
    {
        result = "";
        if (!q.HasGatherDecision) { result = "採取の判断待ちではありません"; return false; }

        int currentTurn = q.gatherDecisionTurn > 0 ? q.gatherDecisionTurn : q.startedTurn;
        q.gatherDecisionPending = false;
        string progress = $"{q.def.gatherItemName} {q.gatheredCount}/{q.def.gatherTargetCount}";

        if (keepSearching)
        {
            int added = Math.Max(1, q.def.phasesPerTurn);
            q.extraPhases += added;
            q.gatherExtensions++;
            result = $"捜索を続けさせた（{progress}）。行程を {added}エリア延ばす"
                + $"（エリア {q.currentPhase}/{q.PhaseLimit}、延長 {q.gatherExtensions} 回目）";
            q.logs.Add($"[Turn {currentTurn}] 続行を指示。{result}");
            q.AddReportEvent(
                currentTurn,
                q.currentPhase,
                ExpeditionEventKind.Decision,
                "捜索続行",
                $"{progress} のまま帰るわけにはいかない。ギルドは滞在の延長を認めた"
                    + $"（延長 {q.gatherExtensions} 回目、エリア {q.PhaseLimit} まで）。",
                important: true);
            return true;
        }

        q.retreated = true;
        q.retreatReason = ExpeditionRetreatReason.GatherTargetMissed;
        result = $"引き上げを指示した（{progress}）";
        q.logs.Add($"[Turn {currentTurn}] 撤退を指示。{result}");
        q.AddReportEvent(
            currentTurn,
            q.currentPhase,
            ExpeditionEventKind.Retreat,
            "撤退",
            $"採取目標未達（{progress}）。ギルドの判断でパーティを引き上げさせた。",
            important: true);
        return true;
    }

    public bool ResolveChoice(QuestRun q, int optionIndex, out string result)
        => ResolveChoice(q, optionIndex, null, out result);

    /// <summary>
    /// 選択肢を解決する。targetsOneMember の選択肢では target に対象を渡す。
    /// 結果テーブルを持つ選択肢は、ここで初めて抽選される（＝対象を決めてから振る）。
    /// </summary>
    public bool ResolveChoice(QuestRun q, int optionIndex, AdventurerData? target, out string result)
    {
        result = "";
        var pending = q.pendingChoice;
        if (pending == null) { result = "選択待ちではありません"; return false; }
        if (optionIndex < 0 || optionIndex >= pending.Event.options.Count)
        {
            result = "無効な選択です";
            return false;
        }

        var option = pending.Event.options[optionIndex];
        if (option.targetsOneMember)
        {
            if (target == null || !target.isAlive || !q.EnumerateMembers().Contains(target))
            {
                result = "この選択には、生存している隊員を1人指定してください";
                return false;
            }
        }

        // 結果が確定しているスキル選択では、習得済みの隊員を選んで機会を失わせない。
        // ギャンブル型は結果がまだ分からないため、従来どおり解決後に重複を報告する。
        if (!option.IsGamble && option.Skill != null
            && target != null && target.AllLearnedSkills.Contains(option.Skill))
        {
            var livingMembers = q.EnumerateMembers().Where(member => member.isAlive).ToList();
            if (livingMembers.Any(member => !member.AllLearnedSkills.Contains(option.Skill)))
            {
                result = $"{target.name} はすでに「{option.Skill.skillName}」を身につけています。別の隊員を選んでください";
                return false;
            }

            bool hasLearnableAlternative = pending.Event.options
                .Where(candidate => !candidate.IsGamble && candidate.Skill != null)
                .Any(candidate => livingMembers.Any(member =>
                    !member.AllLearnedSkills.Contains(candidate.Skill!)));
            if (hasLearnableAlternative)
            {
                result = $"「{option.Skill.skillName}」は全員が習得済みです。別のスキルを選んでください";
                return false;
            }
            // 全候補を全員が習得済みなら、再発したイベントで進行不能にならないよう解決を許可する。
        }

        var outcome = PickOutcome(option);
        string detail = "";
        switch (outcome.effectType)
        {
            case GuildSimulator.Core.Models.QuestChoiceEffectType.Morale:
                int moraleChange = outcome.value >= 0 ? q.morale.Restore(outcome.value) : -q.morale.Drain(-outcome.value);
                detail = $"パーティ士気 {(moraleChange >= 0 ? "+" : "")}{moraleChange}（{q.morale.Current}/{q.morale.Max}）";
                break;
            case GuildSimulator.Core.Models.QuestChoiceEffectType.HealPercent:
            {
                var changes = new List<string>();
                foreach (var a in q.EnumerateMembers().Where(a => a.isAlive))
                {
                    int before = a.CombatHp;
                    a.CombatHp = Math.Min(a.CombatHpMax,
                        a.CombatHp + (int)Math.Ceiling(a.CombatHpMax * outcome.value / 100f));
                    int healed = a.CombatHp - before;
                    if (healed > 0) changes.Add($"{a.name} HP+{healed}");
                }
                detail = changes.Count > 0 ? string.Join("、", changes) : "HPの変化はなかった";
                break;
            }
            case GuildSimulator.Core.Models.QuestChoiceEffectType.DamagePercent:
            {
                var changes = new List<string>();
                foreach (var a in q.EnumerateMembers().Where(a => a.isAlive))
                {
                    int before = a.CombatHp;
                    a.CombatHp = Math.Max(1,
                        a.CombatHp - (int)Math.Ceiling(a.CombatHpMax * outcome.value / 100f));
                    int lost = before - a.CombatHp;
                    if (lost > 0) changes.Add($"{a.name} HP-{lost}");
                }
                detail = changes.Count > 0 ? string.Join("、", changes) : "HPの変化はなかった";
                break;
            }
            case GuildSimulator.Core.Models.QuestChoiceEffectType.Experience:
            {
                var changes = new List<string>();
                foreach (var a in q.EnumerateMembers().Where(a => a.isAlive))
                {
                    int levelBefore = a.level;
                    a.AddExperience(outcome.value, out int levelUps, out var grownStats);
                    changes.Add($"{a.name} 経験値+{outcome.value}"
                        + (levelUps > 0
                            ? $"（レベルアップ {levelBefore}lv→{a.level}lv、{FormatGrownStats(grownStats)}）"
                            : ""));
                }
                detail = string.Join("、", changes);
                break;
            }
            case GuildSimulator.Core.Models.QuestChoiceEffectType.Gold:
                q.pendingLoot.Add(new RewardEntryData
                {
                    type = GuildSimulator.Core.Models.RewardType.Gold, gold = outcome.value, quantity = 1,
                });
                detail = $"ゴールド+{outcome.value}（帰還時に加算）";
                break;
            case GuildSimulator.Core.Models.QuestChoiceEffectType.Equipment:
                if (outcome.Equipment != null)
                {
                    int qty = Math.Max(1, outcome.value);
                    q.pendingLoot.Add(new RewardEntryData
                    {
                        type = GuildSimulator.Core.Models.RewardType.Equipment,
                        Equipment = outcome.Equipment, equipmentId = outcome.Equipment.id,
                        quantity = qty,
                    });
                    detail = $"装備「{outcome.Equipment.displayName}」x{qty} 入手（帰還時に加算）";
                }
                break;
            case GuildSimulator.Core.Models.QuestChoiceEffectType.Consumable:
                if (outcome.Consumable != null)
                {
                    int qty = Math.Max(1, outcome.value);
                    q.pendingLoot.Add(new RewardEntryData
                    {
                        type = GuildSimulator.Core.Models.RewardType.Consumable,
                        Consumable = outcome.Consumable, consumableId = outcome.Consumable.id,
                        quantity = qty,
                    });
                    detail = $"消費アイテム「{outcome.Consumable.displayName}」x{qty} 入手（帰還時に加算）";
                }
                break;
            case GuildSimulator.Core.Models.QuestChoiceEffectType.Treasure:
            {
                int count = Math.Max(1, outcome.value);
                for (int i = 0; i < count; i++)
                    q.chests.Add(new TreasureChest
                    {
                        kind = GuildSimulator.Core.Models.TreasureChestKind.Dungeon,
                        foundPhase = q.currentPhase,
                    });
                detail = $"宝箱 x{count} を持ち帰った（帰還後に開封）";
                break;
            }
            case GuildSimulator.Core.Models.QuestChoiceEffectType.AdventurerStatUp:
            case GuildSimulator.Core.Models.QuestChoiceEffectType.AdventurerStatDown:
            {
                bool up = outcome.effectType
                    == GuildSimulator.Core.Models.QuestChoiceEffectType.AdventurerStatUp;
                int amount = Math.Max(1, outcome.value) * (up ? 1 : -1);
                var stat = ResolveStatType(outcome.targetId);
                int applied = target!.AdjustStatPermanently(stat, amount);
                detail = applied == 0
                    ? $"{target.name} は何も変わらなかった"
                    : $"{target.name} の{StatDisplayName(stat)} {applied:+#;-#;0}（恒久）";
                break;
            }
            case GuildSimulator.Core.Models.QuestChoiceEffectType.AdventurerSkill:
            {
                if (outcome.Skill == null) { detail = "何も起こらなかった"; break; }
                detail = target!.LearnPermanentSkill(outcome.Skill)
                    ? $"{target.name} がスキル「{outcome.Skill.skillName}」を習得"
                    : $"{target.name} はすでに「{outcome.Skill.skillName}」を身につけていた";
                break;
            }
            case GuildSimulator.Core.Models.QuestChoiceEffectType.AdventurerDamage:
            {
                int before = target!.CombatHp;
                target.CombatHp = Math.Max(1,
                    target.CombatHp - (int)Math.Ceiling(target.CombatHpMax * Math.Max(0, outcome.value) / 100f));
                int lost = before - target.CombatHp;
                detail = lost > 0 ? $"{target.name} HP-{lost}" : $"{target.name} は無傷だった";
                break;
            }
        }

        // 結果テーブルを引いた選択肢は、どれを引いたかの一文を先に見せる。
        if (outcome.resultText.Length > 0)
            detail = detail.Length > 0 ? $"{outcome.resultText}\n  → {detail}" : outcome.resultText;
        result = detail.Length > 0 ? $"{option.resultText}\n  → {detail}" : option.resultText;
        q.logs.Add($"[選択] {option.text} - {option.resultText}" + (detail.Length > 0 ? $" ({detail})" : ""));
        q.AddReportEvent(
            pending.createdTurn,
            q.currentPhase,
            ExpeditionEventKind.Decision,
            pending.Event.title,
            $"{option.text}。{option.resultText}" + (detail.Length > 0 ? $" {detail}" : ""),
            important: true);
        q.pendingChoice = null;
        return true;
    }

    /// <summary>結果テーブルから1件を重み付きで引く。表が無い選択肢は選択肢自身が1件になる。</summary>
    static QuestChoiceOutcome PickOutcome(QuestChoiceOptionData option)
    {
        var outcomes = option.Outcomes;
        if (outcomes.Count == 1) return outcomes[0];

        int total = outcomes.Sum(o => Math.Max(0, o.weight));
        if (total <= 0) return outcomes[0];

        int roll = GameRandom.Range(0, total);
        foreach (var o in outcomes)
        {
            roll -= Math.Max(0, o.weight);
            if (roll < 0) return o;
        }
        return outcomes[^1];
    }

    /// <summary>targetIdから能力を決める。空や不明ならランダムに選ぶ。</summary>
    static GuildSimulator.Core.Models.StatType ResolveStatType(string targetId)
    {
        if (!string.IsNullOrWhiteSpace(targetId)
            && Enum.TryParse<GuildSimulator.Core.Models.StatType>(targetId, ignoreCase: true, out var parsed)
            && AdventurerData.GrowableStats.Contains(parsed))
            return parsed;

        var pool = AdventurerData.GrowableStats;
        return pool[GameRandom.Range(0, pool.Count)];
    }

    public static string StatDisplayName(GuildSimulator.Core.Models.StatType t) => t switch
    {
        GuildSimulator.Core.Models.StatType.Vitality => "体力",
        GuildSimulator.Core.Models.StatType.Mental => "精神力",
        GuildSimulator.Core.Models.StatType.Strength => "筋力",
        GuildSimulator.Core.Models.StatType.Agility => "敏捷",
        GuildSimulator.Core.Models.StatType.Intelligence => "知力",
        GuildSimulator.Core.Models.StatType.Constitution => "体格",
        _ => t.ToString(),
    };

    /// <summary>レベルアップで伸びた能力の一覧を「体力+1、敏捷+1」のように表示用にまとめる。</summary>
    public static string FormatGrownStats(IEnumerable<GuildSimulator.Core.Models.StatType> grownStats)
        => string.Join("、", grownStats
            .GroupBy(t => t)
            .Select(g => $"{StatDisplayName(g.Key)}+{g.Count()}"));

    public void FinalizeQuest(QuestRun q)
    {
        if (!q.baseRewardsApplied)
        {
            q.baseRewardsApplied = true;
            if (!q.failed)
            {
                rewardService.ApplyBaseRewards(q, guild, "[完了]");
                // 宝箱は持ち帰ってから開ける。中身が決まるのはこの瞬間。
                rewardService.OpenChests(q, guild, "[完了]");
                rewardService.ApplyPendingLoot(q, guild, "[完了]");
            }
        }
        q.rewarded = true;
        q.completed = q.IsCleared;
        if (q.completed)
        {
            clearedQuestIds.Add(q.def.id);
            if (IsOneShot(q.def)) clearedOneShotIds.Add(q.def.id);
            if (!string.IsNullOrWhiteSpace(q.def.storyBranchId))
                selectedBranchIds.Add(q.def.storyBranchId);

            int reportTurn = q.reportEvents.LastOrDefault()?.turn ?? q.startedTurn;
            foreach (var clueId in q.def.grantedClueIds)
            {
                if (!discoveredClueIds.Add(clueId)) continue;
                q.discoveredClueIds.Add(clueId);
                string clueTitle = q.def.GrantedClues
                    .FirstOrDefault(clue => clue.id == clueId)?.title ?? clueId;
                q.AddReportEvent(
                    reportTurn,
                    q.currentPhase,
                    ExpeditionEventKind.Discovery,
                    "新たな手掛かり",
                    $"調査記録「{clueTitle}」をギルドへ持ち帰った。",
                    important: true,
                    clueId: clueId);
            }
            q.AddReportEvent(
                reportTurn,
                q.currentPhase,
                ExpeditionEventKind.Completion,
                "依頼達成",
                $"{q.def.questName}を完遂し、ギルドへ帰還した。",
                important: true);
        }
        else if (q.retreated)
        {
            int reportTurn = q.reportEvents.LastOrDefault()?.turn ?? q.startedTurn;
            q.AddReportEvent(
                reportTurn,
                q.currentPhase,
                ExpeditionEventKind.Retreat,
                "撤退",
                "生存者は任務の続行を断念し、ギルドへ帰還した。",
                important: true);
        }

        // 戦闘不能は即死ではない。帰還時に、任務の壊滅状況と医療院の効果を踏まえて
        // 死亡または治療可能な負傷へ確定する。
        int injuryReportTurn = q.reportEvents.LastOrDefault()?.turn ?? q.startedTurn;
        foreach (var a in q.EnumerateMembers().Where(a => a.isAlive && a.isIncapacitated))
        {
            var trauma = a.ResolvePendingTrauma(
                partyWiped: q.failed,
                fatalityReductionPercent: FacilitySystem.GetFatalityReductionPercent());
            q.logs.Add($"[帰還処理] {trauma.Message}");
            q.AddReportEvent(
                injuryReportTurn,
                q.currentPhase,
                ExpeditionEventKind.Injury,
                trauma.Died ? "死亡確認" : "負傷者帰還",
                trauma.Message,
                important: true,
                actorName: a.name);
        }

        string expeditionResult = q.completed ? "成功" : q.retreated ? "撤退" : "失敗";
        foreach (var a in q.EnumerateMembers())
            a.RecordExpedition(q.def.questName, expeditionResult);

        // ギルドに帰還した生存メンバーは全快させる（死亡者は蘇生しない）。
        foreach (var a in q.EnumerateMembers())
            if (a.isAlive)
            {
                // クエスト限定の最大HP補正を帰還時に破棄する。0は画面側で平常時最大HPを再計算する印。
                a.CombatHp = 0;
                a.CombatHpMax = 0;
            }

        UnmarkBusy(q);
        activeQuests.Remove(q);
        questHistory.Add(q);
    }

    void MarkBusy(QuestRun q) { foreach (var a in q.EnumerateMembers()) busyIds.Add(a.id); }
    void UnmarkBusy(QuestRun q) { foreach (var a in q.EnumerateMembers()) busyIds.Remove(a.id); }

    // ---- セーブ/ロード ----
    public IReadOnlyCollection<string> ExportClearedOneShotIds() => clearedOneShotIds;
    public IReadOnlyCollection<string> ExportClearedQuestIds() => clearedQuestIds;
    public IReadOnlyCollection<string> ExportDiscoveredClueIds() => discoveredClueIds;
    public IReadOnlyCollection<string> ExportSelectedBranchIds() => selectedBranchIds;

    public bool HasClearedQuest(string id) => clearedQuestIds.Contains(id);
    public bool HasDiscoveredClue(string id) => discoveredClueIds.Contains(id);

    /// <summary>セーブデータからの復元専用。掲示板・進行中クエスト・出発中フラグをまとめて置き換える。</summary>
    public void RestoreState(
        List<QuestBoardEntry> board,
        List<QuestRun> active,
        IEnumerable<string> clearedOneShotIdsToRestore,
        IEnumerable<string>? clearedQuestIdsToRestore = null,
        IEnumerable<string>? discoveredClueIdsToRestore = null,
        IEnumerable<string>? selectedBranchIdsToRestore = null)
    {
        questBoard = board;
        activeQuests = active;

        clearedOneShotIds.Clear();
        foreach (var id in clearedOneShotIdsToRestore)
            clearedOneShotIds.Add(id);

        clearedQuestIds.Clear();
        var restoredClears = (clearedQuestIdsToRestore ?? Array.Empty<string>()).ToList();
        if (restoredClears.Count == 0)
            restoredClears.AddRange(clearedOneShotIds);
        foreach (var id in restoredClears)
            clearedQuestIds.Add(id);

        discoveredClueIds.Clear();
        foreach (var id in discoveredClueIdsToRestore ?? Array.Empty<string>())
            discoveredClueIds.Add(id);

        selectedBranchIds.Clear();
        foreach (var id in selectedBranchIdsToRestore ?? Array.Empty<string>())
            selectedBranchIds.Add(id);

        questBoard.RemoveAll(entry =>
            !MeetsStoryRequirements(entry.quest)
            || (IsOneShot(entry.quest) && clearedOneShotIds.Contains(entry.quest.id)));

        busyIds.Clear();
        foreach (var q in activeQuests)
            MarkBusy(q);
    }

    public static string PolicyName(ExpeditionPolicy policy) => policy switch
    {
        ExpeditionPolicy.SurvivalFirst => "生還優先",
        ExpeditionPolicy.ObjectiveFirst => "依頼達成優先",
        _ => policy.ToString(),
    };
}
