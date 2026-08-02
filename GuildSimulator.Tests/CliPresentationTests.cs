using GuildSimulator.Cli;
using GuildSimulator.Game.Data;
using GuildSimulator.Game.Presentation;
using GuildSimulator.Game.Screens;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using Xunit;

namespace GuildSimulator.Tests;

[CollectionDefinition("Console presentation", DisableParallelization = true)]
public sealed class ConsolePresentationCollection
{
}

[Collection("Console presentation")]
public class CliPresentationTests
{
    [Fact]
    public async Task RetreatReportNamesActualCasualtiesAndShowsSettlementSummary()
    {
        var survivor = new AdventurerData(Master("survivor", "生還者"));
        var casualty = new AdventurerData(Master("casualty", "戦没者"))
        {
            isAlive = false,
        };
        var guild = new GuildManager(startGold: 100);
        guild.AddAdventurer(survivor);
        guild.AddAdventurer(casualty);
        var manager = new QuestManager(guild);
        var quest = new QuestMasterData
        {
            id = "retreat",
            questName = "撤退テスト",
            totalPhases = 10,
        };
        var run = new QuestRun(quest, startedTurn: 1)
        {
            currentPhase = 5,
            retreated = true,
            retreatReason = ExpeditionRetreatReason.SurvivalPolicy,
            guildUpkeepAtStart = guild.EffectiveUpkeepPerTurn,
        };
        run.formation[0] = survivor;
        run.formation[1] = casualty;
        run.startingLevels[survivor.id] = survivor.level;
        run.startingLevels[casualty.id] = casualty.level;
        manager.RestoreState(new(), new() { run }, Array.Empty<string>());

        var originalIn = Console.In;
        var originalOut = Console.Out;
        using var input = new StringReader("\ny\n\n");
        using var output = new StringWriter();
        try
        {
            Console.SetIn(input);
            Console.SetOut(output);

            Ui.Use(new ConsoleGameIo());
            await ActiveQuestScreen.HandleQuestAsync(run, manager, guild);
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
        }

        string text = output.ToString();
        Assert.Contains("死亡者: 戦没者", text);
        Assert.DoesNotContain("死亡者はいません", text);
        Assert.Contains("クエスト終了サマリー", text);
        Assert.Contains("結果: 撤退", text);
        Assert.Contains("生還優先の方針", text);
        Assert.DoesNotContain("士気が尽き", text);
        Assert.Empty(manager.activeQuests);
    }

    [Fact]
    public async Task RecruitScreenRendersEachCandidateOnlyOnce()
    {
        var candidate = Master("candidate", "重複しない候補");
        var guild = new GuildManager(startGold: 100);

        string text = await CaptureConsoleAsync(
            "0\n",
            () => RecruitScreen.ShowAsync(
                new List<AdventurerMasterData> { candidate },
                guild,
                currentTurn: 1,
                new[] { candidate },
                maxCandidateCount: 3));

        Assert.Equal(1, CountOccurrences(text, candidate.baseName));
        Assert.Contains("VIT:", text);
        Assert.Contains("SIZ:", text);
        Assert.DoesNotContain("CON:", text);
    }

    [Fact]
    public async Task QuestBoardRendersQuestNumberAndNameOnlyOnce()
    {
        var guild = new GuildManager(startGold: 100);
        var manager = new QuestManager(guild);
        var quest = new QuestMasterData
        {
            id = "single",
            questName = "重複しない依頼",
            rewardGold = 50,
        };
        manager.questBoard.Add(new QuestBoardEntry(quest, postedTurn: 1));

        string text = await CaptureConsoleAsync(
            "0\n",
            () => QuestBoardScreen.ShowAsync(manager, guild, currentTurn: 1));

        Assert.Equal(1, CountOccurrences(text, quest.questName));
        Assert.Contains("1. 【F】", text);
        Assert.DoesNotContain("1. 1. 【F】", text);
        Assert.Contains("基本報酬", text);
        Assert.DoesNotContain("予想収支", text);
        Assert.DoesNotContain("宝箱・敵ドロップ・選択イベントは上の概算に含みません", text);
    }

    [Fact]
    public async Task QuestBoardShowsDetailScreenBeforeAcceptingQuest()
    {
        var guild = new GuildManager(startGold: 100);
        var manager = new QuestManager(guild);
        var quest = new QuestMasterData
        {
            id = "detail",
            questName = "詳細確認クエスト",
            description = "掲示板には出ない詳しい依頼内容",
            rewardGold = 50,
        };
        manager.questBoard.Add(new QuestBoardEntry(quest, postedTurn: 1));

        string text = await CaptureConsoleAsync(
            "1\nn\n0\n",
            () => QuestBoardScreen.ShowAsync(manager, guild, currentTurn: 1));

        Assert.Contains("掲示板には出ない詳しい依頼内容", text);
        Assert.Contains("このクエストを受注しますか？", text);
    }

    [Fact]
    public async Task QuestDetailsKeepVerboseLogsCollapsedByDefault()
    {
        var guild = new GuildManager(startGold: 100);
        var manager = new QuestManager(guild);
        var quest = new QuestMasterData
        {
            id = "active",
            questName = "ログ折り畳みテスト",
            totalPhases = 10,
        };
        var run = new QuestRun(quest, startedTurn: 1)
        {
            currentPhase = 1,
        };
        run.logs.Add("既定では隠れる戦闘計算ログ");
        manager.RestoreState(new(), new() { run }, Array.Empty<string>());

        string text = await CaptureConsoleAsync(
            "\n",
            () => ActiveQuestScreen.HandleQuestAsync(run, manager, guild));

        Assert.Contains("詳細ログを見る（全1件）", text);
        Assert.Contains("エリア: 1/10", text);
        Assert.DoesNotContain("Phase", text);
        Assert.DoesNotContain("フェーズ", text);
        Assert.DoesNotContain("詳細ログ (", text);
        Assert.DoesNotContain("既定では隠れる戦闘計算ログ", text);
    }

    [Fact]
    public async Task RecruitMinimumFacilityShowsItsActualEffect()
    {
        var db = MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var facility = db.facilities["fac_recruitment_office_01"];
        var guild = new GuildManager(startGold: 200);

        string text = await CaptureConsoleAsync(
            "0\n",
            () => FacilityScreen.ShowAsync(db, guild));

        Assert.Equal(1, facility.recruitMinBonus);
        Assert.Contains("雇入れ候補の最低人数+1", text);
        Assert.DoesNotContain("効果なし", text);
    }

    [Fact]
    public async Task EquippingShieldShowsItsIntrinsicEffectInChangeSummary()
    {
        var adventurer = new AdventurerData(new AdventurerMasterData
        {
            id = "shield_user",
            baseName = "盾役",
            defaultLevel = 1,
            defaultRank = 1,
            vitality = 10,
            mental = 8,
            strength = 10,
            agility = 8,
            intelligence = 6,
            constitution = 10,
        });
        var shield = new EquipmentMasterData
        {
            id = "test_shield",
            displayName = "テスト小盾",
            type = EquipmentType.Shield,
            blockChance = 25,
            blockAv = 4,
            weight = 3,
        };
        var guild = new GuildManager(startGold: 100);
        guild.AddAdventurer(adventurer);
        guild.AddEquipment(shield, 1);

        string text = await CaptureConsoleAsync(
            "1\ne\n2\n1\n\n0\n0\n0\n",
            () => AdventurerScreen.ShowAsync(new GameMasterData(), guild));

        Assert.Contains("ステータス・装備変化", text);
        Assert.Contains("装備効果: なし → [盾] 受け25% 受け成功時AV+4 重量3", text);
        Assert.DoesNotContain("ステータス・装備変化:\r\n── Enterで続ける", text);
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

    static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    static AdventurerMasterData Master(string id, string name) => new()
    {
        id = id,
        baseName = name,
        defaultLevel = 1,
        defaultRank = 1,
    };
}
