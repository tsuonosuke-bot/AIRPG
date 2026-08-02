using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using Xunit;

namespace GuildSimulator.Tests;

public class AppearanceSystemTests
{
    static AdventurerData Adventurer(string name, int appearance) =>
        new(new AdventurerMasterData
        {
            id = name,
            baseName = name,
            vitality = 20,
            mental = 10,
            strength = 20,
            agility = 20,
            intelligence = 10,
            constitution = 10,
            appearance = appearance,
        })
        {
            CombatHpMax = 1_000,
            CombatHp = 1_000,
        };

    static QuestRun RunWith(AdventurerData member, int rank = 1, int guildPoints = 20)
    {
        var run = new QuestRun(new QuestMasterData
        {
            id = "appearance_test",
            questName = "容姿試験",
            rank = rank,
            rewardGuildPoints = guildPoints,
            rewardGold = 0,
            rewardExp = 0,
        }, 1)
        {
            morale = new MoraleState(100, 50),
        };
        run.formation[0] = member;
        return run;
    }

    [Fact]
    public void HighAppearanceAddsGuildPointsOnSuccessfulQuest()
    {
        var guild = new GuildManager();
        var run = RunWith(Adventurer("看板役", appearance: 12));

        new QuestRewardService().ApplyBaseRewards(run, guild, "[完了]");

        Assert.Equal(22, guild.GuildPoints);
        Assert.Contains(run.logs, line => line.Contains("容姿ボーナス +2"));
    }

    [Fact]
    public void HighAppearanceRestoresMoraleEachBattleRound()
    {
        var member = Adventurer("鼓舞役", appearance: 13);
        var enemy = new EnemyData(new EnemyMasterData
        {
            id = "dummy",
            baseName = "案山子",
            vitality = 1,
            mental = 1,
            strength = 1,
            agility = 1,
            intelligence = 1,
            constitution = 1,
            naturalPv = 0,
            naturalDamageDice = "1d1-1",
        })
        {
            CombatHpMax = 1,
            CombatHp = 1,
        };
        var morale = new MoraleState(100, 50);
        var logs = new List<string>();

        BattleResolver.Resolve(
            new IUnitMember?[] { member, null, null, null, null, null },
            new IUnitMember?[] { enemy, null, null, null, null, null },
            logs, 1, 1, morale);

        Assert.True(morale.Current >= 53);
        Assert.Contains(logs, line => line.Contains("華やかな存在感で士気 +3"));
    }

    [Fact]
    public void MerchantAndBanditChecksUseHighestAppearance()
    {
        var run = RunWith(Adventurer("交渉役", appearance: 12), rank: 2);

        string merchant = AppearanceSystem.ResolveHumanEncounter(
            run, 2, HumanEncounterKind.TravelingMerchant, dieRoll: 10);
        Assert.Contains("1d20=10+2=12", merchant);
        Assert.Contains(run.pendingLoot, loot => loot.type == RewardType.Gold && loot.gold == 10);

        string bandits = AppearanceSystem.ResolveHumanEncounter(
            run, 3, HumanEncounterKind.Bandits, dieRoll: 1);
        Assert.Contains("交渉がこじれ", bandits);
        Assert.Equal(45, run.morale.Current);
    }

    [Fact]
    public void ExceptionalMerchantResultAddsRarestAvailableItem()
    {
        var rareItem = new EquipmentMasterData
        {
            id = "rare_item",
            displayName = "星銀の装飾品",
            rarity = Rarity.Rare,
        };
        var run = RunWith(Adventurer("目利き役", appearance: 12));
        run.def.Dungeon = new DungeonMasterData
        {
            treasureTable =
            {
                new RewardEntryData
                {
                    type = RewardType.Equipment,
                    equipmentId = rareItem.id,
                    Equipment = rareItem,
                    weight = 1,
                },
            },
        };

        string result = AppearanceSystem.ResolveHumanEncounter(
            run, 2, HumanEncounterKind.TravelingMerchant, dieRoll: 16);

        Assert.Contains("希少品", result);
        Assert.Contains(run.pendingLoot, loot => loot.Equipment == rareItem);
    }

    [Theory]
    [InlineData(10, 1.0f)]
    [InlineData(7, 1.1f)]
    [InlineData(13, 1.1f)]
    [InlineData(20, 1.25f)]
    public void ExtremeAppearanceSlightlyRaisesTargetWeight(int appearance, float expected)
    {
        Assert.Equal(expected,
            AppearanceSystem.TargetWeightMultiplier(Adventurer("対象", appearance)), 3);
    }
}
