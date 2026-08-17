using GuildSimulator.Cli;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Game.Presentation;
using GuildSimulator.Game.Screens;
using Xunit;

namespace GuildSimulator.Tests;

[Collection("Console presentation")]
public class DetailedQuestLogTests
{
    [Fact]
    public void IndexSeparatesTwoBattlesFromExpeditionLogsWithoutLossOrDuplication()
    {
        var logs = SampleLogs();

        var index = QuestLogIndexer.Build(logs);

        Assert.Equal(2, index.Battles.Count);
        Assert.Contains(index.ExpeditionLogs, line => line.Contains("OTHER_ONLY"));
        Assert.Contains(index.ExpeditionLogs, line => line.Contains("図鑑登録_ONLY"));
        Assert.DoesNotContain(index.ExpeditionLogs, line => line.Contains("BATTLE_A_ONLY"));
        Assert.DoesNotContain(index.ExpeditionLogs, line => line.Contains("BATTLE_B_ONLY"));
        Assert.Contains(index.Battles[0].Lines, line => line.Contains("BATTLE_A_ONLY"));
        Assert.DoesNotContain(index.Battles[0].Lines, line => line.Contains("BATTLE_B_ONLY"));
        Assert.Contains(index.Battles[1].Lines, line => line.Contains("BATTLE_B_ONLY"));
        Assert.Equal(2, index.Battles[0].Rounds);
        Assert.Equal(logs.Count, index.ExpeditionLogs.Count + index.Battles.Sum(battle => battle.Lines.Count));
    }

    [Fact]
    public void IndexUnderstandsLegacyPhaseBattleLogs()
    {
        var index = QuestLogIndexer.Build(new[]
        {
            "[Turn 11] Phase 1: 戦闘開始 冒険者 vs 旧形式の敵 Lv1 ×1",
            "  Phase 1: LEGACY_BATTLE_ONLY",
            "[Turn 11] Phase 1/10: 敵遭遇：旧形式の敵 - 勝利（HP 10/10 士気 8/8）",
            "[Turn 11] Phase 2/10: 進行 - LEGACY_OTHER_ONLY",
        });

        var battle = Assert.Single(index.Battles);
        Assert.Equal(11, battle.Turn);
        Assert.Equal(1, battle.Phase);
        Assert.Contains(battle.Lines, line => line.Contains("LEGACY_BATTLE_ONLY"));
        Assert.Single(index.ExpeditionLogs);
        Assert.Contains("LEGACY_OTHER_ONLY", index.ExpeditionLogs[0]);
    }

    [Fact]
    public async Task PlayerCanChooseOneBattleWithoutOpeningTheOtherBattleDetails()
    {
        var quest = QuestWithSampleLogs();

        // 戦闘一覧は新しい順なので、2番がエリア3の戦闘A。
        string text = await CaptureConsoleAsync(
            "b\n2\n0\n0\n0\n",
            () => QuestLogScreen.ShowAsync(quest));

        Assert.Contains("戦闘ログ一覧", text);
        Assert.Contains("勝利 / 2ラウンド / 詳細4件", text);
        Assert.Contains("戦闘ログ: Turn 6 エリア 3", text);
        Assert.Contains("BATTLE_A_ONLY", text);
        Assert.DoesNotContain("BATTLE_B_ONLY", text);
        Assert.Contains("── ラウンド 1 ──", text);
        Assert.DoesNotContain("エリア 3: BATTLE_A_ONLY", text);
    }

    [Fact]
    public async Task ExpeditionLogViewDoesNotContainBattleActions()
    {
        var quest = QuestWithSampleLogs();

        string text = await CaptureConsoleAsync(
            "e\n0\n0\n",
            () => QuestLogScreen.ShowAsync(quest));

        Assert.Contains("遠征ログ", text);
        Assert.Contains("OTHER_ONLY", text);
        Assert.Contains("図鑑登録_ONLY", text);
        Assert.DoesNotContain("BATTLE_A_ONLY", text);
        Assert.DoesNotContain("BATTLE_B_ONLY", text);
        Assert.DoesNotContain("戦闘開始 冒険者", text);
    }

    static QuestRun QuestWithSampleLogs()
    {
        var quest = new QuestRun(new QuestMasterData
        {
            id = "log_hierarchy",
            questName = "ログ階層テスト",
            totalPhases = 5,
        }, startedTurn: 1);
        quest.logs.AddRange(SampleLogs());
        return quest;
    }

    static List<string> SampleLogs() => new()
    {
        "[Turn 6] エリア 1/5: 進行 - 特記事項なし",
        "  エリア 3: 図鑑登録_ONLY",
        "[Turn 6] エリア 3: 戦闘開始 冒険者 vs 敵A Lv1 ×1",
        "  ── ラウンド 1 ──",
        "  エリア 3: BATTLE_A_ONLY",
        "  ── ラウンド 2 ──",
        "[Turn 6] エリア 3/5: 敵遭遇：敵A - 勝利（HP 10/10 士気 8/8）",
        "[Turn 6] エリア 4/5: 進行 - OTHER_ONLY",
        "[Turn 6] エリア 5: 戦闘開始 冒険者 vs 敵B Lv1 ×2",
        "  ── ラウンド 1 ──",
        "  エリア 5: BATTLE_B_ONLY",
        "[Turn 6] エリア 5/5: ボス遭遇：敵B - 勝利（HP 9/10 士気 8/8）",
    };

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
