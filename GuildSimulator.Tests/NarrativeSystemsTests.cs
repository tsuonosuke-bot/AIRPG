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
        string[] blueOreIds =
        {
            "quest_goblin_slayer", "quest_mine_survey", "quest_old_city_relic",
        };
        string[] skywayIds =
        {
            "quest_amashiro_last_caravan",
            "quest_amashiro_silent_guardians",
            "quest_amashiro_wyvern_roost",
            "quest_amashiro_usurper",
            "quest_middle_ocean_crossing",
            "quest_middle_ocean_heaven_gate",
        };
        var blueOre = db.allQuests.Where(quest => blueOreIds.Contains(quest.id)).ToList();
        var skyway = db.allQuests.Where(quest => skywayIds.Contains(quest.id)).ToList();
        Assert.Equal(3, blueOre.Count);
        Assert.Equal(6, skyway.Count);
        Assert.All(blueOre, quest =>
        {
            Assert.Equal("blue_ore", quest.storyArcId);
            Assert.Equal("青い鉱石事件", quest.storyArcTitle);
            Assert.Single(quest.FixedChoiceEvents);
        });
        Assert.All(skyway, quest =>
        {
            Assert.Equal("skyway", quest.storyArcId);
            Assert.Equal("天城探索記", quest.storyArcTitle);
            Assert.Single(quest.FixedChoiceEvents);
        });

        var forestDiscovery = Assert.Single(db.allQuests,
            quest => quest.id == "quest_deep_woods_scout");
        Assert.Equal("dungeon_woods", forestDiscovery.Dungeon?.id);
        Assert.Contains("clue_amashiro_fogbound_silhouette", forestDiscovery.grantedClueIds);
        Assert.Equal("story_woods_amashiro_discovery",
            Assert.Single(forestDiscovery.FixedChoiceEvents).choiceEventId);
        Assert.Contains(forestDiscovery.id, skyway[0].requiredQuestIds);

        string EarlyDiscoveryText(QuestMasterData quest) => string.Join(" ",
            new[] { quest.storyArcTitle, quest.questName, quest.description }
                .Concat(quest.GrantedClues.Select(clue => $"{clue.title} {clue.description}"))
                .Concat(quest.FixedChoiceEvents.SelectMany(fixedEvent =>
                {
                    var choice = fixedEvent.ChoiceEvent!;
                    return new[] { choice.title, choice.description }
                        .Concat(choice.options.SelectMany(option =>
                            new[] { option.text, option.resultText }));
                })));
        Assert.DoesNotContain("ミドルオーシャン", EarlyDiscoveryText(skyway[0]));
        Assert.DoesNotContain("ミドルオーシャン", EarlyDiscoveryText(skyway[1]));
        Assert.Contains("ミドルオーシャン", EarlyDiscoveryText(skyway[2]));

        foreach (string lowQuestId in new[] { "quest_caravan_escort", "quest_bandit_raiders" })
        {
            var lowQuest = Assert.Single(db.allQuests, quest => quest.id == lowQuestId);
            Assert.Contains(skyway[0].id, lowQuest.requiredQuestIds);
            Assert.Contains("clue_amashiro_last_waybill", lowQuest.requiredClueIds);
        }
        var routeRecovery = Assert.Single(db.allQuests, quest => quest.id == "quest_bandit_hunt");
        Assert.Contains(skyway[2].id, routeRecovery.requiredQuestIds);
        Assert.Contains("clue_amashiro_tide_chart", routeRecovery.requiredClueIds);

        string[] skywayClues =
        {
            "clue_amashiro_last_waybill",
            "clue_amashiro_usurper_edict",
            "clue_amashiro_tide_chart",
            "clue_amashiro_heaven_key",
            "clue_middle_ocean_trade_manifest",
            "clue_middle_ocean_celestial_barrier",
        };
        for (int index = 0; index < skyway.Count; index++)
        {
            Assert.Contains(skywayClues[index], skyway[index].grantedClueIds);
            if (index == 0) continue;
            Assert.Contains(skyway[index - 1].id, skyway[index].requiredQuestIds);
            Assert.Contains(skywayClues[index - 1], skyway[index].requiredClueIds);
        }

        var finalChoice = db.choiceEvents["story_blue_ore_final_choice"];
        Assert.Equal(3, finalChoice.options.Count);
        Assert.All(finalChoice.options, option =>
        {
            Assert.NotNull(option.GrantedClue);
            Assert.False(string.IsNullOrWhiteSpace(option.storyBranchId));
            Assert.False(string.IsNullOrWhiteSpace(option.storyOutcomeText));
        });
        var celestialBarrier = db.choiceEvents["story_middle_ocean_celestial_barrier"];
        Assert.Equal(3, celestialBarrier.options.Count);
        Assert.Equal(3, celestialBarrier.options.Select(option => option.storyBranchId).Distinct().Count());
        Assert.All(celestialBarrier.options, option =>
        {
            Assert.Equal("clue_middle_ocean_celestial_barrier", option.grantedClueId);
            Assert.NotNull(option.GrantedClue);
            Assert.False(string.IsNullOrWhiteSpace(option.storyBranchId));
            Assert.False(string.IsNullOrWhiteSpace(option.storyOutcomeText));
        });
        Assert.DoesNotContain("dungeon_heaven", db.dungeons.Keys);
        Assert.Empty(MasterValidator.Validate(db));
    }

    [Fact]
    public void FirstAmashiroStoryRequiresTheFlowingForestDiscovery()
    {
        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        var db = MasterLoader.Load(dataDir);
        var forest = Assert.Single(db.allQuests, quest => quest.id == "quest_deep_woods_scout");
        var firstAmashiro = Assert.Single(db.allQuests,
            quest => quest.id == "quest_amashiro_last_caravan");

        var manager = new QuestManager(new GuildManager(startRank: 2));
        manager.FillBoard(new[] { firstAmashiro }, currentTurn: 1);
        Assert.Empty(manager.questBoard);

        manager.RestoreState(
            new(),
            new(),
            Array.Empty<string>(),
            clearedQuestIdsToRestore: new[] { forest.id });
        manager.FillBoard(new[] { firstAmashiro }, currentTurn: 2);
        Assert.Equal(firstAmashiro, Assert.Single(manager.questBoard).quest);

        var noviceManager = new QuestManager(new GuildManager(startRank: 1));
        noviceManager.RestoreState(
            new(),
            new(),
            Array.Empty<string>(),
            clearedQuestIdsToRestore: new[] { forest.id });
        noviceManager.FillBoard(new[] { firstAmashiro }, currentTurn: 2);
        Assert.Empty(noviceManager.questBoard);
    }

    [Fact]
    public void StoryQuestHasAPersistentDedicatedBoardSlot()
    {
        var guild = new GuildManager(startRank: Rank.Min);
        var manager = new QuestManager(guild);
        var story = new QuestMasterData
        {
            id = "story",
            questName = "消えない調査",
            rank = Rank.Min,
            isStoryQuest = true,
        };
        var normal = Enumerable.Range(1, 8)
            .Select(index => new QuestMasterData
            {
                id = $"normal_{index}",
                questName = $"通常依頼{index}",
                rank = Rank.Min,
            })
            .ToList();

        manager.FillBoard(normal.Prepend(story), currentTurn: 1);

        Assert.Single(manager.questBoard, entry => entry.quest == story);
        Assert.Equal(manager.NormalBoardCapacity,
            manager.questBoard.Count(entry => !entry.quest.isStoryQuest && !entry.quest.isEmergencyQuest));

        manager.RefreshBoard(normal.Prepend(story), currentTurn: manager.BoardExpireTurns + 1);

        var persistent = Assert.Single(manager.questBoard, entry => entry.quest == story);
        Assert.Equal(1, persistent.postedTurn);
    }

    [Fact]
    public void LegacyCompletedStoryCanRecordOneMissingOutcomeButCannotOverwriteIt()
    {
        var manager = new QuestManager(new GuildManager(startRank: Rank.Max));

        Assert.False(manager.TryRecordLegacyBlueOreOutcome(
            QuestManager.BlueOreSealedBranchId, out _));

        manager.RestoreState(
            new List<QuestBoardEntry>(),
            new List<QuestRun>(),
            new[] { QuestManager.BlueOreFinalQuestId },
            clearedQuestIdsToRestore: new[] { QuestManager.BlueOreFinalQuestId });

        Assert.True(manager.TryRecordLegacyBlueOreOutcome(
            QuestManager.BlueOreSealedBranchId, out var result), result);
        Assert.True(manager.HasSelectedBranch(QuestManager.BlueOreSealedBranchId));
        Assert.False(manager.TryRecordLegacyBlueOreOutcome(
            QuestManager.BlueOreTradedBranchId, out _));
        Assert.False(manager.HasSelectedBranch(QuestManager.BlueOreTradedBranchId));
    }

    [Fact]
    public void FixedStoryChoiceBlocksCompletionThenGrantsClueBranchAndWorldEffect()
    {
        var guild = new GuildManager(startGold: 500, startRank: Rank.Max);
        var manager = new QuestManager(guild);
        var adventurer = new AdventurerData(BasicAdventurer());
        guild.AddAdventurer(adventurer);

        var clue = new StoryClueMasterData
        {
            id = "clue_final",
            title = "最後の刻印",
            description = "鉱脈を制御する刻印。",
        };
        var choice = new QuestChoiceEventMasterData
        {
            id = "fixed_story_choice",
            title = "刻印をどう扱うか",
            options =
            {
                new QuestChoiceOptionData
                {
                    text = "交易に使う",
                    resultText = "商人組合へ渡した。",
                    effectType = QuestChoiceEffectType.Gold,
                    value = 10,
                    grantedClueId = clue.id,
                    GrantedClue = clue,
                    storyBranchId = QuestManager.BlueOreTradedBranchId,
                    storyOutcomeText = "交易が始まった。",
                },
            },
        };
        var storyQuest = new QuestMasterData
        {
            id = "story_final",
            questName = "最後の調査",
            rank = Rank.Min,
            totalPhases = 1,
            phasesPerTurn = 1,
            isStoryQuest = true,
            fixedEvents =
            {
                new QuestPhaseEvent
                {
                    phase = 1,
                    type = QuestEventType.ForceChoice,
                    choiceEventId = choice.id,
                    ChoiceEvent = choice,
                },
            },
        };
        var formation = new AdventurerData?[6];
        formation[0] = adventurer;
        Assert.True(manager.TryStartQuest(storyQuest, formation, 1, out _));

        manager.AdvanceAll(currentTurn: 2);

        var run = Assert.Single(manager.activeQuests);
        Assert.True(run.ObjectiveReached);
        Assert.False(run.ReachedGoal);
        Assert.Equal(choice, run.pendingChoice?.Event);

        Assert.True(manager.ResolveChoice(run, 0, out var result), result);
        Assert.True(run.ReachedGoal);
        Assert.True(manager.HasDiscoveredClue(clue.id));
        Assert.True(manager.HasSelectedBranch(QuestManager.BlueOreTradedBranchId));
        Assert.Equal(new[] { clue.id }, manager.ExportDiscoveredClueIds());
        Assert.Contains(run.reportEvents, e => e.clueId == clue.id && e.detail.Contains(clue.description));
        manager.FinalizeQuest(run);

        var underground = new QuestMasterData
        {
            id = "underground",
            questName = "交易後の廃坑",
            rank = Rank.Min,
            totalPhases = 1,
            Dungeon = new DungeonMasterData { id = "dungeon_mine", dungeonName = "廃坑" },
        };
        Assert.True(manager.TryStartQuest(underground, formation, 3, out _));
        var affected = Assert.Single(manager.activeQuests);
        Assert.Equal(15, affected.goldRewardBonusPercent);
        Assert.Equal(10, affected.enemyFromNothingPercent);
        Assert.Contains(affected.reportEvents, e => e.title == "物語の余波");
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
