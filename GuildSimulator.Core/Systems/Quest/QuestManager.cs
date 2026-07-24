using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
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
        if (IsOneShot(q) && clearedOneShotIds.Contains(q.id)) return false;
        if (questBoard.Any(e => e.quest == q)) return false;
        if (activeQuests.Any(r => r.def == q)) return false;
        return true;
    }

    static bool IsOneShot(QuestMasterData q) => q.isEmergencyQuest || q.rankUpOnClear > 0;

    public bool TryStartQuest(
        QuestMasterData def,
        AdventurerData?[] formation,
        int currentTurn,
        out string error,
        IReadOnlyList<ConsumableMasterData>? carriedConsumables = null)
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
        Array.Copy(formation, run.formation, Math.Min(formation.Length, 6));

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
        switch (option.effectType)
        {
            case GuildSimulator.Core.Models.QuestChoiceEffectType.Morale:
                if (option.value >= 0) q.morale.Restore(option.value);
                else q.morale.Drain(-option.value);
                break;
            case GuildSimulator.Core.Models.QuestChoiceEffectType.HealPercent:
                foreach (var a in q.EnumerateMembers().Where(a => a.isAlive))
                    a.CombatHp = Math.Min(a.CombatHpMax,
                        a.CombatHp + (int)Math.Ceiling(a.CombatHpMax * option.value / 100f));
                break;
            case GuildSimulator.Core.Models.QuestChoiceEffectType.DamagePercent:
                foreach (var a in q.EnumerateMembers().Where(a => a.isAlive))
                    a.CombatHp = Math.Max(1,
                        a.CombatHp - (int)Math.Ceiling(a.CombatHpMax * option.value / 100f));
                break;
            case GuildSimulator.Core.Models.QuestChoiceEffectType.Experience:
                foreach (var a in q.EnumerateMembers().Where(a => a.isAlive))
                    a.AddExperience(option.value, out _);
                break;
            case GuildSimulator.Core.Models.QuestChoiceEffectType.Gold:
                q.pendingLoot.Add(new RewardEntryData
                {
                    type = GuildSimulator.Core.Models.RewardType.Gold, gold = option.value, quantity = 1,
                });
                break;
            case GuildSimulator.Core.Models.QuestChoiceEffectType.Equipment:
                if (option.Equipment != null)
                    q.pendingLoot.Add(new RewardEntryData
                    {
                        type = GuildSimulator.Core.Models.RewardType.Equipment,
                        Equipment = option.Equipment, equipmentId = option.Equipment.id,
                        quantity = Math.Max(1, option.value),
                    });
                break;
            case GuildSimulator.Core.Models.QuestChoiceEffectType.Consumable:
                if (option.Consumable != null)
                    q.pendingLoot.Add(new RewardEntryData
                    {
                        type = GuildSimulator.Core.Models.RewardType.Consumable,
                        Consumable = option.Consumable, consumableId = option.Consumable.id,
                        quantity = Math.Max(1, option.value),
                    });
                break;
        }
        result = option.resultText;
        q.logs.Add($"[選択] {option.text} - {option.resultText}");
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
        if (q.completed && IsOneShot(q.def)) clearedOneShotIds.Add(q.def.id);

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

    /// <summary>セーブデータからの復元専用。掲示板・進行中クエスト・出発中フラグをまとめて置き換える。</summary>
    public void RestoreState(List<QuestBoardEntry> board, List<QuestRun> active, IEnumerable<string> clearedOneShotIdsToRestore)
    {
        questBoard = board;
        activeQuests = active;

        clearedOneShotIds.Clear();
        foreach (var id in clearedOneShotIdsToRestore)
            clearedOneShotIds.Add(id);

        busyIds.Clear();
        foreach (var q in activeQuests)
            MarkBusy(q);
    }
}
