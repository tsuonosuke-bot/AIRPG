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
                    : q.CanComplete ? "[完了可能]"
                    : $"Phase {q.currentPhase}/{q.def.totalPhases}";
                string hp = q.unitHpMax > 0 ? $"HP {q.unitHpCurrent}/{q.unitHpMax}" : "";
                string morale = $"  士気 {q.morale.Current}/{q.morale.Max}";
                string gather = q.def.IsGatherQuest
                    ? $"  {q.def.gatherItemName} {q.gatheredCount}/{q.def.gatherTargetCount}" : "";
                Console.WriteLine($"  {i + 1}. {q.def.questName}  {status}  {hp}{morale}{gather}");
                var members = q.EnumerateMembers().ToList();
                Console.WriteLine($"      メンバー: {string.Join(", ", members.Select(a => $"{a.name}({(a.isAlive ? "生" : "死")})"))}");
            }
            Console.WriteLine("  0. 戻る");
            Console.Write("詳細/完了処理: ");
            if (!int.TryParse(Console.ReadLine(), out int sel) || sel == 0) return;
            if (sel < 1 || sel > actives.Count) { ConsoleHelper.Error("無効"); continue; }
            HandleQuest(actives[sel - 1], questManager, guild);
        }
    }

    const int LogPageSize = 10;

    // ターン進行直後に結果待ちクエストを直接処理する導線からも呼べるよう公開する。
    public static void HandleQuest(QuestRun q, QuestManager qm, GuildManager guild)
    {
        int offset = 0; // 0 = 最新ページ。増えるほど過去へ遡る
        while (true)
        {
            string state = q.failed ? "全滅" : q.retreated ? "撤退" : q.CanComplete ? "完了可能" : "進行中";
            ConsoleHelper.Header($"クエスト: {q.def.questName}");
            Console.WriteLine($"  フェーズ: {q.currentPhase}/{q.def.totalPhases}  状態: {state}");
            Console.WriteLine($"  ユニットHP: {q.unitHpCurrent}/{q.unitHpMax}");
            Console.WriteLine($"  士気　　　: {q.morale.Current}/{q.morale.Max}"
                + (q.morale.Rate <= 0.3f && !q.morale.IsBroken ? "  ← 危険域" : ""));
            if (q.def.IsGatherQuest)
                Console.WriteLine($"  採取状況　: {q.def.gatherItemName} {q.gatheredCount}/{q.def.gatherTargetCount}"
                    + (q.GatherFulfilled ? "  → 必要数を確保済み、帰還可能" : ""));

            int total = q.logs.Count;
            int maxOffset = Math.Max(0, (total - 1) / LogPageSize * LogPageSize);
            offset = Math.Clamp(offset, 0, maxOffset);
            int skip = Math.Max(0, total - LogPageSize - offset);
            int take = Math.Min(LogPageSize, total - skip);

            Console.WriteLine($"\n  ログ ({(total == 0 ? 0 : skip + 1)}〜{skip + take} / 全{total}件):");
            foreach (var log in q.logs.Skip(skip).Take(take))
                ConsoleHelper.Dim($"    {log}");

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
                qm.FinalizeQuest(q, null);
            return;
        }

        if (q.retreated)
        {
            ConsoleHelper.Warn("士気が尽き、パーティは撤退しました");
            ConsoleHelper.Dim($"  基本報酬は{QuestRewardService.RetreatRewardRate:P0}、ギルドポイントと選択報酬はなし");
            ConsoleHelper.Dim("  道中で拾った戦利品は持ち帰れます。死者は出ていません");
            if (ConsoleHelper.Confirm("引き上げを確定しますか？"))
                qm.FinalizeQuest(q, null);
            return;
        }

        ConsoleHelper.Info("クエストクリア！報酬を選んでください");
        var options = qm.GetPendingRewards(q);
        Console.WriteLine("\n  追加報酬の選択:");
        for (int i = 0; i < options.Count; i++)
        {
            Console.WriteLine($"    {i + 1}. {options[i].Title}");
            string detail = options[i].Detail;
            if (detail.Length > 0) ConsoleHelper.Dim($"         {detail}");
        }
        Console.WriteLine($"    0. スキップ");

        RewardOption? chosen = null;
        Console.Write("選択: ");
        if (int.TryParse(Console.ReadLine(), out int pick) && pick >= 1 && pick <= options.Count)
            chosen = options[pick - 1];

        qm.FinalizeQuest(q, chosen);
        ConsoleHelper.Info("クエスト完了！");
        ConsoleHelper.PressAnyKey();
    }
}
