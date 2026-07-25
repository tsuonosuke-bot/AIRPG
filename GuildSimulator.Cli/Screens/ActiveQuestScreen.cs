using GuildSimulator.Core.GameData;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Core.Systems.Guild;

namespace GuildSimulator.Cli.Screens;

public static class ActiveQuestScreen
{
    public static void Show(QuestManager questManager, GuildManager guild)
    {
        while (true)
        {
            ConsoleHelper.Header("進行中クエスト");
            var actives = questManager.activeQuests;
            if (actives.Count == 0) { ConsoleHelper.Dim("進行中のクエストはありません"); ConsoleHelper.PressAnyKey(); return; }

            for (int i = 0; i < actives.Count; i++)
            {
                var q = actives[i];
                string status = q.failed ? "[全滅]"
                    : q.retreated ? "[撤退]"
                    : q.HasPendingChoice ? "[選択待ち]"
                    : q.CanComplete ? "[完了可能]"
                    : $"Phase {q.currentPhase}/{q.def.totalPhases}";
                string hp = q.unitHpMax > 0 ? $"HP {q.unitHpCurrent}/{q.unitHpMax}" : "";
                string morale = $"  士気 {q.morale.Current}/{q.morale.Max}";
                string gather = q.def.IsGatherQuest
                    ? $"  {q.def.gatherItemName} {q.gatheredCount}/{q.def.gatherTargetCount}" : "";
                Console.WriteLine($"  {i + 1}. {q.def.questName}  {status}  {hp}{morale}{gather}");
                var members = q.EnumerateMembers().ToList();
                Console.Write("      メンバー: ");
                for (int memberIndex = 0; memberIndex < members.Count; memberIndex++)
                {
                    if (memberIndex > 0) Console.Write(", ");
                    var member = members[memberIndex];
                    ConsoleHelper.WriteRarityName(member.name, member.master.rarity);
                    Console.Write($"({(member.isAlive ? "生" : "死")})");
                }
                Console.WriteLine();
            }
            Console.WriteLine("  0. 戻る");
            Console.Write("詳細/完了処理: ");
            if (!int.TryParse(Console.ReadLine(), out int sel) || sel == 0) return;
            if (sel < 1 || sel > actives.Count) { ConsoleHelper.Error("無効"); continue; }
            HandleQuest(actives[sel - 1], questManager, guild);
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
    public static void HandleQuest(QuestRun q, QuestManager qm, GuildManager guild)
    {
        if (q.pendingChoice != null)
        {
            ShowChoice(q, qm);
            return;
        }
        int offset = 0; // 0 = 最新ページ。増えるほど過去へ遡る
        while (true)
        {
            string state = q.failed ? "全滅" : q.retreated ? "撤退" : q.CanComplete ? "完了可能" : "進行中";
            ConsoleHelper.Header($"クエスト: {q.def.questName}");
            Console.WriteLine($"  フェーズ: {q.currentPhase}/{q.def.totalPhases}  状態: {state}");
            Console.WriteLine($"  遠征方針  : {QuestManager.PolicyName(q.policy)}");
            Console.WriteLine($"  ユニットHP: {q.unitHpCurrent}/{q.unitHpMax}");
            Console.WriteLine($"  士気　　　: {q.morale.Current}/{q.morale.Max}"
                + (q.morale.Rate <= 0.3f && !q.morale.IsBroken ? "  ← 危険域" : ""));
            if (q.def.IsGatherQuest)
                Console.WriteLine($"  採取状況　: {q.def.gatherItemName} {q.gatheredCount}/{q.def.gatherTargetCount}"
                    + (q.GatherFulfilled ? "  → 必要数を確保済み、帰還可能" : ""));

            ShowExpeditionReport(q);

            int total = q.logs.Count;
            int maxOffset = Math.Max(0, (total - 1) / LogPageSize * LogPageSize);
            offset = Math.Clamp(offset, 0, maxOffset);
            int skip = Math.Max(0, total - LogPageSize - offset);
            int take = Math.Min(LogPageSize, total - skip);

            Console.WriteLine($"\n  詳細ログ ({(total == 0 ? 0 : skip + 1)}〜{skip + take} / 全{total}件):");
            foreach (var log in q.logs.Skip(skip).Take(take))
                ConsoleHelper.WriteQuestLog($"    {log}");

            Console.WriteLine();
            var opts = new List<string>();
            if (skip > 0) opts.Add("o: さらに古いログ");
            if (offset > 0) opts.Add("n: 新しいログへ戻る");
            opts.Add("Enter: 続ける");
            Console.WriteLine("  " + string.Join("   ", opts));
            Console.Write("  選択: ");
            var nav = Console.ReadLine()?.Trim();
            if (nav == "o" && skip > 0) { offset += LogPageSize; continue; }
            if (nav == "n" && offset > 0) { offset = Math.Max(0, offset - LogPageSize); continue; }
            break;
        }

        if (!q.CanComplete && !q.failed) { return; }

        Console.WriteLine();
        if (q.failed)
        {
            ConsoleHelper.Error("パーティは全滅しました（報酬・戦利品はすべて失われます）");
            if (ConsoleHelper.Confirm("クエストを終了しますか？"))
            {
                var before = CaptureSettlement(guild, q);
                qm.FinalizeQuest(q, null);
                ShowCompletionSummary(q, guild, before, "全滅");
                ConsoleHelper.PressAnyKey();
            }
            return;
        }

        if (q.retreated)
        {
            ConsoleHelper.Warn("士気が尽き、パーティは撤退しました");
            ConsoleHelper.Dim($"  基本報酬は{QuestRewardService.RetreatRewardRate:P0}、ギルドポイントと選択報酬はなし");
            ConsoleHelper.Dim("  道中で拾った戦利品は持ち帰れます");
            var fallen = q.EnumerateMembers().Where(member => !member.isAlive).ToList();
            if (fallen.Count == 0)
                ConsoleHelper.Dim("  死亡者はいません");
            else
                ConsoleHelper.Error($"  死亡者: {string.Join("、", fallen.Select(member => member.name))}");
            if (ConsoleHelper.Confirm("引き上げを確定しますか？"))
            {
                var before = CaptureSettlement(guild, q);
                qm.FinalizeQuest(q, null);
                ShowCompletionSummary(q, guild, before, "撤退");
                ConsoleHelper.PressAnyKey();
            }
            return;
        }

        ConsoleHelper.Info("クエストクリア！報酬を選んでください");
        var options = qm.GetPendingRewards(q);
        Console.WriteLine("\n  追加報酬の選択:");
        for (int i = 0; i < options.Count; i++)
        {
            Console.Write($"    {i + 1}. ");
            if (options[i].equipment != null)
            {
                Console.Write("装備：");
                ConsoleHelper.WriteRarityName(options[i].equipment!.displayName, options[i].equipment!.rarity);
                if (options[i].quantity > 1) Console.Write($" x{options[i].quantity}");
                Console.WriteLine();
            }
            else if (options[i].consumable != null)
            {
                Console.Write("消費アイテム：");
                ConsoleHelper.WriteRarityName(options[i].consumable!.displayName, options[i].consumable!.rarity);
                Console.WriteLine();
            }
            else Console.WriteLine(options[i].Title);
            string detail = options[i].Detail;
            if (detail.Length > 0) ConsoleHelper.Dim($"         {detail}");
        }
        Console.WriteLine($"    0. スキップ");

        RewardOption? chosen = null;
        Console.Write("選択: ");
        if (int.TryParse(Console.ReadLine(), out int pick) && pick >= 1 && pick <= options.Count)
            chosen = options[pick - 1];

        var settlementBefore = CaptureSettlement(guild, q);
        qm.FinalizeQuest(q, chosen);
        ShowCompletionSummary(q, guild, settlementBefore, "成功");
        ConsoleHelper.PressAnyKey();
    }

    static SettlementSnapshot CaptureSettlement(GuildManager guild, QuestRun q) => new(
        guild.Gold,
        guild.GuildPoints,
        guild.GuildRank,
        guild.EffectiveUpkeepPerTurn,
        q.logs.Count);

    static void ShowCompletionSummary(
        QuestRun q,
        GuildManager guild,
        SettlementSnapshot before,
        string result)
    {
        ConsoleHelper.Header("クエスト終了サマリー");
        if (result == "成功") ConsoleHelper.Info($"  結果: {result} － {q.def.questName}");
        else if (result == "撤退") ConsoleHelper.Warn($"  結果: {result} － {q.def.questName}");
        else ConsoleHelper.Error($"  結果: {result} － {q.def.questName}");

        int goldDelta = guild.Gold - before.Gold;
        int pointDelta = guild.GuildPoints - before.GuildPoints;
        Console.WriteLine($"  資金精算: {Signed(goldDelta)}G（{before.Gold}G → {guild.Gold}G）");
        Console.WriteLine($"  ギルドポイント: {Signed(pointDelta)}（{before.GuildPoints} → {guild.GuildPoints}）");
        if (guild.GuildRank != before.GuildRank)
            ConsoleHelper.Info($"  ギルドランク: {before.GuildRank} → {guild.GuildRank}");

        int startUpkeep = q.guildUpkeepAtStart > 0 ? q.guildUpkeepAtStart : before.Upkeep;
        int currentUpkeep = guild.EffectiveUpkeepPerTurn;
        Console.WriteLine($"  維持費: 出発時 {startUpkeep}G/T → 現在 {currentUpkeep}G/T"
            + (currentUpkeep == startUpkeep ? "" : $"（{Signed(currentUpkeep - startUpkeep)}G/T）"));

        var fallen = q.EnumerateMembers().Where(member => !member.isAlive).ToList();
        if (fallen.Count == 0)
            Console.WriteLine("  死亡者: なし");
        else
            ConsoleHelper.Error($"  死亡者: {string.Join("、", fallen.Select(member => member.name))}");

        Console.WriteLine("  成長:");
        bool hasGrowth = false;
        foreach (var member in q.EnumerateMembers())
        {
            if (!q.startingLevels.TryGetValue(member.id, out int startLevel))
            {
                ConsoleHelper.Dim($"    {member.name}: Lv{member.level}");
                continue;
            }
            if (member.level <= startLevel) continue;
            hasGrowth = true;
            ConsoleHelper.Info($"    {member.name}: Lv{startLevel} → Lv{member.level}");
        }
        if (!hasGrowth && q.startingLevels.Count > 0)
            ConsoleHelper.Dim("    レベルアップなし");

        if (q.discoveredClueIds.Count > 0)
        {
            Console.WriteLine("  新たな手掛かり:");
            foreach (var clueEvent in q.reportEvents
                .Where(e => e.kind == GuildSimulator.Core.Models.ExpeditionEventKind.Discovery))
                ConsoleHelper.Info($"    ・{clueEvent.detail}");
        }

        var settlementLogs = q.logs
            .Skip(before.LogCount)
            .Where(log => log.StartsWith("[完了]") || log.StartsWith("[選択報酬]"))
            .ToList();
        Console.WriteLine("  獲得内訳:");
        if (settlementLogs.Count == 0)
            ConsoleHelper.Dim("    報酬なし");
        else
            foreach (var log in settlementLogs)
                ConsoleHelper.Dim($"    {log}");
    }

    static string Signed(int value) => value > 0 ? $"+{value}" : value.ToString();

    static void ShowExpeditionReport(QuestRun q)
    {
        Console.WriteLine();
        Console.WriteLine("  遠征報告:");
        if (q.reportEvents.Count == 0)
        {
            ConsoleHelper.Dim("    まだ報告できる出来事はない");
            return;
        }

        foreach (var e in q.reportEvents.TakeLast(8))
        {
            string marker = e.important ? "◆" : "・";
            string phase = e.phase > 0 ? $" Phase {e.phase}" : "";
            Console.WriteLine($"    {marker} Turn {e.turn}{phase}: {e.title}");
            if (!string.IsNullOrWhiteSpace(e.detail))
                ConsoleHelper.Dim($"       {e.detail}");
        }
    }

    static void ShowChoice(QuestRun q, QuestManager qm)
    {
        var pending = q.pendingChoice;
        if (pending == null) return;
        ConsoleHelper.Header($"選択イベント: {pending.Event.title}");
        Console.WriteLine($"  {pending.Event.description}");
        Console.WriteLine();
        for (int i = 0; i < pending.Event.options.Count; i++)
            Console.WriteLine($"  {i + 1}. {pending.Event.options[i].text}");
        Console.WriteLine("  0. あとで決める");
        Console.Write("選択: ");
        if (!int.TryParse(Console.ReadLine(), out int selected)
            || selected <= 0 || selected > pending.Event.options.Count)
            return;
        if (qm.ResolveChoice(q, selected - 1, out var result))
        {
            ConsoleHelper.Info(result);
            ConsoleHelper.PressAnyKey();
        }
        else
            ConsoleHelper.Error(result);
    }
}
