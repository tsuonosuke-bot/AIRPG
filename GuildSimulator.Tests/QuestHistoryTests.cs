using GuildSimulator.Cli;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Game.Presentation;
using GuildSimulator.Game.Screens;
using Xunit;

namespace GuildSimulator.Tests;

[Collection("Console presentation")]
public class QuestHistoryTests
{
    [Fact]
    public void ShortLogsArePreservedWithoutAnOmissionMarker()
    {
        string[] source = { "最初の短いログ", "最後の短いログ" };

        var history = new QuestHistoryEntry(
            "short", "短い依頼", 1, 2, QuestHistoryOutcome.Success, source);

        Assert.Equal(source, history.Logs);
        Assert.DoesNotContain(QuestHistoryEntry.OmissionMarker, history.Logs);
    }

    [Fact]
    public void LongLogsKeepTheNewestContentWithinLineAndCharacterBudgets()
    {
        var source = Enumerable.Range(0, QuestHistoryEntry.MaxLogLines + 50)
            .Select(index => $"LOG_{index:D4}_{new string('x', 100)}")
            .ToList();
        source[0] += "_OLDEST_MUST_BE_DROPPED";
        source[^1] += "_NEWEST_MUST_REMAIN";

        var history = new QuestHistoryEntry(
            "long", "長大な依頼", 1, 20, QuestHistoryOutcome.Success, source);

        Assert.InRange(history.Logs.Count, 1, QuestHistoryEntry.MaxLogLines);
        Assert.InRange(history.LogCharacterCount, 1, QuestHistoryEntry.MaxLogCharacters);
        Assert.Equal(QuestHistoryEntry.OmissionMarker, history.Logs[0]);
        Assert.Contains("NEWEST_MUST_REMAIN", history.Logs[^1]);
        Assert.DoesNotContain(history.Logs, line => line.Contains("OLDEST_MUST_BE_DROPPED"));
        Assert.Equal(
            QuestManager.QuestHistoryLimit * QuestHistoryEntry.MaxLogCharacters,
            QuestManager.QuestHistoryLogCharacterBudget);
        Assert.True(QuestManager.QuestHistoryLogCharacterBudget <= 1_200_000);
    }

    [Fact]
    public void CharacterBoundaryMarksAPartialOlderLineAndKeepsNewestLineWhole()
    {
        string newest = "NEWEST_LINE_IS_WHOLE";
        var history = new QuestHistoryEntry(
            "partial",
            "途中行の依頼",
            1,
            2,
            QuestHistoryOutcome.Success,
            new[] { new string('a', QuestHistoryEntry.MaxLogCharacters * 2), newest });

        Assert.Equal(QuestHistoryEntry.OmissionMarker, history.Logs[0]);
        Assert.StartsWith(QuestHistoryEntry.PartialLineMarker, history.Logs[1]);
        Assert.Equal(newest, history.Logs[^1]);
        Assert.True(history.LogCharacterCount <= QuestHistoryEntry.MaxLogCharacters);
    }

    [Theory]
    [InlineData(700, 1)]
    [InlineData(300, 160)]
    public void TruncatedLatestBattleKeepsItsStartAndRemainsIndexable(
        int actionCount,
        int fillerLength)
    {
        const string battleStart =
            "[Turn 99] エリア 9: 戦闘開始 冒険者 vs 巨獣 Lv99 ×1";
        var source = new List<string> { "この行は古いため省略される", battleStart };
        for (int index = 0; index < actionCount; index++)
            source.Add(
                $"  エリア 9: ACTION_{index:D4}_{new string('戦', fillerLength)}"
                + (index == actionCount - 1 ? "_LATEST_BATTLE_ACTION" : ""));
        source.Add("[Turn 99] エリア 9/9: 敵遭遇：巨獣 - 敗北（HP 0/999 士気 0/99）");

        var history = new QuestHistoryEntry(
            "bounded_battle",
            "長期戦の依頼",
            1,
            99,
            QuestHistoryOutcome.Failure,
            source);

        Assert.Equal(QuestHistoryEntry.OmissionMarker, history.Logs[0]);
        Assert.Equal(battleStart, history.Logs[1]);
        Assert.InRange(history.Logs.Count, 1, QuestHistoryEntry.MaxLogLines);
        Assert.InRange(history.LogCharacterCount, 1, QuestHistoryEntry.MaxLogCharacters);
        var battle = Assert.Single(QuestLogIndexer.Build(history.Logs).Battles);
        Assert.Equal("敗北（HP 0/999 士気 0/99）", battle.Result);
        Assert.Contains(battle.Lines, line => line.Contains("LATEST_BATTLE_ACTION"));
    }

    [Fact]
    public void FinalizingQuestsKeepsOnlyTheNewestThirtyImmutableSnapshots()
    {
        var manager = new QuestManager(new GuildManager(startGold: 100, startRank: 1));

        for (int index = 0; index < QuestManager.QuestHistoryLimit + 5; index++)
        {
            var run = new QuestRun(new QuestMasterData
            {
                id = $"history_{index}",
                questName = $"履歴{index}",
                totalPhases = 1,
            }, startedTurn: index + 1)
            {
                failed = true,
            };
            run.logs.Add($"LOG_{index}");
            manager.activeQuests.Add(run);

            manager.FinalizeQuest(run);
            run.logs.Add("FINALIZE_AFTER_CAPTURE");
        }

        Assert.Equal(QuestManager.QuestHistoryLimit, manager.QuestHistory.Count);
        Assert.Equal("history_5", manager.QuestHistory[0].QuestId);
        Assert.Equal($"history_{QuestManager.QuestHistoryLimit + 4}", manager.QuestHistory[^1].QuestId);
        Assert.DoesNotContain("FINALIZE_AFTER_CAPTURE", manager.QuestHistory[^1].Logs);
    }

    [Fact]
    public async Task RecordsScreenCanReopenArchivedExpeditionLogs()
    {
        var manager = new QuestManager(new GuildManager(startGold: 100, startRank: 1));
        var history = new QuestHistoryEntry(
            "removed_quest",
            "今はマスタにない依頼",
            startedTurn: 3,
            completedTurn: 4,
            QuestHistoryOutcome.Success,
            new[] { "[Turn 4] エリア 2/2: ARCHIVED_EXPEDITION_LOG" });
        manager.RestoreState(
            new List<QuestBoardEntry>(),
            new List<QuestRun>(),
            Array.Empty<string>(),
            questHistoryToRestore: new[] { history });

        string text = await CaptureConsoleAsync(
            "1\ne\n0\n0\n0\n",
            () => QuestHistoryScreen.ShowAsync(manager));

        Assert.Contains("完了クエスト履歴", text);
        Assert.Contains("成功  今はマスタにない依頼", text);
        Assert.Contains("遠征ログ: 今はマスタにない依頼", text);
        Assert.Contains("ARCHIVED_EXPEDITION_LOG", text);
        Assert.Contains("クエスト履歴へ戻る", text);
    }

    [Fact]
    public void FinalizeAddsMissingTerminalReportEventsWithoutMovingDepartureToTheEnd()
    {
        var manager = new QuestManager(new GuildManager(startGold: 100, startRank: 1));
        var run = new QuestRun(new QuestMasterData
        {
            id = "report_snapshot",
            questName = "報告保存依頼",
            totalPhases = 1,
        }, startedTurn: 1)
        {
            currentPhase = 1,
        };
        run.logs.Add("[Turn 2] エリア 1/1: 進行 - 既存ログ");
        run.AddReportEvent(1, 0, ExpeditionEventKind.Departure, "出発", "DEPARTURE_ONLY");
        run.AddReportEvent(2, 1, ExpeditionEventKind.Discovery, "新発見", "DISCOVERY_ONLY", important: true);
        manager.activeQuests.Add(run);

        manager.FinalizeQuest(run);

        var history = Assert.Single(manager.QuestHistory);
        Assert.Contains(history.Logs, line => line.Contains("DISCOVERY_ONLY"));
        Assert.Contains(history.Logs, line => line.Contains("[遠征報告/完了]") && line.Contains("報告保存依頼"));
        Assert.DoesNotContain(history.Logs, line => line.Contains("DEPARTURE_ONLY"));
        Assert.Contains("[遠征報告/発見]", history.Logs[^2]);
        Assert.Contains("[遠征報告/完了]", history.Logs[^1]);
    }

    [Theory]
    [InlineData("1\n\n", "特性「記憶の特性」を得た")]
    [InlineData("0\n\n", "特性の開花を見送った")]
    public async Task TraitDecisionUpdatesQuestLogAndMatchingCompletedHistory(
        string inputText,
        string expectedLog)
    {
        var guild = new GuildManager(startGold: 100, startRank: 1);
        var manager = new QuestManager(guild);
        var adventurer = new AdventurerData(new AdventurerMasterData
        {
            id = "trait_adv",
            baseName = "記憶する冒険者",
            defaultLevel = 1,
            defaultRank = 1,
        });
        guild.AddAdventurer(adventurer);
        var run = new QuestRun(new QuestMasterData
        {
            id = "trait_history",
            questName = "特性履歴依頼",
            totalPhases = 1,
        }, startedTurn: 4)
        {
            currentPhase = 1,
            retreated = true,
        };
        run.formation[0] = adventurer;
        run.logs.Add("BASE_LOG");
        manager.activeQuests.Add(run);
        manager.FinalizeQuest(run);

        var skill = new SkillMasterData { id = "trait_memory_skill", skillName = "記憶の力" };
        var trait = new TraitMasterData
        {
            id = "trait_memory",
            traitName = "記憶の特性",
            Skill = skill,
            skillId = skill.id,
            description = "履歴に残る",
        };
        run.pendingTraitOffers.Add(new TraitOffer(
            adventurer,
            "は記憶を選ぶ時を迎えた",
            "遠征の記録",
            new[] { trait }));

        await CaptureConsoleAsync(
            inputText,
            () => ActiveQuestScreen.ResolveTraitOffersAsync(run, manager));

        Assert.Contains(run.logs, line => line.Contains(expectedLog));
        var historyLogs = manager.QuestHistory[^1].Logs;
        Assert.Contains(historyLogs, line => line.Contains(expectedLog));
        int retreatIndex = historyLogs
            .Select((line, index) => (line, index))
            .Single(item => item.line.Contains("[遠征報告/撤退]"))
            .index;
        int traitIndex = historyLogs
            .Select((line, index) => (line, index))
            .Single(item => item.line.Contains(expectedLog))
            .index;
        Assert.True(retreatIndex < traitIndex);
        Assert.Empty(run.pendingTraitOffers);
    }

    static async Task<string> CaptureConsoleAsync(string inputText, Func<Task> action)
    {
        var originalIn = Console.In;
        var originalOut = Console.Out;
        using var input = new StringReader(inputText);
        using var output = new StringWriter();
        try
        {
            Console.SetIn(input);
            Console.SetOut(output);
            Ui.Use(new ConsoleGameIo());
            await action();
            return output.ToString();
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
        }
    }
}
