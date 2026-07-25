using GuildSimulator.Cli.Data;
using GuildSimulator.Core.Systems.Quest;

namespace GuildSimulator.Cli.Screens;

public static class StoryJournalScreen
{
    public static void Show(GameMasterData db, QuestManager questManager)
    {
        ConsoleHelper.Header("調査記録");

        var clues = db.clues.Values
            .Where(clue => questManager.HasDiscoveredClue(clue.id))
            .OrderBy(clue => clue.id)
            .ToList();

        Console.WriteLine("  【発見した手掛かり】");
        if (clues.Count == 0)
        {
            ConsoleHelper.Dim("    まだ世界の謎につながる手掛かりはない");
        }
        else
        {
            foreach (var clue in clues)
            {
                ConsoleHelper.Info($"    ◆ {clue.title}");
                Console.WriteLine($"       {clue.description}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("  【完了した物語】");
        var completedStories = db.allQuests
            .Where(q => q.isStoryQuest && questManager.HasClearedQuest(q.id))
            .ToList();
        if (completedStories.Count == 0)
        {
            ConsoleHelper.Dim("    まだ物語に関わる依頼は完了していない");
        }
        else
        {
            foreach (var quest in completedStories)
                Console.WriteLine($"    ・{quest.questName}");
        }

        ConsoleHelper.PressAnyKey();
    }
}
