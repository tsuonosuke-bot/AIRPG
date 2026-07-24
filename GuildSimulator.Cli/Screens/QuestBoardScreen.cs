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
            Console.WriteLine($"  受注可能: ギルドランク{guild.GuildRank}以下");
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
                Console.WriteLine($"      難易度 {diff.label}（スコア{diff.score:0}）  報酬 資金:{q.rewardGold}G 経験値:{q.rewardExp} ギルドポイント:{q.rewardGuildPoints}");
                if (q.IsGatherQuest)
                    ConsoleHelper.Dim($"      採取: {q.gatherItemName} x{q.gatherTargetCount}（目標超過1個につき +{q.gatherGoldPerItem}G / 必要数を集めた時点で帰還）");
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
        Console.WriteLine("冒険者を選び、次に配置先を指定してください");

        var formation = new AdventurerData?[6];
        var advs = guild.adventurers;

        while (formation.Any(x => x == null))
        {
            var available = advs.Where((a, i) =>
                a.isAlive &&
                !qm.IsAdventurerBusy(a.id) &&
                !formation.Contains(a)).ToList();
            if (available.Count == 0)
            {
                ConsoleHelper.Dim("  配置可能な冒険者をすべて編成しました");
                break;
            }

            Console.WriteLine();
            ShowFormation(formation);
            Console.WriteLine();
            Console.WriteLine("  0. 編成を確定");
            for (int i = 0; i < available.Count; i++)
            {
                var a = available[i];
                Console.Write($"  {i + 1}. ");
                ConsoleHelper.WriteRarityName(a.name, a.master.rarity);
                Console.WriteLine($" Lv{a.level} {a.ClassAndRace}");
            }
            Console.Write($"追加する冒険者 [0-{available.Count}]: ");
            if (!int.TryParse(Console.ReadLine(), out int pick) || pick == 0) break;
            if (pick < 1 || pick > available.Count)
            {
                ConsoleHelper.Error("無効な番号です");
                continue;
            }

            var openSlots = Enumerable.Range(0, formation.Length)
                .Where(slot => formation[slot] == null)
                .ToList();
            Console.WriteLine($"\n  {available[pick - 1].name} の配置先:");
            for (int i = 0; i < openSlots.Count; i++)
            {
                Console.WriteLine($"  {i + 1}. {PositionName(openSlots[i])}");
            }
            Console.Write($"配置先 [1-{openSlots.Count}]: ");
            if (!int.TryParse(Console.ReadLine(), out int slotPick)
                || slotPick < 1 || slotPick > openSlots.Count)
            {
                ConsoleHelper.Warn("配置をキャンセルしました");
                continue;
            }
            formation[openSlots[slotPick - 1]] = available[pick - 1];
        }

        int count = formation.Count(x => x != null);
        if (count == 0) { ConsoleHelper.Warn("編成が空のためキャンセル"); return; }

        ConsoleHelper.Header("編成確認");
        ShowFormation(formation);
        var carriedConsumables = SelectConsumables(guild);
        if (carriedConsumables.Count > 0)
            Console.WriteLine($"  持ち込み（出発時消費）: {string.Join(", ", carriedConsumables.Select(x => x.displayName))}");
        if (!ConsoleHelper.Confirm("このメンバーで受注しますか？")) return;

        if (qm.TryStartQuest(def, formation, currentTurn, out var error, carriedConsumables))
            ConsoleHelper.Info($"クエスト「{def.questName}」を受注しました！ （Turn {currentTurn} 開始）");
        else
            ConsoleHelper.Error($"受注失敗: {error}");

        ConsoleHelper.PressAnyKey();
    }

    static List<ConsumableMasterData> SelectConsumables(GuildManager guild)
    {
        var selected = new List<ConsumableMasterData>();
        for (int slot = 1; slot <= 2; slot++)
        {
            var stock = guild.GetConsumablesView()
                .Where(s => s.count > selected.Count(x => x == s.item))
                .ToList();
            if (stock.Count == 0) break;
            Console.WriteLine();
            Console.WriteLine($"  持ち込みスロット{slot}（出発時に消費）");
            Console.WriteLine("  0. 選択を終了");
            for (int i = 0; i < stock.Count; i++)
                Console.WriteLine($"  {i + 1}. {stock[i].item.displayName} x{stock[i].count} - {stock[i].item.description}");
            Console.Write("選択: ");
            if (!int.TryParse(Console.ReadLine(), out int pick) || pick <= 0 || pick > stock.Count) break;
            selected.Add(stock[pick - 1].item);
        }
        return selected;
    }

    static void ShowFormation(AdventurerData?[] formation)
    {
        Console.WriteLine("  現在の編成:");
        for (int i = 0; i < formation.Length; i++)
        {
            Console.Write($"    {PositionName(i),-4}: ");
            if (formation[i] != null)
                ConsoleHelper.WriteRarityName(formation[i]!.name, formation[i]!.master.rarity);
            else
                Console.Write("空");
            Console.WriteLine();
        }
    }

    static string PositionName(int slot) => slot < 3 ? $"前衛{slot + 1}" : $"後衛{slot - 2}";
}
