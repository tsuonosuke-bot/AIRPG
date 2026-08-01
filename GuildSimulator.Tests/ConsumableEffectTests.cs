using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

[Collection("Guild static state")]
public class ConsumableEffectTests
{
    [Fact]
    public void AdoptedConsumablesLoadFromMasterData()
    {
        var db = MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

        Assert.Equal(12, db.consumables.Count);
        Assert.Equal(ConsumableEffectType.RestHealPercent,
            db.consumables["item_camp_herbal_tea"].effectType);
        Assert.True(db.consumables["item_sharpeners_black_oil"].RequiresTarget);
        Assert.True(db.consumables["item_stardust_catalyst"].RequiresTarget);
        Assert.Equal(50, db.consumables["item_defiant_horn"].secondaryEffectValue);
        Assert.Empty(MasterValidator.Validate(db));

        var run = new QuestRun(new QuestMasterData { id = "wiring" }, 1);
        foreach (var id in new[]
                 {
                     "item_camp_herbal_tea", "item_explorers_pendulum",
                     "item_graverobbers_key", "item_defiant_horn", "item_mechanical_smoke_bomb",
                 })
            run.ApplyConsumable(db.consumables[id]);
        Assert.Equal(40, run.restHealBonusPercent);
        Assert.Equal(25, run.treasureFromNothingPercent);
        Assert.Equal(1, run.guaranteedNonEmptyChestCount);
        Assert.Equal(25, run.enemyFromNothingPercent);
        Assert.Equal(50, run.battleExpBonusPercent);
        Assert.Equal(25, run.emergencyRetreatHpPercent);
    }

    [Fact]
    public void PendulumMovesOnlyNothingWeightAndDoesNotReduceEnemyChance()
    {
        var dungeon = EventTestDungeon();

        var weights = QuestProgressor.CalculateEventWeights(
            dungeon, PartySkillEffects.None, treasureFromNothingPercent: 25, enemyFromNothingPercent: 0);

        Assert.Equal(17f, weights.Values.Sum(), 3);
        Assert.Equal(6f, weights[DungeonEventType.EnemyEncounter], 3);
        Assert.Equal(3.5f, weights[DungeonEventType.Treasure], 3);
        Assert.Equal(4.5f, weights[DungeonEventType.Nothing], 3);
    }

    [Fact]
    public void HornMovesNothingWeightToEnemiesWithoutReducingOtherEvents()
    {
        var dungeon = EventTestDungeon();

        var weights = QuestProgressor.CalculateEventWeights(
            dungeon, PartySkillEffects.None, treasureFromNothingPercent: 0, enemyFromNothingPercent: 25);

        Assert.Equal(17f, weights.Values.Sum(), 3);
        Assert.Equal(7.5f, weights[DungeonEventType.EnemyEncounter], 3);
        Assert.Equal(2f, weights[DungeonEventType.Treasure], 3);
        Assert.Equal(2f, weights[DungeonEventType.Trap], 3);
        Assert.Equal(4.5f, weights[DungeonEventType.Nothing], 3);
    }

    [Fact]
    public void TargetedOilAndCatalystAffectOnlyTheirChosenAdventurer()
    {
        var first = Adventurer("first", "前衛");
        var second = Adventurer("second", "後衛");
        var run = new QuestRun(new QuestMasterData { id = "q" }, 1);
        var oil = Consumable("oil", ConsumableEffectType.TargetPv, 1);
        var catalyst = Consumable("catalyst", ConsumableEffectType.TargetMpv, 1);

        run.ApplyConsumable(oil, first);
        run.ApplyConsumable(catalyst, second);

        var firstBonus = run.ConsumableCombatBonusFor(first);
        var secondBonus = run.ConsumableCombatBonusFor(second);
        Assert.Equal(1, firstBonus.pv);
        Assert.Equal(0, firstBonus.mpv);
        Assert.Equal(0, secondBonus.pv);
        Assert.Equal(1, secondBonus.mpv);
    }

    [Fact]
    public void TargetedConsumableRequiresAFormationMemberBeforeAnythingIsSpent()
    {
        var guild = new GuildManager();
        var member = Adventurer("member", "隊員");
        guild.AddAdventurer(member);
        var oil = Consumable("oil", ConsumableEffectType.TargetPv, 1);
        guild.AddConsumable(oil);
        var manager = new QuestManager(guild);
        var formation = new AdventurerData?[6];
        formation[0] = member;

        Assert.False(manager.TryStartQuestWithConsumables(
            new QuestMasterData { id = "q", totalPhases = 2 },
            formation, 1, out var error, new[] { new ConsumableUse(oil) }));
        Assert.Contains("対象", error);
        Assert.Equal(1, guild.GetConsumableCount(oil));

        Assert.True(manager.TryStartQuestWithConsumables(
            new QuestMasterData { id = "q2", totalPhases = 2 },
            formation, 1, out error, new[] { new ConsumableUse(oil, member) }), error);
        Assert.Equal(0, guild.GetConsumableCount(oil));
        Assert.Equal(1, manager.activeQuests.Single().targetPvBonusByAdventurerId[member.id]);
    }

    [Fact]
    public void HerbalTeaIncreasesRestHealing()
    {
        var member = Adventurer("member", "隊員");
        member.CombatHpMax = 100;
        member.CombatHp = 0;
        var quest = new QuestMasterData { id = "rest", totalPhases = 1 };
        quest.fixedEvents.Add(new QuestPhaseEvent { phase = 1, type = QuestEventType.ForceHeal });
        var run = new QuestRun(quest, 1) { restHealBonusPercent = 40, morale = new MoraleState(100) };
        run.formation[0] = member;

        new QuestProgressor().AdvanceOnePhase(run, 1);

        Assert.Equal(70, member.CombatHp);
    }

    [Fact]
    public void KeySkipsTheEmptyRollForTheFirstDungeonChest()
    {
        var dungeon = new DungeonMasterData { id = "dungeon" };
        dungeon.treasureTable.Add(new RewardEntryData
        {
            type = RewardType.Gold, gold = 10, weight = 1,
        });
        var run = new QuestRun(new QuestMasterData { id = "q", Dungeon = dungeon }, 1)
        {
            guaranteedNonEmptyChestCount = 1,
        };
        run.chests.Add(new TreasureChest { kind = TreasureChestKind.Dungeon, foundPhase = 1 });

        new QuestRewardService().OpenChests(run, new GuildManager(), "[完了]");

        Assert.Single(run.pendingLoot);
        Assert.Equal(0, run.guaranteedNonEmptyChestCount);
        Assert.Contains(run.logs, log => log.Contains("盗掘者の合鍵を使用"));
    }

    [Fact]
    public void MechanicalSmokeBombRetreatsBeforeAQuarterHpPartyFights()
    {
        var member = Adventurer("member", "隊員");
        member.CombatHpMax = 100;
        member.CombatHp = 25;
        var enemy = new EnemyData(new EnemyMasterData
        {
            id = "enemy", baseName = "訓練人形", vitality = 10, mental = 10,
            strength = 10, agility = 10, intelligence = 10, constitution = 10,
        })
        {
            CombatHpMax = 100,
            CombatHp = 100,
        };
        var logs = new List<string>();

        var result = BattleResolver.Resolve(
            new IUnitMember?[] { member, null, null, null, null, null },
            new IUnitMember?[] { enemy, null, null, null, null, null },
            logs, 1, 1, new MoraleState(100),
            ExpeditionPolicy.ObjectiveFirst,
            emergencyRetreatHpPercent: 25);

        Assert.True(result.adventurersRetreated);
        Assert.Equal(ExpeditionRetreatReason.SmokeBomb, result.retreatReason);
        Assert.Equal(0, result.rounds);
        Assert.Contains(logs, log => log.Contains("機関の煙玉"));
    }

    static DungeonMasterData EventTestDungeon()
    {
        var dungeon = new DungeonMasterData { id = "events" };
        dungeon.eventTable[DungeonEventType.EnemyEncounter] = 6;
        dungeon.eventTable[DungeonEventType.Nothing] = 6;
        dungeon.eventTable[DungeonEventType.Trap] = 2;
        dungeon.eventTable[DungeonEventType.Treasure] = 2;
        dungeon.eventTable[DungeonEventType.Heal] = 1;
        return dungeon;
    }

    static ConsumableMasterData Consumable(
        string id, ConsumableEffectType effectType, int value) => new()
    {
        id = id,
        displayName = id,
        effectType = effectType,
        effectValue = value,
    };

    static AdventurerData Adventurer(string id, string name) => new(new AdventurerMasterData
    {
        id = id,
        baseName = name,
        vitality = 10,
        mental = 10,
        strength = 10,
        agility = 10,
        intelligence = 10,
        constitution = 10,
    });
}
