using GuildSimulator.Cli;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Game.Presentation;
using Xunit;

namespace GuildSimulator.Tests;

[Collection("Console presentation")]
public class TurnSummaryRendererTests
{
    [Fact]
    public void SummarySeparatesCurrentValuesChangesAndNotableEvents()
    {
        var member = new AdventurerData(new AdventurerMasterData
        {
            id = "member",
            baseName = "ドラン",
            defaultLevel = 1,
            defaultRank = 1,
        })
        {
            CombatHpMax = 172,
            CombatHp = 138,
        };
        var quest = new QuestRun(new QuestMasterData
        {
            id = "summary",
            questName = "スライム駆除",
            totalPhases = 5,
        }, startedTurn: 1)
        {
            currentPhase = 5,
            morale = new MoraleState(190),
        };
        quest.formation[0] = member;
        quest.AddReportEvent(1, 0, ExpeditionEventKind.Departure, "ギルドを出発", "出発した");
        quest.AddReportEvent(6, 1, ExpeditionEventKind.Progress, "進行", "何も起きなかった");
        quest.AddReportEvent(6, 2, ExpeditionEventKind.Progress, "進行", "何も起きなかった");
        quest.AddReportEvent(
            6,
            3,
            ExpeditionEventKind.Encounter,
            "敵遭遇：コルヴスのつがい（脅威度F）",
            "勝利（HP 145/172 士気 190/190）");
        quest.AddReportEvent(6, 4, ExpeditionEventKind.Progress, "進行", "何も起きなかった");
        quest.AddReportEvent(
            6,
            5,
            ExpeditionEventKind.Encounter,
            "ボス遭遇：スライム2匹",
            "勝利（HP 138/172 士気 190/190） / 成長: ドラン Lv1→2 精神力+1",
            important: true);

        string text = CaptureConsole(() => TurnSummaryRenderer.Write(
            quest,
            beforePhase: 0,
            beforeHp: 172,
            beforeMorale: 190,
            beforeReportCount: 1));

        Assert.Contains("◆ スライム駆除", text);
        Assert.Contains("状態  達成・報酬受取待ち", text);
        Assert.Contains("進捗  エリア 5/5（今ターン +5）", text);
        Assert.Contains("HP    138/172（今ターン -34）", text);
        Assert.Contains("士気  190/190（変化なし）", text);
        Assert.Contains("・ 3/5  敵遭遇：コルヴスのつがい（脅威度F） → 勝利", text);
        Assert.Contains("◆ 5/5  ボス遭遇：スライム2匹 → 勝利", text);
        Assert.Contains("★ 成長  ドラン Lv1→2 精神力+1", text);
        Assert.Contains("・ほか3エリア：特記事項なし", text);
        Assert.DoesNotContain("ギルドを出発", text);
        Assert.DoesNotContain("HP 145/172", text);
        Assert.DoesNotContain("[Turn 6]", text);
    }

    [Fact]
    public void SummaryCountsDistinctPhasesAndDoesNotHideMixedPhaseEventsAsQuiet()
    {
        var quest = new QuestRun(new QuestMasterData
        {
            id = "phase-summary",
            questName = "静穏集計",
            totalPhases = 4,
        }, startedTurn: 1)
        {
            currentPhase = 4,
            morale = new MoraleState(100),
        };
        quest.AddReportEvent(2, 1, ExpeditionEventKind.Progress, "進行", "何も起きなかった");
        quest.AddReportEvent(2, 1, ExpeditionEventKind.Progress, "進行", "何も起きなかった");
        quest.AddReportEvent(2, 2, ExpeditionEventKind.Progress, "進行", "何も起きなかった");
        quest.AddReportEvent(2, 2, ExpeditionEventKind.Gather, "採取", "薬草を1個採取");
        quest.AddReportEvent(2, 3, ExpeditionEventKind.Progress, "進行", "何も起きなかった");
        quest.AddReportEvent(2, 4, ExpeditionEventKind.Encounter, "敵遭遇", "勝利");

        string text = CaptureConsole(() => TurnSummaryRenderer.Write(
            quest,
            beforePhase: 0,
            beforeHp: 0,
            beforeMorale: 100,
            beforeReportCount: 0));

        Assert.Contains("・ 2/4  採取 → 薬草を1個採取", text);
        Assert.Contains("・ほか2エリア：特記事項なし", text);
        Assert.DoesNotContain("ほか4エリア", text);
    }

    static string CaptureConsole(Action action)
    {
        var originalOut = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Ui.Use(new ConsoleGameIo());
            action();
            return output.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
