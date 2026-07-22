using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Core.Systems.Guild;

namespace GuildSimulator.Cli.Screens;

public static class QuestBoardScreen
{
    public static void Show(QuestManager questManager, GuildManager guild, int currentTurn)
    {
        while (true)
        {
            ConsoleHelper.Header("クエストボード");
            var board = questManager.questBoard;
            Console.WriteLine($"  掲示 {board.Count}/{questManager.BoardCapacity} 枚   受注可能: ギルドランク{guild.GuildRank}以下");
            Console.WriteLine();
            if (board.Count == 0) { ConsoleHelper.Warn("掲示中のクエストはありません"); ConsoleHelper.PressAnyKey(); return; }
            for (int i = 0; i < board.Count; i++)
            {
                var e = board[i];
                var q = e.quest;
                string emg = q.isEmergencyQuest ? " [緊急]" : "";
                int estTurns = (int)Math.Ceiling((double)q.totalPhases / q.phasesPerTurn);
                var diff = DungeonDifficulty.Evaluate(q);

                Console.WriteLine($"  {i + 1}. 【Rank{q.rank}】{q.questName}  所要:{estTurns}T{emg}");
                Console.WriteLine($"      難易度 {diff.label}（スコア{diff.score:0}）  報酬 Gold:{q.rewardGold} EXP:{q.rewardExp} GP:{q.rewardGuildPoints}");
                if (q.IsGatherQuest)
                    ConsoleHelper.Dim($"      採取: {q.gatherItemName} x{q.gatherTargetCount}（1個につき +{q.gatherGoldPerItem}G / 必要数を集めた時点で帰還）");
                string bossInfo = diff.hasBoss ? $"  ボス:Lv{diff.bossLevel}" : "";
                ConsoleHelper.Dim($"      場所: {q.Dungeon?.dungeonName ?? "？"}  敵{diff.EnemyLevelRange}"
                    + $"  戦闘{diff.combatChance * 100:0}% 罠{diff.trapChance * 100:0}%{bossInfo}");
                ConsoleHelper.Dim($"      掲示期限: あと{e.RemainingTurns(currentTurn, questManager.BoardExpireTurns)}ターン");
            }
            Console.WriteLine("  0. 戻る");
            Console.Write("受注するクエスト番号: ");
            if (!int.TryParse(Console.ReadLine(), out int sel) || sel == 0) return;
            if (sel < 1 || sel > board.Count) { ConsoleHelper.Error("無効"); continue; }
            SelectAndStart(board[sel - 1].quest, questManager, guild, currentTurn);
        }
    }

    static void SelectAndStart(QuestMasterData def, QuestManager qm, GuildManager guild, int currentTurn)
    {
        ConsoleHelper.Header($"編成: {def.questName}");
        Console.WriteLine("前衛スロット (0-2) と後衛スロット (3-5) に冒険者を配置してください");

        var formation = new AdventurerData?[6];
        var advs = guild.adventurers;

        for (int slot = 0; slot < 6; slot++)
        {
            string pos = slot < 3 ? $"前衛{slot + 1}" : $"後衛{slot - 2}";
            Console.WriteLine($"\n  【{pos}】");
            var available = advs.Where((a, i) =>
                a.isAlive &&
                !qm.IsAdventurerBusy(a.id) &&
                !formation.Contains(a)).ToList();
            if (available.Count == 0) { ConsoleHelper.Dim("配置可能な冒険者がいません"); continue; }

            Console.WriteLine("  0. スキップ");
            for (int i = 0; i < available.Count; i++)
            {
                var a = available[i];
                Console.WriteLine($"  {i + 1}. {a.name} Lv{a.level} {a.ClassAndRace}");
            }
            Console.Write($"  選択 [0-{available.Count}]: ");
            if (int.TryParse(Console.ReadLine(), out int pick) && pick >= 1 && pick <= available.Count)
                formation[slot] = available[pick - 1];
        }

        int count = formation.Count(x => x != null);
        if (count == 0) { ConsoleHelper.Warn("編成が空のためキャンセル"); return; }

        ConsoleHelper.Header("編成確認");
        for (int i = 0; i < 6; i++)
        {
            string pos = i < 3 ? $"前衛{i + 1}" : $"後衛{i - 2}";
            Console.WriteLine($"  {pos}: {formation[i]?.name ?? "空"}");
        }
        if (!ConsoleHelper.Confirm("このメンバーで受注しますか？")) return;

        if (qm.TryStartQuest(def, formation, currentTurn, out var error))
            ConsoleHelper.Info($"クエスト「{def.questName}」を受注しました！ （Turn {currentTurn} 開始）");
        else
            ConsoleHelper.Error($"受注失敗: {error}");

        ConsoleHelper.PressAnyKey();
    }
}
