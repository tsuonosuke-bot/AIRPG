using GuildSimulator.Cli;
using GuildSimulator.Game.Presentation;
using GuildSimulator.Game.Screens;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
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
        Assert.Empty(manager.activeQuests);
    }

    static AdventurerMasterData Master(string id, string name) => new()
    {
        id = id,
        baseName = name,
        defaultLevel = 1,
        defaultRank = 1,
    };
}
