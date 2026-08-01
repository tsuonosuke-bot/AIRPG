using GuildSimulator.Core.GameData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Game.Presentation;

namespace GuildSimulator.Game.Screens;

public static class ActiveQuestScreen
{
    public static async Task ShowAsync(QuestManager questManager, GuildManager guild)
    {
        while (true)
        {
            Ui.BeginScreen();
            Ui.Header("進行中クエスト");
            var actives = questManager.activeQuests;
            if (actives.Count == 0)
            {
                Ui.Dim("進行中のクエストはありません");
                await Ui.PauseAsync();
                return;
            }

            var entries = new List<MenuOption>();
            for (int i = 0; i < actives.Count; i++)
            {
                var q = actives[i];
                string status = q.failed ? "[全員戦闘不能]"
                    : q.retreated ? "[撤退]"
                    : q.HasPendingChoice ? "[選択待ち]"
                    : q.HasGatherDecision ? "[採取の指示待ち]"
                    : q.CanComplete ? "[完了可能]"
                    : $"Phase {q.currentPhase}/{q.PhaseLimit}";
                string hp = q.unitHpMax > 0 ? $"HP {q.unitHpCurrent}/{q.unitHpMax}" : "";
                string morale = $"  士気 {q.morale.Current}/{q.morale.Max}";
                string gather = q.def.IsGatherQuest
                    ? $"  {q.def.gatherItemName} {q.gatheredCount}/{q.def.gatherTargetCount}" : "";
                string chests = q.chests.Count > 0 ? $"  宝箱 {q.chests.Count}個（未開封）" : "";
                Ui.WriteLine($"  {i + 1}. {q.def.questName}  {status}  {hp}{morale}{gather}{chests}");
                var members = q.EnumerateMembers().ToList();
                Ui.Write("      メンバー: ");
                for (int memberIndex = 0; memberIndex < members.Count; memberIndex++)
                {
                    if (memberIndex > 0) Ui.Write(", ");
                    var member = members[memberIndex];
                    Ui.WriteRarityName(member.name, member.master.rarity);
                    string memberState = !member.isAlive ? "死"
                        : member.isIncapacitated ? "戦闘不能"
                        : member.IsInjured ? "負傷" : "生";
                    Ui.Write($"({memberState})");
                }
                Ui.WriteLine();

                entries.Add(new MenuOption(
                    (i + 1).ToString(),
                    $"{i + 1}. {q.def.questName}  {status}",
                    $"{hp}{morale}{gather}",
                    q.failed ? TextStyle.Error
                        : q.CanComplete || q.HasPendingChoice || q.HasGatherDecision ? TextStyle.Info
                        : TextStyle.Normal));
            }

            int? sel = await Ui.SelectIndexAsync("詳細/完了処理", entries);
            if (sel == null) return;
            await HandleQuestAsync(actives[sel.Value - 1], questManager, guild);
        }
    }

    const int LogPageSize = 10;
    sealed record SettlementSnapshot(
        int Gold,
        int GuildPoints,
        int GuildRank,
        int Upkeep,
        int LogCount);

    // ターン進行直後に結果待ちクエストを直接処理する導線からも呼べるよう公開する。
    public static async Task HandleQuestAsync(QuestRun q, QuestManager qm, GuildManager guild)
    {
        if (q.pendingChoice != null)
        {
            await ShowChoiceAsync(q, qm);
            return;
        }
        if (q.HasGatherDecision)
        {
            await ShowGatherDecisionAsync(q, qm);
            return;
        }
        int offset = 0; // 0 = 最新ページ。増えるほど過去へ遡る
        bool showDetailedLogs = false;
        while (true)
        {
            string state = q.failed ? "全員戦闘不能" : q.retreated ? "撤退" : q.CanComplete ? "完了可能" : "進行中";
            Ui.BeginScreen();
            Ui.Header($"クエスト: {q.def.questName}");
            Ui.WriteLine($"  フェーズ: {q.currentPhase}/{q.PhaseLimit}  状態: {state}"
                + (q.gatherExtensions > 0 ? $"（延長 {q.gatherExtensions} 回）" : ""));
            Ui.WriteLine($"  遠征方針  : {QuestManager.PolicyName(q.policy)}");
            Ui.WriteLine($"  ユニットHP: {q.unitHpCurrent}/{q.unitHpMax}");
            Ui.WriteLine($"  士気　　　: {q.morale.Current}/{q.morale.Max}"
                + (q.morale.Rate <= 0.3f && !q.morale.IsBroken ? "  ← 危険域" : ""));
            if (q.def.IsGatherQuest)
                Ui.WriteLine($"  採取状況　: {q.def.gatherItemName} {q.gatheredCount}/{q.def.gatherTargetCount}"
                    + (q.GatherFulfilled ? "  → 必要数を確保済み、帰還可能" : ""));

            ShowExpeditionReport(q);

            int total = q.logs.Count;
            var opts = new List<MenuOption>();
            if (showDetailedLogs)
            {
                int maxOffset = Math.Max(0, (total - 1) / LogPageSize * LogPageSize);
                offset = Math.Clamp(offset, 0, maxOffset);
                int skip = Math.Max(0, total - LogPageSize - offset);
                int take = Math.Min(LogPageSize, total - skip);

                Ui.WriteLine();
                Ui.WriteLine($"  詳細ログ ({(total == 0 ? 0 : skip + 1)}〜{skip + take} / 全{total}件):");
                foreach (var log in q.logs.Skip(skip).Take(take))
                    Ui.WriteQuestLog($"    {log}");

                if (skip > 0) opts.Add(new MenuOption("o", "さらに古いログ"));
                if (offset > 0) opts.Add(new MenuOption("n", "新しいログへ戻る"));
                opts.Add(new MenuOption("h", "詳細ログを閉じる", Style: TextStyle.Dim));
            }
            else if (total > 0)
            {
                opts.Add(new MenuOption("l", $"詳細ログを見る（全{total}件）", Style: TextStyle.Dim));
            }
            opts.Add(new MenuOption("", "続ける", Style: TextStyle.Dim));
            Ui.WriteLine();
            string nav = await Ui.SelectAsync("表示/進行", opts);
            if (nav == "l" && !showDetailedLogs) { showDetailedLogs = true; continue; }
            if (nav == "h" && showDetailedLogs) { showDetailedLogs = false; offset = 0; continue; }
            if (nav == "o" && showDetailedLogs) { offset += LogPageSize; continue; }
            if (nav == "n" && offset > 0) { offset = Math.Max(0, offset - LogPageSize); continue; }
            break;
        }

        if (!q.CanComplete && !q.failed) { return; }

        Ui.WriteLine();
        if (q.failed)
        {
            Ui.Error("パーティは全員戦闘不能になりました（報酬・戦利品・宝箱はすべて失われます）");
            Ui.Warn("帰還処理で各メンバーの死亡または負傷が確定します。医療院は死亡率を下げます");
            if (await Ui.ConfirmAsync("クエストを終了しますか？"))
            {
                var before = CaptureSettlement(guild, q);
                qm.FinalizeQuest(q);
                ShowCompletionSummary(q, guild, before, "壊滅");
                await Ui.PauseAsync();
            }
            return;
        }

        if (q.retreated)
        {
            Ui.Warn(RetreatMessage(q.retreatReason));
            Ui.Dim($"  基本報酬は{QuestRewardService.RetreatRewardRate:P0}、ギルドポイントはなし");
            Ui.Dim("  道中で拾った戦利品と宝箱は持ち帰れます");
            var downed = q.EnumerateMembers().Where(member => member.isIncapacitated).ToList();
            if (downed.Count > 0)
                Ui.Warn($"  戦闘不能者（帰還時に負傷判定）: {string.Join("、", downed.Select(member => member.name))}");
            if (await Ui.ConfirmAsync("引き上げを確定しますか？"))
            {
                var before = CaptureSettlement(guild, q);
                qm.FinalizeQuest(q);
                ShowCompletionSummary(q, guild, before, "撤退");
                await Ui.PauseAsync();
            }
            return;
        }

        Ui.Info("クエストクリア！" + (q.chests.Count > 0 ? $" 持ち帰った宝箱 {q.chests.Count}個を開けます" : ""));

        var settlementBefore = CaptureSettlement(guild, q);
        qm.FinalizeQuest(q);
        ShowCompletionSummary(q, guild, settlementBefore, "成功");
        await Ui.PauseAsync();
    }

    static SettlementSnapshot CaptureSettlement(GuildManager guild, QuestRun q) => new(
        guild.Gold,
        guild.GuildPoints,
        guild.GuildRank,
        guild.EffectiveUpkeepPerTurn,
        q.logs.Count);

    static string RetreatMessage(ExpeditionRetreatReason reason) => reason switch
    {
        ExpeditionRetreatReason.MoraleBroken => "士気が尽き、パーティは撤退しました",
        ExpeditionRetreatReason.SurvivalPolicy => "生還優先の方針に従い、損耗が危険域へ達する前に撤退しました",
        ExpeditionRetreatReason.BattleStalemate => "長期戦を打ち切り、パーティは撤退しました",
        ExpeditionRetreatReason.GatherTargetMissed => "採取目標を達成できず、パーティは撤退しました",
        ExpeditionRetreatReason.SmokeBomb => "機関の煙玉を展開し、パーティは安全に撤退しました",
        _ => "パーティは撤退しました",
    };

    static void ShowCompletionSummary(
        QuestRun q,
        GuildManager guild,
        SettlementSnapshot before,
        string result)
    {
        Ui.Header("クエスト終了サマリー");
        if (result == "成功") Ui.Info($"  結果: {result} － {q.def.questName}");
        else if (result == "撤退") Ui.Warn($"  結果: {result} － {q.def.questName}");
        else Ui.Error($"  結果: {result} － {q.def.questName}");

        int goldDelta = guild.Gold - before.Gold;
        int pointDelta = guild.GuildPoints - before.GuildPoints;
        Ui.WriteLine($"  資金精算: {Signed(goldDelta)}G（{before.Gold}G → {guild.Gold}G）");
        Ui.WriteLine($"  ギルドポイント: {Signed(pointDelta)}（{before.GuildPoints} → {guild.GuildPoints}）");
        if (guild.GuildRank != before.GuildRank)
            Ui.Info($"  ギルドランク: {Rank.Label(before.GuildRank)} → {guild.GuildRankLabel}");

        int startUpkeep = q.guildUpkeepAtStart > 0 ? q.guildUpkeepAtStart : before.Upkeep;
        int currentUpkeep = guild.EffectiveUpkeepPerTurn;
        Ui.WriteLine($"  維持費: 出発時 {startUpkeep}G/T → 現在 {currentUpkeep}G/T"
            + (currentUpkeep == startUpkeep ? "" : $"（{Signed(currentUpkeep - startUpkeep)}G/T）"));

        var fallen = q.EnumerateMembers().Where(member => !member.isAlive).ToList();
        if (fallen.Count == 0)
            Ui.WriteLine("  死亡者: なし");
        else
            Ui.Error($"  死亡者: {string.Join("、", fallen.Select(member => member.name))}");

        var injured = q.EnumerateMembers().Where(member => member.isAlive && member.IsInjured).ToList();
        Ui.WriteLine("  帰還時の負傷:");
        if (injured.Count == 0)
            Ui.Dim("    なし");
        else
            foreach (var member in injured)
                foreach (var injury in member.injuries)
                    Ui.Warn($"    {member.name}: {injury.DisplayName}（休養あと{injury.remainingRestTurns}T、{injury.EffectDescription}）");

        Ui.WriteLine("  成長:");
        bool hasGrowth = false;
        foreach (var member in q.EnumerateMembers())
        {
            if (!q.startingLevels.TryGetValue(member.id, out int startLevel))
            {
                Ui.Dim($"    {member.name}: Lv{member.level}");
                continue;
            }
            if (member.level <= startLevel) continue;
            hasGrowth = true;
            Ui.Info($"    {member.name}: Lv{startLevel} → Lv{member.level}");
        }
        if (!hasGrowth && q.startingLevels.Count > 0)
            Ui.Dim("    レベルアップなし");

        if (q.discoveredClueIds.Count > 0)
        {
            Ui.WriteLine("  新たな手掛かり:");
            foreach (var clueEvent in q.reportEvents
                .Where(e => e.kind == GuildSimulator.Core.Models.ExpeditionEventKind.Discovery))
                Ui.Info($"    ・{clueEvent.detail}");
        }

        var settlementLogs = q.logs
            .Skip(before.LogCount)
            .Where(log => log.StartsWith("[完了]"))
            .ToList();
        Ui.WriteLine("  獲得内訳:");
        if (settlementLogs.Count == 0)
            Ui.Dim("    報酬なし");
        else
            foreach (var log in settlementLogs)
                Ui.Dim($"    {log}");
    }

    static string Signed(int value) => value > 0 ? $"+{value}" : value.ToString();

    static void ShowExpeditionReport(QuestRun q)
    {
        Ui.WriteLine();
        Ui.WriteLine("  遠征報告:");
        if (q.reportEvents.Count == 0)
        {
            Ui.Dim("    まだ報告できる出来事はない");
            return;
        }

        foreach (var e in q.reportEvents.TakeLast(8))
        {
            string marker = e.important ? "◆" : "・";
            string phase = e.phase > 0 ? $" Phase {e.phase}" : "";
            Ui.WriteLine($"    {marker} Turn {e.turn}{phase}: {e.title}");
            if (!string.IsNullOrWhiteSpace(e.detail))
                Ui.Dim($"       {e.detail}");
        }
    }

    /// <summary>
    /// 採取が目標に届かないまま予定フェーズを使い切ったときの二択。
    /// 延長には回数制限がないので、引き際を決めるのはプレイヤーの側になる。
    /// 判断材料（残り士気・HP・現地の消耗）を並べたうえで聞く。
    /// </summary>
    static async Task ShowGatherDecisionAsync(QuestRun q, QuestManager qm)
    {
        int added = Math.Max(1, q.def.phasesPerTurn);
        int shortage = Math.Max(0, q.def.gatherTargetCount - q.gatheredCount);

        Ui.BeginScreen();
        Ui.Header($"採取の指示待ち: {q.def.questName}");
        Ui.Warn($"  予定していた {q.PhaseLimit} フェーズを使い切りましたが、"
            + $"{q.def.gatherItemName} が {shortage} 個足りません");
        Ui.WriteLine($"  採取状況　: {q.def.gatherItemName} {q.gatheredCount}/{q.def.gatherTargetCount}");
        Ui.WriteLine($"  ユニットHP: {q.unitHpCurrent}/{q.unitHpMax}");
        Ui.WriteLine($"  士気　　　: {q.morale.Current}/{q.morale.Max}"
            + (q.morale.Rate <= 0.3f && !q.morale.IsBroken ? "  ← 危険域" : ""));
        if (q.gatherExtensions > 0)
            Ui.Dim($"  すでに {q.gatherExtensions} 回延長しています");
        if (q.chests.Count > 0)
            Ui.Dim($"  未開封の宝箱 {q.chests.Count}個を抱えています（全滅すると失います）");
        Ui.WriteLine();

        var options = new List<MenuOption>
        {
            new("1", $"捜索を続けさせる（+{added}フェーズ）",
                "帰還が1ターン遅れ、そのぶん維持費がかかります。踏み足すフェーズでは戦闘も罠も起こります"),
            new("2", "引き上げさせる",
                $"撤退扱い。基本報酬は{QuestRewardService.RetreatRewardRate:P0}、ギルドポイントはなし。"
                    + "拾った戦利品と宝箱は持ち帰れます"),
        };
        foreach (var option in options)
            Ui.WriteLine($"  {option.Key}. {option.Label}"
                + (string.IsNullOrEmpty(option.Detail) ? "" : $"（{option.Detail}）"));

        int? selected = await Ui.SelectIndexAsync("指示", options, "あとで決める");
        if (selected == null) return;

        if (qm.ResolveGatherDecision(q, keepSearching: selected.Value == 1, out var result))
        {
            Ui.Info(result);
            await Ui.PauseAsync();
        }
        else
            Ui.Error(result);
    }

    static async Task ShowChoiceAsync(QuestRun q, QuestManager qm)
    {
        var pending = q.pendingChoice;
        if (pending == null) return;
        Ui.BeginScreen();
        Ui.Header($"選択イベント: {pending.Event.title}");
        Ui.WriteLine($"  {pending.Event.description}");
        Ui.WriteLine();

        var options = pending.Event.options
            .Select((option, i) => new MenuOption(
                (i + 1).ToString(),
                option.text,
                // 結果が複数あるなら、賭けだと分かるようにしておく。何が起きるかは伏せる。
                option.IsGamble ? "何が起きるかは分からない" : null))
            .ToList();
        foreach (var option in options)
            Ui.WriteLine($"  {option.Key}. {option.Label}"
                + (string.IsNullOrEmpty(option.Detail) ? "" : $"（{option.Detail}）"));

        int? selected = await Ui.SelectIndexAsync("選択", options, "あとで決める");
        if (selected == null) return;

        var chosen = pending.Event.options[selected.Value - 1];
        AdventurerData? target = null;
        if (chosen.targetsOneMember)
        {
            target = await SelectMemberAsync(q);
            if (target == null) return;   // 対象選びをやめたら選択自体を保留に戻す
        }

        if (qm.ResolveChoice(q, selected.Value - 1, target, out var result))
        {
            Ui.Info(result);
            await Ui.PauseAsync();
        }
        else
            Ui.Error(result);
    }

    /// <summary>
    /// 効果を受ける隊員を1人選ばせる。結果の抽選は選んだ後に行われるので、
    /// プレイヤーは「誰に賭けるか」だけを決めることになる。
    /// </summary>
    static async Task<AdventurerData?> SelectMemberAsync(QuestRun q)
    {
        var members = q.EnumerateMembers().Where(a => a.isAlive).ToList();
        if (members.Count == 0)
        {
            Ui.Error("対象にできる隊員がいません");
            await Ui.PauseAsync();
            return null;
        }

        Ui.WriteLine();
        var entries = members
            .Select((a, i) => new MenuOption(
                (i + 1).ToString(),
                $"{a.name} Lv{a.level} ランク{a.RankLabel}",
                $"{a.ClassAndRace}  HP {a.CombatHp}/{a.CombatHpMax}  "
                    + $"VIT{a.vitality} MEN{a.mental} STR{a.strength} AGI{a.agility} INT{a.intelligence}",
                Ui.RarityStyle(a.master.rarity)))
            .ToList();

        int? pick = await Ui.SelectIndexAsync("誰に任せる？", entries, "やめる");
        return pick == null ? null : members[pick.Value - 1];
    }
}
