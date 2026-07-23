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

    public bool TryStartQuest(QuestMasterData def, AdventurerData?[] formation, int currentTurn, out string error)
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

        var run = new QuestRun(def, currentTurn);
        Array.Copy(formation, run.formation, Math.Min(formation.Length, 6));

        // クエスト開始時、編成メンバーのHPを最大値まで初期化する（オーラ/遺物の加算込み）。
        var perMember = UnitCalculator.CalcPerMember(run.formation.Cast<IUnitMember?>().ToArray());
        foreach (var (m, s) in perMember)
        {
            m.CombatHpMax = s.hp;
            m.CombatHp = s.hp;
        }

        // 士気の上限は編成の san 合計（＝mental の高さ）。粘り強さは編成で決まる。
        run.morale = new MoraleState(perMember.Sum(x => x.stats.san));

        activeQuests.Add(run);
        MarkBusy(run);
        questBoard.RemoveAll(e => e.quest == def);
        return true;
    }

    public void AdvanceAll(int currentTurn)
    {
        foreach (var q in activeQuests.ToList())
        {
            int steps = q.def.phasesPerTurn;
            for (int i = 0; i < steps && q.IsInProgress; i++)
                progressor.AdvanceOnePhase(q, currentTurn);

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
            if (a.isAlive) a.CombatHp = a.CombatHpMax;

        UnmarkBusy(q);
        activeQuests.Remove(q);
        questHistory.Add(q);
    }

    void MarkBusy(QuestRun q) { foreach (var a in q.EnumerateMembers()) busyIds.Add(a.id); }
    void UnmarkBusy(QuestRun q) { foreach (var a in q.EnumerateMembers()) busyIds.Remove(a.id); }
}
