using GuildSimulator.Cli;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Game.Data;
using GuildSimulator.Game.Presentation;
using GuildSimulator.Game.Screens;
using Xunit;

namespace GuildSimulator.Tests;

[Collection("Console presentation")]
public class SkywayStoryJournalTests
{
    static GameMasterData Load() =>
        MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public async Task JournalHidesAmashiroUntilTheFlowingForestDiscovery()
    {
        var db = Load();
        var manager = new QuestManager(new GuildManager(startRank: 1));

        string text = await RenderAsync(db, manager);

        Assert.DoesNotContain("天城探索記", text);
        Assert.DoesNotContain("ミドルオーシャン", text);
        Assert.DoesNotContain("天城・濃霧の入口", text);
    }

    [Fact]
    public async Task ForestDiscoveryRevealsOnlyTheFirstAmashiroLead()
    {
        var db = Load();
        var manager = new QuestManager(new GuildManager(startRank: 2));
        manager.RestoreState(
            new(),
            new(),
            Array.Empty<string>(),
            clearedQuestIdsToRestore: new[] { "quest_deep_woods_scout" },
            discoveredClueIdsToRestore: new[] { "clue_amashiro_fogbound_silhouette" });

        string text = await RenderAsync(db, manager);

        Assert.Contains("天城探索記", text);
        Assert.Contains("天城・濃霧の入口", text);
        Assert.Contains("行程は未確定", text);
        Assert.DoesNotContain("ミドルオーシャン", text);
        Assert.DoesNotContain("天城・無言の関所", text);
    }

    [Fact]
    public async Task MiddleOceanNameAppearsOnlyAfterItIsSightedAboveTheClouds()
    {
        var db = Load();
        var manager = new QuestManager(new GuildManager(startRank: 6));
        manager.RestoreState(
            new(),
            new(),
            new[]
            {
                "quest_amashiro_last_caravan",
                "quest_amashiro_silent_guardians",
                "quest_amashiro_wyvern_roost",
            },
            clearedQuestIdsToRestore: new[]
            {
                "quest_deep_woods_scout",
                "quest_amashiro_last_caravan",
                "quest_amashiro_silent_guardians",
                "quest_amashiro_wyvern_roost",
            },
            discoveredClueIdsToRestore: new[]
            {
                "clue_amashiro_fogbound_silhouette",
                "clue_amashiro_last_waybill",
                "clue_amashiro_usurper_edict",
                "clue_amashiro_tide_chart",
            });

        string text = await RenderAsync(db, manager);

        Assert.Contains("天城とミドルオーシャン", text);
        Assert.Contains("天城・簒奪者の封鎖機", text);
    }

    static async Task<string> RenderAsync(GameMasterData db, QuestManager manager)
    {
        var originalIn = Console.In;
        var originalOut = Console.Out;
        using var input = new StringReader("\n");
        using var output = new StringWriter();
        try
        {
            Console.SetIn(input);
            Console.SetOut(output);
            Ui.Use(new ConsoleGameIo());
            await StoryJournalScreen.ShowAsync(db, manager);
            return output.ToString();
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
        }
    }
}
