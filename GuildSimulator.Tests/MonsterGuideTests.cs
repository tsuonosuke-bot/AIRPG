using GuildSimulator.Cli;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Game.Data;
using GuildSimulator.Game.Presentation;
using GuildSimulator.Game.Screens;
using Xunit;

namespace GuildSimulator.Tests;

[Collection("Console presentation")]
public class MonsterGuideTests
{
    [Fact]
    public void QuestEncounterRegistersMonsterEvenWhenPartyIsDefeated()
    {
        var enemy = new EnemyMasterData
        {
            id = "enemy_newly_seen",
            baseName = "初遭遇モンスター",
            vitality = 1,
            mental = 1,
            strength = 1,
            agility = 1,
            intelligence = 1,
            constitution = 1,
            naturalDamageDice = "1d2",
            naturalPv = 1,
            threat = 1,
        };
        var unit = new EnemyUnitTemplate
        {
            id = "unit_newly_seen",
            unitName = enemy.baseName,
            Formation = new List<EnemyMasterData?> { enemy, null, null, null, null, null },
        };
        var quest = new QuestMasterData
        {
            id = "quest_newly_seen",
            questName = "図鑑登録テスト",
            totalPhases = 1,
            phasesPerTurn = 1,
            bossPhase = 1,
            BossEnemy = unit,
        };
        var run = new GuildSimulator.Core.GameData.QuestRun(quest, startedTurn: 1);
        var guild = new GuildManager(startGold: 100);
        var manager = new QuestManager(guild);
        manager.RestoreState(new(), new() { run }, Array.Empty<string>());

        manager.AdvanceAll(currentTurn: 2);

        Assert.True(guild.HasDiscoveredEnemy(enemy.id));
        Assert.Contains(run.logs, log => log.Contains("モンスター図鑑") && log.Contains(enemy.baseName));
    }

    [Fact]
    public async Task GuideShowsStatsForDiscoveredMonstersAndHidesUnknownOnes()
    {
        var db = MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var guild = new GuildManager(startGold: 100);
        guild.DiscoverEnemy(db.enemies["enemy_forest_wolf"]);

        string text = await CaptureConsoleAsync(
            "1\n\n0\n",
            () => MonsterGuideScreen.ShowAsync(db, guild));

        Assert.Contains($"登録数: 1/{db.enemies.Count}", text);
        Assert.Contains("【F】ルプス", text);
        Assert.Contains("HP:38 AV:0 DV:6", text);
        Assert.Contains("ダメージ:1d3 PV:4", text);
        Assert.Contains("獣の牙", text);
        Assert.DoesNotContain("マルフィサ", text);
    }

    [Fact]
    public async Task GuideShowsMonsterDropNameRarityAndBaseChance()
    {
        var db = MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var guild = new GuildManager(startGold: 100);
        guild.DiscoverEnemy(db.enemies["enemy_slime"]);

        string text = await CaptureConsoleAsync(
            "1\n\n0\n",
            () => MonsterGuideScreen.ShowAsync(db, guild));

        Assert.Contains("希少ドロップ", text);
        Assert.Contains("粘核の指輪", text);
        Assert.Contains("アンコモン", text);
        Assert.Contains("基礎1%", text);
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
