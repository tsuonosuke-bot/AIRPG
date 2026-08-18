using GuildSimulator.Game.Data;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Game.Presentation;

namespace GuildSimulator.Game.Screens;

public static class StoryJournalScreen
{
    public static async Task ShowAsync(GameMasterData db, QuestManager questManager)
    {
        Ui.BeginScreen();
        Ui.Header("調査記録");

        var storyQuests = db.allQuests.Where(q => q.isStoryQuest).ToList();
        var completedStories = storyQuests
            .Where(q => questManager.HasClearedQuest(q.id))
            .ToList();
        QuestChoiceOptionData? selectedOutcome = db.choiceEvents.Values
            .SelectMany(choiceEvent => choiceEvent.options)
            .FirstOrDefault(option => !string.IsNullOrWhiteSpace(option.storyBranchId)
                && questManager.HasSelectedBranch(option.storyBranchId));

        Ui.WriteLine("  【青い鉱石事件】");
        Ui.WriteLine($"    調査進行: {completedStories.Count}/{storyQuests.Count}");
        if (selectedOutcome != null)
        {
            Ui.Info($"    結末: {selectedOutcome.text}");
            Ui.WriteLine($"       {selectedOutcome.storyOutcomeText}");
        }
        else if (questManager.HasClearedQuest(QuestManager.BlueOreFinalQuestId)
            && db.choiceEvents.TryGetValue("story_blue_ore_final_choice", out var finalChoice))
        {
            Ui.Warn("    旧版から引き継いだ記録には、遺物をどう扱ったかだけが残っていません");
            var choices = finalChoice.options.Select((option, index) => new MenuOption(
                (index + 1).ToString(),
                option.text,
                option.storyOutcomeText,
                TextStyle.Accent)).ToList();
            int? selected = await Ui.SelectIndexAsync("過去の判断を一度だけ確定する", choices, "あとで決める");
            if (selected.HasValue)
            {
                var option = finalChoice.options[selected.Value];
                if (questManager.TryRecordLegacyBlueOreOutcome(option.storyBranchId, out var result))
                {
                    selectedOutcome = option;
                    Ui.Info($"    結末: {option.text}");
                    Ui.WriteLine($"       {option.storyOutcomeText}");
                }
                else
                {
                    Ui.Warn($"    {result}");
                }
            }
            else
            {
                Ui.Dim("    結末は未確定。次に調査記録を開いたときにも選べます");
            }
        }
        else
        {
            ShowCurrentLead(storyQuests, questManager);
        }

        var clues = questManager.ExportDiscoveredClueIds()
            .Select(id => db.clues.GetValueOrDefault(id))
            .Where(clue => clue != null)
            .Select(clue => clue!)
            .ToList();

        Ui.WriteLine();
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
                var unlocked = storyQuests.FirstOrDefault(quest => quest.requiredClueIds.Contains(clue.id));
                if (unlocked != null)
                    Ui.Dim($"       → 次の調査「{unlocked.questName}」につながる");
                else if (selectedOutcome != null)
                    Ui.Dim("       → ギルドの決断によって事件の結末が確定した");
            }
        }

        Ui.WriteLine();
        Ui.WriteLine("  【完了した物語】");
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

    static void ShowCurrentLead(
        IReadOnlyList<QuestMasterData> storyQuests,
        QuestManager questManager)
    {
        var next = storyQuests
            .Where(quest => !questManager.HasClearedQuest(quest.id))
            .FirstOrDefault(quest => questManager.AreStoryRequirementsMet(quest));
        if (next == null)
        {
            if (storyQuests.All(quest => questManager.HasClearedQuest(quest.id)))
                Ui.Dim("    主要な調査は完了しているが、この古い記録には最終判断が残っていない");
            else
                Ui.Dim("    現在は次の調査につながる手掛かりを探している");
            return;
        }

        string state;
        if (questManager.activeQuests.Any(run => run.def == next))
            state = "進行中";
        else if (questManager.questBoard.Any(entry => entry.quest == next))
            state = "物語専用枠に掲示中";
        else if (next.rank > questManager.GuildRank)
            state = $"ギルドランク{Rank.Label(next.rank)}で調査可能";
        else
            state = "掲示準備中";

        Ui.Info($"    現在の調査: {next.questName}（{state}）");
        if (!string.IsNullOrWhiteSpace(next.description))
            Ui.WriteLine($"       {next.description}");
    }
}
