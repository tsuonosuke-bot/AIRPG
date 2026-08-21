using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

public class SkywayExpansionTests
{
    static GameMasterData Load() => MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void AmashiroReplacesTheHighwayAndKeepsItsEnemiesBelowArea16()
    {
        var db = Load();

        Assert.DoesNotContain("dungeon_highway", db.dungeons.Keys);
        var amashiro = db.dungeons["dungeon_amashiro"];
        Assert.Equal("天城（あましろ）", amashiro.dungeonName);
        Assert.Equal("ミドルオーシャン", db.dungeons["dungeon_middle_ocean"].dungeonName);

        string[] formerHighwayUnits =
        {
            "unit_bandit_lookout", "unit_bandit_ambush", "unit_bandit_gang",
            "unit_wolf_pair", "unit_raven_flock", "unit_goblin_scouts",
            "unit_deserter_patrol", "unit_bandit_hawks", "unit_bandit_raiders",
            "unit_goblin_pair", "unit_bandit_back_line", "unit_highway_coalition",
        };
        foreach (string unitId in formerHighwayUnits)
        {
            var encounter = Assert.Single(amashiro.encounterTable, entry => entry.unitId == unitId);
            Assert.InRange(encounter.minPhase, 1, 15);
            Assert.InRange(encounter.maxPhase, encounter.minPhase, 15);
            Assert.NotNull(encounter.Unit);
        }

        Assert.Equal("dungeon_amashiro",
            db.allQuests.Single(quest => quest.id == "quest_caravan_escort").Dungeon?.id);
        Assert.Equal("dungeon_amashiro",
            db.allQuests.Single(quest => quest.id == "quest_bandit_raiders").Dungeon?.id);
        Assert.Equal(15, db.allQuests.Single(quest => quest.id == "quest_bandit_hunt").bossPhase);

        for (int phase = 1; phase <= 40; phase++)
            Assert.Contains(amashiro.encounterTable,
                entry => entry.weight > 0 && entry.Unit != null && entry.IsEligible(phase));

        int lowerThreat = amashiro.encounterTable
            .Where(entry => entry.IsEligible(15))
            .Max(MaxThreat);
        Assert.InRange(lowerThreat, 1, 3);
    }

    [Fact]
    public void AmashiroAscendsFromConstructsToWyverns()
    {
        var db = Load();
        var amashiro = db.dungeons["dungeon_amashiro"];

        AssertEncounterStarts(amashiro.encounterTable, "unit_amashiro_armor_pair", 16);
        AssertEncounterStarts(amashiro.encounterTable, "unit_amashiro_sentinel_patrol", 18);
        AssertEncounterStarts(amashiro.encounterTable, "unit_amashiro_construct_line", 21);
        AssertEncounterStarts(amashiro.encounterTable, "unit_wyvern_fledglings", 25);
        AssertEncounterStarts(amashiro.encounterTable, "unit_wyvern_roost", 28);
        AssertEncounterStarts(amashiro.encounterTable, "unit_wyvern_storm", 33);
        Assert.DoesNotContain(amashiro.encounterTable,
            entry => entry.unitId == "unit_amashiro_dragon_golem");

        var usurper = db.allQuests.Single(quest => quest.id == "quest_amashiro_usurper");
        Assert.Equal("unit_amashiro_dragon_golem", usurper.BossEnemy?.id);
        Assert.Equal(usurper.totalPhases, usurper.bossPhase);

        var lesser = db.enemies["enemy_wyvern_lesser"];
        var greater = db.enemies["enemy_wyvern_greater"];
        var storm = db.enemies["enemy_wyvern_storm"];
        Assert.True(lesser.threat < greater.threat);
        Assert.True(greater.threat < storm.threat);
    }

    [Fact]
    public void MiddleOceanContainsSkyFishSharksWhalesAndABossOnlyLeviathan()
    {
        var db = Load();
        var ocean = db.dungeons["dungeon_middle_ocean"];
        string[] normalUnits =
        {
            "unit_skyfish_school", "unit_storm_jelly_drift", "unit_sky_manta_pair",
            "unit_sky_shark_hunt", "unit_middle_ocean_predators", "unit_sky_whale",
        };
        Assert.All(normalUnits, unitId =>
            Assert.Contains(ocean.encounterTable, entry => entry.unitId == unitId && entry.Unit != null));
        Assert.DoesNotContain(ocean.encounterTable, entry => entry.unitId == "unit_middle_ocean_leviathan");

        var finalQuest = db.allQuests.Single(quest => quest.id == "quest_middle_ocean_heaven_gate");
        Assert.Equal("unit_middle_ocean_leviathan", finalQuest.BossEnemy?.id);
        Assert.Equal(finalQuest.totalPhases, finalQuest.bossPhase);

        var fish = db.enemies["enemy_skyfish"];
        var shark = db.enemies["enemy_sky_shark"];
        var whale = db.enemies["enemy_sky_whale"];
        var leviathan = db.enemies["enemy_middle_ocean_leviathan"];
        Assert.True(fish.threat < shark.threat);
        Assert.True(shark.threat < whale.threat);
        Assert.True(whale.threat < leviathan.threat);

        string[] newEnemies =
        {
            "enemy_amashiro_animated_armor", "enemy_amashiro_clockwork_sentinel",
            "enemy_amashiro_dragon_golem", "enemy_wyvern_greater", "enemy_wyvern_storm",
            "enemy_skyfish", "enemy_storm_jelly", "enemy_sky_manta", "enemy_sky_shark",
            "enemy_sky_whale", "enemy_middle_ocean_leviathan",
        };
        Assert.All(newEnemies, enemyId =>
        {
            var enemy = db.enemies[enemyId];
            Assert.False(string.IsNullOrWhiteSpace(enemy.baseName));
            Assert.False(string.IsNullOrWhiteSpace(enemy.description));
            Assert.False(string.IsNullOrWhiteSpace(enemy.naturalDamageDice));
            Assert.True(enemy.naturalPv > 0);
            Assert.InRange(enemy.threat, 1, 7);
        });
    }

    [Fact]
    public void AmashiroTreasureRemainsUsefulAtEveryStoryRank()
    {
        var db = Load();
        var rewards = db.dungeons["dungeon_amashiro"].treasureTable;

        foreach (int rank in new[] { 2, 4, 5, 6 })
        {
            var eligible = rewards.Where(reward =>
                reward.minQuestRank <= rank && reward.maxQuestRank >= rank).ToList();
            Assert.Contains(eligible, reward => reward.type == RewardType.Gold);
            Assert.Contains(eligible, reward => reward.type == RewardType.Equipment);
            Assert.Contains(eligible, reward => reward.type == RewardType.Consumable);
        }
    }

    static void AssertEncounterStarts(
        IReadOnlyList<EncounterEntry> encounters,
        string unitId,
        int expectedMinPhase)
    {
        var encounter = Assert.Single(encounters, entry => entry.unitId == unitId);
        Assert.Equal(expectedMinPhase, encounter.minPhase);
        Assert.True(encounter.weight > 0);
        Assert.NotNull(encounter.Unit);
        Assert.Equal(6, encounter.Unit!.Formation.Count);
        Assert.Contains(encounter.Unit.Formation, enemy => enemy != null);
    }

    static int MaxThreat(EncounterEntry encounter) => encounter.Unit?.Formation
        .Where(enemy => enemy != null)
        .Max(enemy => enemy!.threat) ?? 0;
}
