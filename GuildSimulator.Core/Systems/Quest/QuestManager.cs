using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Guild;

namespace GuildSimulator.Core.Systems.Quest;

public class QuestManager
{
    public List<QuestRun> activeQuests = new();
    public List<QuestRun> questHistory = new();
    public List<QuestBoardEntry> questBoard = new();

    /// <summary>通常クエストの掲示枠数。</summary>
    public int NormalBoardCapacity = 3;

    /// <summary>通常枠とは別に掲示できる緊急クエストの最大枚数。</summary>
    public int EmergencyBoardCapacity = 1;
    public int BoardCapacity => NormalBoardCapacity + EmergencyBoardCapacity;

    /// <summary>受注されないまま掲示され続けた枠を差し替えるまでのターン数。</summary>
    public int BoardExpireTurns = 7;

    readonly GuildManager guild;
    readonly QuestProgressor progressor = new();
    readonly QuestRewardService rewardService = new();
    readonly HashSet<string> busyIds = new();

    // 一度きりのクエスト（緊急/昇格試験）はクリア後に再掲示しない。
    readonly HashSet<string> clearedOneShotIds = new();
    readonly HashSet<string> clearedQuestIds = new();
    readonly HashSet<string> discoveredClueIds = new();
    readonly HashSet<string> selectedBranchIds = new();

    public QuestManager(GuildManager guild) => this.guild = guild;

    public bool IsAdventurerBusy(string id) => busyIds.Contains(id);
    public bool HasPendingChoices => activeQuests.Any(q => q.HasPendingChoice);

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
        foreach (var group in (carriedConsumables ?? Array.Empty<ConsumableMasterData>()).GroupBy(x => x))
            if (guild.GetConsumableCount(group.Key) < group.Count())
            {
                error = $"消費アイテムが不足しています: {group.Key.displayName}";
                return false;
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
        foreach (var item in carriedConsumables ?? Array.Empty<ConsumableMasterData>())
        {
            if (!guild.TryConsumeConsumable(item))
            {
                error = $"消費アイテムを消費できませんでした: {item.displayName}";
                return false;
            }
            run.ApplyConsumable(item);
            run.logs.Add($"[出発準備] {item.displayName}: {item.description}");
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
            if (q.HasPendingChoice) continue;
            int steps = q.def.phasesPerTurn;
            for (int i = 0; i < steps && q.IsInProgress; i++)
                progressor.AdvanceOnePhase(q, currentTurn);

            if (q.IsInProgress)
            {
                var choiceEvent = PickTurnEndEvent(q.def.Dungeon);
                if (choiceEvent != null)
                {
                    q.pendingChoice = new PendingQuestChoice { Event = choiceEvent, createdTurn = currentTurn };
                    q.logs.Add($"[Turn {currentTurn}] 選択イベント発生: {choiceEvent.title}");
                }
            }

            // ランクポイント・昇格は正規クリアのみ。撤退では得られない。
            if (q.IsCleared && !q.clearProgressApplied)
            {
                q.clearProgressApplied = true;
                foreach (var a in q.EnumerateMembers())
                {
                    a.OnClearQuest(q.def.rank);
                    a.AddRankPoints(a.CalcRankPointGain(q.def.rank), out _);
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

    public bool ResolveChoice(QuestRun q, int optionIndex, out string result)
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
        string detail = "";
        switch (option.effectType)
        {
            case GuildSimulator.Core.Models.QuestChoiceEffectType.Morale:
                int moraleChange = option.value >= 0 ? q.morale.Restore(option.value) : -q.morale.Drain(-option.value);
                detail = $"パーティ士気 {(moraleChange >= 0 ? "+" : "")}{moraleChange}（{q.morale.Current}/{q.morale.Max}）";
                break;
            case GuildSimulator.Core.Models.QuestChoiceEffectType.HealPercent:
            {
                var changes = new List<string>();
                foreach (var a in q.EnumerateMembers().Where(a => a.isAlive))
                {
                    int before = a.CombatHp;
                    a.CombatHp = Math.Min(a.CombatHpMax,
                        a.CombatHp + (int)Math.Ceiling(a.CombatHpMax * option.value / 100f));
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
                        a.CombatHp - (int)Math.Ceiling(a.CombatHpMax * option.value / 100f));
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
                    a.AddExperience(option.value, out int levelUps);
                    changes.Add($"{a.name} 経験値+{option.value}" + (levelUps > 0 ? $"（Lv.{levelUps}アップ）" : ""));
                }
                detail = string.Join("、", changes);
                break;
            }
            case GuildSimulator.Core.Models.QuestChoiceEffectType.Gold:
                q.pendingLoot.Add(new RewardEntryData
                {
                    type = GuildSimulator.Core.Models.RewardType.Gold, gold = option.value, quantity = 1,
                });
                detail = $"ゴールド+{option.value}（帰還時に加算）";
                break;
            case GuildSimulator.Core.Models.QuestChoiceEffectType.Equipment:
                if (option.Equipment != null)
                {
                    int qty = Math.Max(1, option.value);
                    q.pendingLoot.Add(new RewardEntryData
                    {
                        type = GuildSimulator.Core.Models.RewardType.Equipment,
                        Equipment = option.Equipment, equipmentId = option.Equipment.id,
                        quantity = qty,
                    });
                    detail = $"装備「{option.Equipment.displayName}」x{qty} 入手（帰還時に加算）";
                }
                break;
            case GuildSimulator.Core.Models.QuestChoiceEffectType.Consumable:
                if (option.Consumable != null)
                {
                    int qty = Math.Max(1, option.value);
                    q.pendingLoot.Add(new RewardEntryData
                    {
                        type = GuildSimulator.Core.Models.RewardType.Consumable,
                        Consumable = option.Consumable, consumableId = option.Consumable.id,
                        quantity = qty,
                    });
                    detail = $"消費アイテム「{option.Consumable.displayName}」x{qty} 入手（帰還時に加算）";
                }
                break;
        }
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

    public List<RewardOption> GetPendingRewards(QuestRun q) =>
        rewardService.BuildRewardOptions(q, guild);

    public void FinalizeQuest(QuestRun q, RewardOption? chosenReward)
    {
        if (!q.baseRewardsApplied)
        {
            q.baseRewardsApplied = true;
            if (!q.failed)
            {
                rewardService.ApplyBaseRewards(q, guild, "[完了]");
                rewardService.ApplyPendingLoot(q, guild, "[完了]");
            }
        }
        // 選択報酬は正規クリアの取り分。撤退では選ばせない。
        if (chosenReward != null && !q.extraRewardTaken && !q.retreated)
        {
            q.extraRewardTaken = true;
            rewardService.ApplyChosenReward(q, chosenReward, guild);
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
