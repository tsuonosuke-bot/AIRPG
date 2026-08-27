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
    /// <summary>
    /// WebのlocalStorageを含むセーブ領域が無制限に増えないよう保持する完了履歴の上限。
    /// </summary>
    public const int QuestHistoryLimit = 30;
    public const int QuestHistoryLogCharacterBudget =
        QuestHistoryLimit * QuestHistoryEntry.MaxLogCharacters;

    public List<QuestRun> activeQuests = new();
    readonly List<QuestHistoryEntry> questHistory = new();
    public IReadOnlyList<QuestHistoryEntry> QuestHistory => questHistory;
    public List<QuestBoardEntry> questBoard = new();

    /// <summary>施設のボーナスを含まない、通常クエストの掲示枠の基本数。</summary>
    public int BaseNormalBoardCapacity = 3;

    /// <summary>通常クエストの掲示枠数（施設による増加分を含む）。</summary>
    public int NormalBoardCapacity => BaseNormalBoardCapacity + FacilitySystem.GetQuestBoardBonus();

    /// <summary>施設が通常枠とは別に確保する、新人向けFランク依頼枠。</summary>
    public int NoviceBoardCapacity => FacilitySystem.GetNoviceQuestBoardBonus();

    /// <summary>通常枠とは別に掲示できる緊急クエストの最大枚数。</summary>
    public int EmergencyBoardCapacity = 1;

    /// <summary>通常依頼の抽選に埋もれない、物語依頼の継続掲示枠。</summary>
    public int StoryBoardCapacity = 1;

    public int BoardCapacity =>
        NormalBoardCapacity + NoviceBoardCapacity + EmergencyBoardCapacity + StoryBoardCapacity;

    /// <summary>受注されないまま掲示され続けた枠を差し替えるまでのターン数。</summary>
    public int BoardExpireTurns = 7;

    /// <summary>
    /// 特性のマスタ。帰還時の開花判定に使う。設定されていなければ特性は一切開花しないので、
    /// マスタを読み込んだホスト側が起動時とロード時に渡す。
    /// </summary>
    public IReadOnlyList<TraitMasterData> traitCatalog = Array.Empty<TraitMasterData>();

    readonly GuildManager guild;
    readonly QuestProgressor progressor;
    readonly QuestRewardService rewardService = new();
    readonly HashSet<string> busyIds = new();

    // 一度きりのクエスト（緊急/昇格試験）はクリア後に再掲示しない。
    readonly HashSet<string> clearedOneShotIds = new();
    readonly HashSet<string> clearedQuestIds = new();
    // 通常クエストはクリア後、しばらく（5～10ターン）再掲示しない。questId -> 再掲示可能になるターン。
    readonly Dictionary<string, int> questCooldownUntilTurn = new();
    public const int NormalQuestCooldownMinTurns = 5;
    public const int NormalQuestCooldownMaxTurns = 10;
    readonly HashSet<string> discoveredClueIds = new();
    readonly List<string> discoveredClueOrder = new();
    readonly HashSet<string> selectedBranchIds = new();

    public const string BlueOreSealedBranchId = "blue_ore_sealed";
    public const string BlueOreStudiedBranchId = "blue_ore_studied";
    public const string BlueOreTradedBranchId = "blue_ore_traded";
    public const string BlueOreFinalQuestId = "quest_old_city_relic";

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
        // 物語依頼は新しい手掛かりそのものなので、受注するまで専用枠へ残す。
        questBoard.RemoveAll(e => !e.quest.isStoryQuest && e.IsExpired(currentTurn, BoardExpireTurns));
        FillBoard(pool, currentTurn);
    }

    /// <summary>空きスロットだけを補充する（既存の掲示は据え置き）。</summary>
    public void FillBoard(IEnumerable<QuestMasterData> pool, int currentTurn)
    {
        var poolList = pool.ToList();
        var candidates = poolList.Where(q => IsPostable(q, currentTurn)).ToList();
        var storyCandidates = candidates
            .Where(q => q.isStoryQuest && !q.isEmergencyQuest)
            .OrderBy(q => q.rank)
            .ThenBy(poolList.IndexOf)
            .ToList();
        while (questBoard.Count(e => e.quest.isStoryQuest) < StoryBoardCapacity
            && storyCandidates.Count > 0)
        {
            var chosen = storyCandidates[0];
            questBoard.Add(new QuestBoardEntry(chosen, currentTurn));
            storyCandidates.RemoveAt(0);
        }

        var normalCandidates = candidates
            .Where(q => !q.isEmergencyQuest && !q.isStoryQuest)
            .ToList();

        // ギルドランクが上がるほど候補総数が増え、F依頼が通常3枠の抽選から消えやすくなる。
        // 訓練所などの新人枠は先にF依頼を確保し、その後で通常枠を全ランクから補充する。
        var noviceCandidates = normalCandidates.Where(q => q.rank == Rank.Min).ToList();
        while (questBoard.Count(e => !e.quest.isEmergencyQuest && !e.quest.isStoryQuest
                && e.quest.rank == Rank.Min) < NoviceBoardCapacity
            && noviceCandidates.Count > 0)
        {
            int i = GameRandom.Range(0, noviceCandidates.Count);
            var chosen = noviceCandidates[i];
            questBoard.Add(new QuestBoardEntry(chosen, currentTurn));
            noviceCandidates.RemoveAt(i);
            normalCandidates.Remove(chosen);
        }

        int totalNormalCapacity = NormalBoardCapacity + NoviceBoardCapacity;
        while (questBoard.Count(e => !e.quest.isEmergencyQuest && !e.quest.isStoryQuest) < totalNormalCapacity
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

    // 掲示条件: 必要GP・ギルドランクを満たす / 掲示済みでも受注中でもない / クリア済みの一度きりクエストでない
    // / 通常クエストのクリア後クールダウン中でない。
    bool IsPostable(QuestMasterData q, int currentTurn)
    {
        if (q.rank > guild.GuildRank) return false;
        int availableGuildPoints = q.rankUpOnClear > 0
            ? guild.GuildPointsThisRank
            : guild.GuildPoints;
        if (availableGuildPoints < q.requiredGuildPoints) return false;
        if (!MeetsStoryRequirements(q)) return false;
        if (IsOneShot(q) && clearedOneShotIds.Contains(q.id)) return false;
        if (questCooldownUntilTurn.TryGetValue(q.id, out int cooldownUntil) && currentTurn < cooldownUntil)
            return false;
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
        if (formation.Length > GuildManager.FormationSlotCount)
        {
            error = $"編成データが最大{GuildManager.FormationSlotCount}枠を超えています";
            return false;
        }
        var members = formation.Where(a => a != null).ToArray();
        if (members.Length == 0) { error = "編成が空です"; return false; }
        if (members.Length > guild.PartyCapacity)
        {
            error = $"現在のパーティ編成上限は{guild.PartyCapacity}人です"
                + $"（編成枠強化 {guild.PartyCapacityUpgradeCount}/{GuildManager.PartyCapacityUpgradeMaximum}）";
            return false;
        }
        if (members.Select(member => member!.id).Distinct().Count() != members.Length)
        {
            error = "同じ冒険者を複数の配置枠へ編成することはできません";
            return false;
        }
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
        Array.Copy(formation, run.formation, Math.Min(formation.Length, run.formation.Length));
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
        string storyAftermath = ApplyStoryWorldEffects(run);
        run.AddReportEvent(
            currentTurn,
            0,
            ExpeditionEventKind.Departure,
            "ギルドを出発",
            $"遠征方針は「{PolicyName(policy)}」。{run.EnumerateMembers().Count()}名で任務へ向かった。",
            important: true);
        if (!string.IsNullOrWhiteSpace(storyAftermath))
        {
            run.logs.Add($"[物語の余波] {storyAftermath}");
            run.AddReportEvent(
                currentTurn,
                0,
                ExpeditionEventKind.Progress,
                "物語の余波",
                storyAftermath,
                important: true);
        }

        activeQuests.Add(run);
        MarkBusy(run);
        questBoard.RemoveAll(e => e.quest == def);
        return true;
    }

    string ApplyStoryWorldEffects(QuestRun run)
    {
        string dungeonId = run.def.Dungeon?.id ?? "";
        if (dungeonId is not ("dungeon_mine" or "dungeon_old_city")) return "";

        if (selectedBranchIds.Contains(BlueOreSealedBranchId))
        {
            run.trapDamageReductionPercent = Math.Clamp(run.trapDamageReductionPercent + 25, 0, 90);
            return "青い鉱脈を封じたことで地下の脈動が鎮まり、この遠征では罠ダメージを25%軽減する。";
        }
        if (selectedBranchIds.Contains(BlueOreStudiedBranchId))
        {
            run.expRewardBonusPercent += 10;
            run.battleExpBonusPercent += 10;
            return "王立古物院が公開した調査知識により、この遠征で得る経験値が10%増える。";
        }
        if (selectedBranchIds.Contains(BlueOreTradedBranchId))
        {
            run.goldRewardBonusPercent += 15;
            run.enemyFromNothingPercent = Math.Clamp(run.enemyFromNothingPercent + 10, 0, 100);
            return "鉱石交易の活況で報酬Goldが15%増える一方、採掘権を狙う敵との遭遇が増える。";
        }
        return "";
    }

    public void AdvanceAll(int currentTurn)
    {
        foreach (var q in activeQuests.ToList())
        {
            if (q.HasPendingChoice || q.HasGatherDecision) continue;
            if (TryQueueFixedChoice(q, currentTurn)) continue;

            int steps = q.def.phasesPerTurn;
            for (int i = 0; i < steps && q.IsInProgress; i++)
            {
                progressor.AdvanceOnePhase(q, currentTurn);
                if (!q.failed && !q.retreated && TryQueueFixedChoice(q, currentTurn)) break;
            }

            if (q.IsInProgress && !q.HasPendingChoice)
                AppearanceSystem.TryRunHumanEncounter(q, currentTurn);

            if (q.IsInProgress && !q.HasPendingChoice)
            {
                var choiceEvent = PickTurnEndEvent(q.def.Dungeon);
                if (choiceEvent != null)
                {
                    q.pendingChoice = new PendingQuestChoice { Event = choiceEvent, createdTurn = currentTurn };
                    q.logs.Add($"[Turn {currentTurn}] 選択イベント発生: {choiceEvent.title}");
                }
            }

            ApplyClearProgress(q, currentTurn);
        }
    }

    bool TryQueueFixedChoice(QuestRun q, int currentTurn)
    {
        if (q.HasPendingChoice || q.failed || q.retreated) return false;

        var fixedChoice = q.def.FixedChoiceEvents
            .Where(e => e.phase <= q.currentPhase)
            .Where(e => !q.resolvedFixedChoiceEventIds.Contains(e.ChoiceEvent!.id))
            .OrderBy(e => e.phase)
            .FirstOrDefault();
        if (fixedChoice?.ChoiceEvent == null) return false;

        q.pendingChoice = new PendingQuestChoice
        {
            Event = fixedChoice.ChoiceEvent,
            createdTurn = currentTurn,
        };
        q.logs.Add($"[Turn {currentTurn}] 物語イベント発生: {fixedChoice.ChoiceEvent.title}");
        return true;
    }

    // 習熟度・昇格は正規クリアのみ。固定選択の解決直後に手動帰還しても取りこぼさないよう、
    // ターン進行と帰還処理の両方から同じ処理を呼ぶ。
    void ApplyClearProgress(QuestRun q, int currentTurn)
    {
        if (!q.IsCleared || q.clearProgressApplied) return;

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
            else if (a.isAlive && a.currentClass != null
                && a.IsSuitableQuestRank(q.def.rank) && a.IsAtMasteryCap)
            {
                q.logs.Add($"[職業習熟] {a.name} {a.currentClass.className}は"
                    + $"{a.RankLabel}ランク上限{a.MasteryCap}（昇格まで成長停止）");
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
                    bool wasAtLevelCap = a.IsAtLevelCap;
                    a.AddExperience(outcome.value, out int levelUps, out var grownStats);
                    if (levelUps > 0) q.RecordLevelGrowth(a.id, grownStats);
                    string expText = wasAtLevelCap
                        ? $"経験値は{a.RankLabel}ランク上限Lv{a.LevelCap}のため蓄積されない"
                        : $"経験値+{outcome.value}";
                    changes.Add($"{a.name} {expText}"
                        + (levelUps > 0
                            ? $"（レベルアップ {levelBefore}lv→{a.level}lv、{FormatGrownStats(grownStats)}）"
                            : ""));
                }
                detail = string.Join("、", changes);
                break;
            }
            case GuildSimulator.Core.Models.QuestChoiceEffectType.Gold:
            {
                // F帯のイベント金額を基準に、依頼ランク相応の「見つけ物」にする。
                // 収入は帰還時、損失はその場で反映する。失敗しても支払いや強奪だけが消えないようにする。
                int eventGold = outcome.value * QuestEventGoldMultiplier(q.def.rank);
                if (eventGold >= 0)
                {
                    q.pendingLoot.Add(new RewardEntryData
                    {
                        type = GuildSimulator.Core.Models.RewardType.Gold, gold = eventGold, quantity = 1,
                    });
                    detail = $"ゴールド{eventGold:+#;-#;0}（帰還時に反映）";
                    break;
                }

                int requestedLoss = eventGold == int.MinValue ? int.MaxValue : -eventGold;
                if (!option.IsGamble)
                {
                    if (guild.Gold < requestedLoss)
                    {
                        result = $"資金が不足しています（必要 {requestedLoss}G / 所持 {guild.Gold}G）。"
                            + "別の選択肢を選んでください";
                        return false;
                    }
                    if (guild.Gold == requestedLoss)
                    {
                        result = $"支払い後に0Gとなり破産するため {requestedLoss}G は支払えません。"
                            + "少なくとも1Gを残してください";
                        return false;
                    }

                    guild.SpendGold(requestedLoss, $"道中イベント: {pending.Event.title}");
                    detail = $"ゴールド-{requestedLoss}（即時支払い / 残り {guild.Gold}G）";
                    break;
                }

                // 抽選後に「払えないので未解決」とすると、再選択で結果を引き直せてしまう。
                // 強奪などのランダム損失は1Gだけ残して取れる分を取り、結果を必ず確定させる。
                int actualLoss = Math.Min(requestedLoss, guild.Gold > 1 ? guild.Gold - 1 : 0);
                if (actualLoss > 0)
                    guild.SpendGold(actualLoss, $"道中イベント: {pending.Event.title}");
                detail = actualLoss == requestedLoss
                    ? $"ゴールド-{actualLoss}（即時損失 / 残り {guild.Gold}G）"
                    : $"要求 {requestedLoss}G のうちゴールド-{actualLoss}"
                        + $"（所持金から即時損失 / 残り {guild.Gold}G）";
                break;
            }
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
            case GuildSimulator.Core.Models.QuestChoiceEffectType.Purchase:
            {
                string itemName = outcome.Equipment?.displayName
                    ?? outcome.Consumable?.displayName
                    ?? "不明な商品";
                int price = CalculatePurchasePrice(q, outcome.value);
                int negotiation = PurchaseNegotiationPercent(q);
                if (outcome.Equipment == null && outcome.Consumable == null)
                {
                    detail = "商品を確認できず、取引を見送った";
                }
                else if (guild.Gold < price)
                {
                    result = $"資金が不足しています（必要 {price}G / 所持 {guild.Gold}G）。"
                        + "別の商品か「見送る」を選んでください";
                    return false;
                }
                else if (guild.Gold == price)
                {
                    result = $"購入後に0Gとなり破産するため「{itemName}」は購入できません。"
                        + "少なくとも1Gを残してください";
                    return false;
                }
                else
                {
                    guild.SpendGold(price, $"遠征中の購入: {itemName}");
                    if (outcome.Equipment != null)
                        q.pendingLoot.Add(new RewardEntryData
                        {
                            type = GuildSimulator.Core.Models.RewardType.Equipment,
                            Equipment = outcome.Equipment,
                            equipmentId = outcome.Equipment.id,
                            quantity = 1,
                        });
                    else
                        q.pendingLoot.Add(new RewardEntryData
                        {
                            type = GuildSimulator.Core.Models.RewardType.Consumable,
                            Consumable = outcome.Consumable,
                            consumableId = outcome.Consumable!.id,
                            quantity = 1,
                        });

                    string negotiationNote = negotiation > 0
                        ? $" / 交渉で {negotiation}%引き"
                        : negotiation < 0
                            ? $" / 価格補正で {-negotiation}%増し"
                            : "";
                    detail = $"「{itemName}」を {price}G で購入"
                        + $"（帰還時に加算{negotiationNote}）";
                }
                break;
            }
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

        var storyDetails = new List<string>();
        if (option.GrantedClue != null
            && DiscoverClue(q, option.GrantedClue, pending.createdTurn))
        {
            storyDetails.Add($"手掛かり「{option.GrantedClue.title}」を発見");
        }
        if (!string.IsNullOrWhiteSpace(option.storyBranchId))
        {
            selectedBranchIds.Add(option.storyBranchId);
            if (!string.IsNullOrWhiteSpace(option.storyOutcomeText))
                storyDetails.Add(option.storyOutcomeText);
        }
        if (storyDetails.Count > 0)
            detail = detail.Length > 0
                ? $"{detail} / {string.Join(" / ", storyDetails)}"
                : string.Join(" / ", storyDetails);

        if (q.def.FixedChoiceEvents.Any(e => e.ChoiceEvent?.id == pending.Event.id)
            && !q.resolvedFixedChoiceEventIds.Contains(pending.Event.id))
            q.resolvedFixedChoiceEventIds.Add(pending.Event.id);

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
        if (q.ReachedGoal)
            q.logs.Add($"[Turn {pending.createdTurn}] クエスト完了！報酬を受け取れます");
        return true;
    }

    bool DiscoverClue(QuestRun q, StoryClueMasterData clue, int currentTurn)
    {
        if (!discoveredClueIds.Add(clue.id)) return false;

        discoveredClueOrder.Add(clue.id);
        if (!q.discoveredClueIds.Contains(clue.id)) q.discoveredClueIds.Add(clue.id);
        q.AddReportEvent(
            currentTurn,
            q.currentPhase,
            ExpeditionEventKind.Discovery,
            "新たな手掛かり",
            $"{clue.title}: {clue.description}",
            important: true,
            clueId: clue.id);
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

    public static string StatDisplayName(GuildSimulator.Core.Models.StatType t) =>
        AdventurerData.StatDisplayName(t);

    /// <summary>選択イベントのGold倍率。実装済みのF〜Dは 1 / 2 / 4 倍。</summary>
    public static int QuestEventGoldMultiplier(int questRank) => questRank switch
    {
        <= 1 => 1,
        2 => 2,
        3 => 4,
        4 => 6,
        5 => 8,
        6 => 10,
        _ => 12,
    };

    /// <summary>
    /// 道中取引に効く交渉補正。報酬Goldを増やす交渉術は価格を下げ、浪費癖は価格を上げる。
    /// 複数人ぶんはパーティ効果として合算するが、価格が極端にならないよう±25%で止める。
    /// </summary>
    public static int PurchaseNegotiationPercent(QuestRun q) =>
        Math.Clamp(PartySkillEffects.Of(q.formation).goldPercent, -25, 25);

    /// <summary>選択肢に書かれた提示価格へ交渉補正を適用した、実際の支払額。</summary>
    public static int CalculatePurchasePrice(QuestRun q, int listedPrice)
    {
        int percent = 100 - PurchaseNegotiationPercent(q);
        return Math.Max(1, (int)Math.Ceiling(Math.Max(1, listedPrice) * percent / 100f));
    }

    /// <summary>レベルアップで伸びた能力の一覧を「体力+1、敏捷+1」のように表示用にまとめる。</summary>
    public static string FormatGrownStats(IEnumerable<GuildSimulator.Core.Models.StatType> grownStats)
        => AdventurerData.FormatGrownStats(grownStats);

    public void FinalizeQuest(QuestRun q)
    {
        int reportTurn = q.reportEvents.LastOrDefault()?.turn ?? q.startedTurn;
        ApplyClearProgress(q, reportTurn);

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
            if (IsOneShot(q.def))
                clearedOneShotIds.Add(q.def.id);
            else
                questCooldownUntilTurn[q.def.id] = reportTurn
                    + GameRandom.Range(NormalQuestCooldownMinTurns, NormalQuestCooldownMaxTurns + 1);
            if (!string.IsNullOrWhiteSpace(q.def.storyBranchId))
                selectedBranchIds.Add(q.def.storyBranchId);

            foreach (var clueId in q.def.grantedClueIds)
            {
                var clue = q.def.GrantedClues.FirstOrDefault(candidate => candidate.id == clueId);
                if (clue != null) DiscoverClue(q, clue, reportTurn);
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

        // 依頼の結末そのものを記録に落とす。死亡判定を通したあとでなければ
        // 「連れて帰れなかった仲間」が数えられないので、順序を入れ替えてはいけない。
        ExpeditionOutcomeRecorder.Record(q);

        // 遠征での身の置き方を生涯記録へ合流させ、そこで特性の開花を判定する。
        // 撤退でも全滅でも数える——どう戦ったかは、依頼を果たせたかどうかとは別の話なので。
        foreach (var a in q.EnumerateMembers())
            if (q.recorder.Entries.TryGetValue(a.id, out var earned))
                a.records.MergeFrom(earned);
        q.pendingTraitOffers = TraitSystem.BuildOffers(q.EnumerateMembers(), traitCatalog);

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
        questHistory.Add(QuestHistoryEntry.Capture(q));
        TrimQuestHistory();
    }

    void TrimQuestHistory()
    {
        int overflow = questHistory.Count - QuestHistoryLimit;
        if (overflow > 0)
            questHistory.RemoveRange(0, overflow);
    }

    /// <summary>
    /// 帰還処理後に確定した特性選択などを、対応する最新の完了履歴へ反映する。
    /// 同じクエストIDでも別の遠征を上書きしないよう開始Turnまで照合する。
    /// </summary>
    public bool RefreshCompletedQuestHistory(QuestRun quest)
    {
        for (int index = questHistory.Count - 1; index >= 0; index--)
        {
            var history = questHistory[index];
            if (!string.Equals(history.QuestId, quest.def.id, StringComparison.Ordinal)
                || history.StartedTurn != quest.startedTurn)
                continue;

            questHistory[index] = history.WithQuestLogUpdates(quest);
            return true;
        }
        return false;
    }

    void MarkBusy(QuestRun q) { foreach (var a in q.EnumerateMembers()) busyIds.Add(a.id); }
    void UnmarkBusy(QuestRun q) { foreach (var a in q.EnumerateMembers()) busyIds.Remove(a.id); }

    // ---- セーブ/ロード ----
    public IReadOnlyCollection<string> ExportClearedOneShotIds() => clearedOneShotIds;
    public IReadOnlyCollection<string> ExportClearedQuestIds() => clearedQuestIds;
    public IReadOnlyCollection<string> ExportDiscoveredClueIds() => discoveredClueOrder;
    public IReadOnlyCollection<string> ExportSelectedBranchIds() => selectedBranchIds;
    public IReadOnlyDictionary<string, int> ExportQuestCooldowns() => questCooldownUntilTurn;

    public bool HasClearedQuest(string id) => clearedQuestIds.Contains(id);
    public bool HasDiscoveredClue(string id) => discoveredClueIds.Contains(id);
    public bool HasSelectedBranch(string id) => selectedBranchIds.Contains(id);
    public bool HasSelectedBlueOreOutcome =>
        selectedBranchIds.Contains(BlueOreSealedBranchId)
        || selectedBranchIds.Contains(BlueOreStudiedBranchId)
        || selectedBranchIds.Contains(BlueOreTradedBranchId);

    /// <summary>
    /// 固定選択イベント導入前に最終調査まで終えていたセーブだけ、調査記録から結末を補完する。
    /// 新規進行では最終依頼を完了するまで使えず、すでに選んだ結末の上書きもできない。
    /// </summary>
    public bool TryRecordLegacyBlueOreOutcome(string branchId, out string result)
    {
        if (!clearedQuestIds.Contains(BlueOreFinalQuestId))
        {
            result = "最終調査が完了していません";
            return false;
        }
        if (HasSelectedBlueOreOutcome)
        {
            result = "青い鉱石事件の結末はすでに確定しています";
            return false;
        }
        if (branchId is not (BlueOreSealedBranchId or BlueOreStudiedBranchId or BlueOreTradedBranchId))
        {
            result = "選べない結末です";
            return false;
        }

        selectedBranchIds.Add(branchId);
        result = "過去の判断を調査記録へ反映しました";
        return true;
    }

    public bool AreStoryRequirementsMet(QuestMasterData quest) => MeetsStoryRequirements(quest);
    public bool IsQuestKnown(QuestMasterData quest) =>
        clearedQuestIds.Contains(quest.id)
        || activeQuests.Any(run => run.def.id == quest.id)
        || questBoard.Any(entry => entry.quest.id == quest.id)
        || (quest.rank <= guild.GuildRank
            && (quest.rankUpOnClear > 0 ? guild.GuildPointsThisRank : guild.GuildPoints)
                >= quest.requiredGuildPoints
            && MeetsStoryRequirements(quest));
    public int GuildRank => guild.GuildRank;

    /// <summary>セーブデータからの復元専用。掲示板・進行中クエスト・出発中フラグをまとめて置き換える。</summary>
    public void RestoreState(
        List<QuestBoardEntry> board,
        List<QuestRun> active,
        IEnumerable<string> clearedOneShotIdsToRestore,
        IEnumerable<string>? clearedQuestIdsToRestore = null,
        IEnumerable<string>? discoveredClueIdsToRestore = null,
        IEnumerable<string>? selectedBranchIdsToRestore = null,
        IEnumerable<QuestHistoryEntry>? questHistoryToRestore = null,
        IEnumerable<KeyValuePair<string, int>>? questCooldownsToRestore = null)
    {
        questBoard = board;
        activeQuests = active;

        questHistory.Clear();
        questHistory.AddRange(
            (questHistoryToRestore ?? Array.Empty<QuestHistoryEntry>())
                .TakeLast(QuestHistoryLimit));

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
        discoveredClueOrder.Clear();
        foreach (var id in discoveredClueIdsToRestore ?? Array.Empty<string>())
            if (discoveredClueIds.Add(id)) discoveredClueOrder.Add(id);

        selectedBranchIds.Clear();
        foreach (var id in selectedBranchIdsToRestore ?? Array.Empty<string>())
            selectedBranchIds.Add(id);

        questCooldownUntilTurn.Clear();
        foreach (var kv in questCooldownsToRestore ?? Array.Empty<KeyValuePair<string, int>>())
            questCooldownUntilTurn[kv.Key] = kv.Value;

        questBoard.RemoveAll(entry =>
            !MeetsStoryRequirements(entry.quest)
            || (entry.quest.rankUpOnClear > 0
                && guild.GuildPointsThisRank < entry.quest.requiredGuildPoints)
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
