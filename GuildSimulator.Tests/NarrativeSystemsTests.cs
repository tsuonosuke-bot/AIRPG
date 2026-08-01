using GuildSimulator.Game.Data;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using Xunit;

namespace GuildSimulator.Tests;

[Collection("Guild static state")]
public class NarrativeSystemsTests
{
    [Fact]
    public void MasterDataContainsProfilesCluesAndAStoryChain()
    {
        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        var db = MasterLoader.Load(dataDir);

        Assert.Contains(db.allAdventurers, a => !string.IsNullOrWhiteSpace(a.background));
        Assert.Contains("clue_stolen_blue_ore", db.clues.Keys);

        var goblin = Assert.Single(db.allQuests, q => q.id == "quest_goblin_slayer");
        var mine = Assert.Single(db.allQuests, q => q.id == "quest_mine_survey");
        Assert.True(goblin.isStoryQuest);
        Assert.Contains("clue_stolen_blue_ore", goblin.grantedClueIds);
        Assert.Contains(goblin.id, mine.requiredQuestIds);
        Assert.Contains("clue_stolen_blue_ore", mine.requiredClueIds);
        Assert.Empty(MasterValidator.Validate(db));
    }

    [Fact]
    public void CompletingStoryQuestRecordsClueAndUnlocksDependentQuest()
    {
        var guild = new GuildManager();
        var manager = new QuestManager(guild);
        var adventurer = new AdventurerData(BasicAdventurer());
        guild.AddAdventurer(adventurer);

        var clue = new StoryClueMasterData { id = "clue", title = "青い鉱石" };
        var first = new QuestMasterData
        {
            id = "first",
            questName = "最初の調査",
            totalPhases = 1,
            isStoryQuest = true,
            grantedClueIds = { clue.id },
            GrantedClues = { clue },
        };
        var next = new QuestMasterData
        {
            id = "next",
            questName = "続く調査",
            totalPhases = 1,
            isStoryQuest = true,
            requiredQuestIds = { first.id },
            requiredClueIds = { clue.id },
        };

        manager.FillBoard(new[] { next }, currentTurn: 1);
        Assert.Empty(manager.questBoard);

        var formation = new AdventurerData?[6];
        formation[0] = adventurer;
        Assert.True(manager.TryStartQuest(first, formation, 1, out _));
        var run = Assert.Single(manager.activeQuests);
        run.currentPhase = first.totalPhases;
        manager.FinalizeQuest(run);

        Assert.True(manager.HasClearedQuest(first.id));
        Assert.True(manager.HasDiscoveredClue(clue.id));
        Assert.Contains(clue.id, run.discoveredClueIds);
        Assert.Equal(1, adventurer.expeditionCount);
        Assert.Equal(1, adventurer.successfulExpeditionCount);

        manager.FillBoard(new[] { next }, currentTurn: 2);
        Assert.Equal(next, Assert.Single(manager.questBoard).quest);
    }

    [Fact]
    public void QuestStartStoresPolicyAndStructuredDeparture()
    {
        var guild = new GuildManager();
        var manager = new QuestManager(guild);
        var adventurer = new AdventurerData(BasicAdventurer());
        guild.AddAdventurer(adventurer);
        var formation = new AdventurerData?[6];
        formation[0] = adventurer;

        Assert.True(manager.TryStartQuest(
            new QuestMasterData { id = "q", questName = "調査", totalPhases = 2 },
            formation,
            currentTurn: 3,
            out _,
            policy: ExpeditionPolicy.SurvivalFirst));

        var run = Assert.Single(manager.activeQuests);
        Assert.Equal(ExpeditionPolicy.SurvivalFirst, run.policy);
        var departure = Assert.Single(run.reportEvents);
        Assert.Equal(ExpeditionEventKind.Departure, departure.kind);
        Assert.Contains("生還優先", departure.detail);
    }

    [Fact]
    public void SurvivalFirstRetreatsWhenAHealthyPartyWouldStillContinue()
    {
        var adventurer = new AdventurerData(BasicAdventurer())
        {
            CombatHpMax = 100,
            CombatHp = 20,
        };
        var enemy = new EnemyData(new EnemyMasterData
        {
            id = "enemy",
            baseName = "壁役",
            vitality = 100,
            mental = 1,
            strength = 0,
            agility = 0,
            intelligence = 0,
            constitution = 0,
        })
        {
            CombatHpMax = 1000,
            CombatHp = 1000,
        };
        var logs = new List<string>();

        var result = BattleResolver.Resolve(
            new IUnitMember?[] { adventurer, null, null, null, null, null },
            new IUnitMember?[] { enemy, null, null, null, null, null },
            logs,
            turn: 1,
            phase: 1,
            new MoraleState(100),
            ExpeditionPolicy.SurvivalFirst);

        Assert.True(result.adventurersRetreated);
        Assert.Equal(ExpeditionRetreatReason.SurvivalPolicy, result.retreatReason);
        Assert.Contains(logs, log => log.Contains("生還優先の命令"));
        Assert.True(adventurer.isAlive);
    }

    static AdventurerMasterData BasicAdventurer() => new()
    {
        id = "adv",
        baseName = "テスト",
        vitality = 10,
        mental = 10,
        strength = 10,
        agility = 10,
        intelligence = 10,
        constitution = 10,
    };
}
