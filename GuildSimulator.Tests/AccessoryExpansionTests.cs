using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

/// <summary>F〜D帯の店売りアクセサリーと、その探索入手経路を固定する。</summary>
[Collection("Guild static state")]
public sealed class AccessoryExpansionTests
{
    static readonly string[] AccessoryIds =
    {
        "eq_accessory_camp_prayer_stone",
        "eq_accessory_caravan_strap",
        "eq_accessory_moonsilver_charm",
        "eq_accessory_vanguard_tassel",
        "eq_accessory_blacksteel_ring",
        "eq_accessory_lifebond_amber",
    };

    static GameMasterData Load() => MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void NewAccessoriesProvideTwoDistinctShopChoicesPerRankBand()
    {
        var db = Load();
        var accessories = AccessoryIds.Select(id => db.equipment[id]).ToList();

        Assert.Equal(new[] { 1, 1, 2, 2, 3, 3 }, accessories.Select(item => item.shopTier));
        Assert.All(accessories, item =>
        {
            Assert.Equal(EquipmentType.Accessory, item.type);
            Assert.Equal(Rarity.Common, item.rarity);
            Assert.False(item.id.StartsWith(ShopService.DropOnlyIdPrefix, StringComparison.Ordinal));
            Assert.Equal(new[] { EquipSlot.Accessory }, item.GetAllowedSlots());
            Assert.True(item.price > 0);
            Assert.True(item.weight > 0);
        });

        Assert.Equal(10, db.equipment[AccessoryIds[0]].bonus.san);
        Assert.Equal(4, db.equipment[AccessoryIds[1]].bonus.carry);
        Assert.Equal(1, db.equipment[AccessoryIds[2]].bonus.mav);
        Assert.Equal(20, db.equipment[AccessoryIds[3]].bonus.threatWeight);
        Assert.Equal(5, db.equipment[AccessoryIds[4]].bonus.autoPenetrate);
        Assert.Equal(10, db.equipment[AccessoryIds[5]].bonus.emergencyHeal);
    }

    [Theory]
    [InlineData("eq_accessory_camp_prayer_stone", "dungeon_meadow", 1)]
    [InlineData("eq_accessory_caravan_strap", "dungeon_woods", 1)]
    [InlineData("eq_accessory_moonsilver_charm", "dungeon_woods", 2)]
    [InlineData("eq_accessory_vanguard_tassel", "dungeon_amashiro", 2)]
    [InlineData("eq_accessory_blacksteel_ring", "dungeon_mine", 3)]
    [InlineData("eq_accessory_lifebond_amber", "dungeon_crypt", 3)]
    public void EachAccessoryHasARankMatchedTreasureRoute(string equipmentId, string dungeonId, int rank)
    {
        var db = Load();
        var reward = Assert.Single(db.dungeons[dungeonId].treasureTable,
            entry => entry.Equipment?.id == equipmentId);

        Assert.Equal(RewardType.Equipment, reward.type);
        Assert.InRange(rank, reward.minQuestRank, reward.maxQuestRank);
        Assert.True(reward.weight > 0);
        Assert.False(reward.unique);
    }

    [Fact]
    public void CarryAccessoryActuallyReducesOverweightPenalty()
    {
        _ = new GuildManager();
        var db = Load();
        var adventurer = new AdventurerData(new AdventurerMasterData
        {
            id = "accessory_carry_test",
            baseName = "積載確認",
            defaultLevel = 1,
            defaultRank = Rank.Min,
            vitality = 6,
            mental = 10,
            strength = 6,
            agility = 10,
            intelligence = 10,
            constitution = 4,
            appearance = 10,
        });
        adventurer.SetEquipped(EquipSlot.RightHand, new EquipmentMasterData
        {
            id = "heavy_test_weapon",
            displayName = "試験用重量武器",
            type = EquipmentType.Weapon,
            weight = 14,
        });

        Assert.Equal(10, adventurer.CarryLimit);
        Assert.Equal(4, adventurer.OverweightAmount);

        adventurer.SetEquipped(EquipSlot.Accessory, db.equipment["eq_accessory_caravan_strap"]);

        Assert.Equal(4, adventurer.EquipmentCarryBonus);
        Assert.Equal(14, adventurer.CarryLimit);
        Assert.Equal(15, adventurer.TotalEquipmentWeight);
        Assert.Equal(1, adventurer.OverweightAmount);
    }
}
