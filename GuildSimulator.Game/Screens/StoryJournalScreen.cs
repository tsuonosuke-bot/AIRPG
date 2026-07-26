using GuildSimulator.Game.Data;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Game.Presentation;

namespace GuildSimulator.Game.Screens;

public static class StoryJournalScreen
{
    public static async Task ShowAsync(GameMasterData db, QuestManager questManager)
    {
        Ui.BeginScreen();
        Ui.Header("調査記録");

        var clues = db.clues.Values
            .Where(clue => questManager.HasDiscoveredClue(clue.id))
            .OrderBy(clue => clue.id)
            .ToList();

        Ui.WriteLine("  【発見した手掛かり】");
        if (clues.Count == 0)
        {
            Ui.Dim("    まだ世界の謎につながる手掛かりはない");
        }
        else
        {
            foreach (var clue in clues)
            {
                Ui.Info($"    ◆ {clue.title}");
                Ui.WriteLine($"       {clue.description}");
            }
        }

        Ui.WriteLine();
        Ui.WriteLine("  【完了した物語】");
        var completedStories = db.allQuests
            .Where(q => q.isStoryQuest && questManager.HasClearedQuest(q.id))
            .ToList();
        if (completedStories.Count == 0)
        {
            Ui.Dim("    まだ物語に関わる依頼は完了していない");
        }
        else
        {
            foreach (var quest in completedStories)
                Ui.WriteLine($"    ・{quest.questName}");
        }

        await Ui.PauseAsync();
    }
}
