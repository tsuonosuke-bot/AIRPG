using GuildSimulator.Cli.Data;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using Xunit;

namespace GuildSimulator.Tests;

public class FeatureExpansionTests
{
    [Fact]
    public void ShopStockRefreshesOnTurnOneAndThenTurnSix()
    {
        var guild = new GuildManager();
        var equipment = Enumerable.Range(1, 10).Select(i => new EquipmentMasterData
        {
            id = $"eq_{i}", displayName = $"装備{i}", price = 10,
        }).ToList();
        var items = Enumerable.Range(1, 5).Select(i => new ConsumableMasterData
        {
            id = $"item_{i}", displayName = $"道具{i}", price = 10,
        }).ToList();

        Assert.True(ShopService.RefreshIfNeeded(guild, 1, equipment, items));
        var initialEquipment = new Dictionary<string, int>(guild.shopEquipmentStock);

        Assert.False(ShopService.RefreshIfNeeded(guild, 5, equipment, items));
        Assert.Equal(initialEquipment, guild.shopEquipmentStock);
        Assert.True(ShopService.RefreshIfNeeded(guild, 6, equipment, items));
        Assert.Equal(6, guild.LastShopRefreshTurn);
    }

    [Fact]
    public void CarriedConsumableIsSpentAndAppliesForTheQuest()
    {
        var guild = new GuildManager();
        var adventurer = new AdventurerData(BasicAdventurer());
        guild.AddAdventurer(adventurer);
        var tonic = new ConsumableMasterData
        {
            id = "tonic",
            displayName = "生命の霊薬",
            effectType = ConsumableEffectType.MaxHpPercent,
            effectValue = 20,
        };
        guild.AddConsumable(tonic);
        var manager = new QuestManager(guild);
        var formation = new AdventurerData?[6];
        formation[0] = adventurer;

        Assert.True(manager.TryStartQuest(
            new QuestMasterData { id = "q", totalPhases = 10 },
            formation, 1, out var error, new[] { tonic }), error);

        Assert.Equal(0, guild.GetConsumableCount(tonic));
        Assert.Contains("tonic", manager.activeQuests.Single().usedConsumableIds);
        Assert.True(adventurer.CombatHpMax > adventurer.GetFinalCombatStats().hp);
    }

    [Fact]
    public void TurnEndChoiceBlocksUntilResolved()
    {
        var guild = new GuildManager();
        var adventurer = new AdventurerData(BasicAdventurer());
        guild.AddAdventurer(adventurer);
        var choice = new QuestChoiceEventMasterData
        {
            id = "choice",
            title = "分かれ道",
            weight = 1,
            options =
            {
                new QuestChoiceOptionData
                {
                    text = "調べる", resultText = "資金を発見",
                    effectType = QuestChoiceEffectType.Gold, value = 25,
                },
                new QuestChoiceOptionData { text = "進む", resultText = "何もなし" },
            },
        };
        var dungeon = new DungeonMasterData { turnEndEventChance = 1f };
        dungeon.turnEndEvents.Add(choice);
        var quest = new QuestMasterData
        {
            id = "q", totalPhases = 10, phasesPerTurn = 1, Dungeon = dungeon,
        };
        var manager = new QuestManager(guild);
        var formation = new AdventurerData?[6];
        formation[0] = adventurer;
        Assert.True(manager.TryStartQuest(quest, formation, 1, out _));

        manager.AdvanceAll(2);

        var run = manager.activeQuests.Single();
        Assert.True(manager.HasPendingChoices);
        Assert.Equal(1, run.currentPhase);
        manager.AdvanceAll(3);
        Assert.Equal(1, run.currentPhase);

        Assert.True(manager.ResolveChoice(run, 0, out var result));
        Assert.StartsWith("資金を発見", result);
        Assert.Contains("ゴールド+25", result);
        Assert.False(manager.HasPendingChoices);
        Assert.Contains(run.pendingLoot, x => x.type == RewardType.Gold && x.gold == 25);
    }

    [Fact]
    public void LearnedClassSkillRemainsActiveAfterChangingClass()
    {
        var skill = new SkillMasterData { id = "skill", skillName = "達人技" };
        var firstClass = new ClassMasterData { id = "first", className = "第一職" };
        firstClass.classSkills.Add(new ClassSkillEntry
        {
            skillId = skill.id, Skill = skill, requiredClearCount = 0,
        });
        var secondClass = new ClassMasterData { id = "second", className = "第二職" };
        var master = BasicAdventurer();
        master.DefaultClass = firstClass;
        master.defaultClassId = firstClass.id;
        var adventurer = new AdventurerData(master);

        adventurer.ChangeClass(secondClass);

        Assert.Contains(skill, adventurer.Skills);
    }

    [Fact]
    public void MasterDataResolvesRareDropsAndRarities()
    {
        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        var db = MasterLoader.Load(dataDir);

        Assert.Contains(db.equipment.Values, e => e.rarity == Rarity.Legend);
        Assert.Contains(db.allAdventurers, a => a.rarity != Rarity.Common);
        Assert.Contains(db.enemies.Values.SelectMany(e => e.dropTable),
            d => d.Equipment != null || d.Consumable != null);
        Assert.Empty(MasterValidator.Validate(db));
    }

    static AdventurerMasterData BasicAdventurer() => new()
    {
        id = "adv",
        baseName = "テスト",
        defaultLevel = 1,
        defaultRank = 1,
        vitality = 10,
        mental = 10,
        strength = 10,
        agility = 10,
        intelligence = 10,
        constitution = 10,
    };
}
