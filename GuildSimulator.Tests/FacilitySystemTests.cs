using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using Xunit;

namespace GuildSimulator.Tests;

public class FacilitySystemTests
{
    static FacilityMasterData BoardFacility() => new()
    {
        id = "fac_board", displayName = "掲示板の増築",
        buildCostGold = 100, upkeepGoldPerTurn = 10, requiredGuildRank = 1,
        questBoardBonus = 1,
    };

    static FacilityMasterData ShopFacility() => new()
    {
        id = "fac_shop", displayName = "商店の増築",
        buildCostGold = 100, upkeepGoldPerTurn = 5, requiredGuildRank = 1,
        shopLevelBonus = 1,
    };

    [Fact]
    public void BuildingFacilitySpendsGoldAndAddsUpkeep()
    {
        var guild = new GuildManager(startGold: 200);
        int upkeepBefore = guild.BaseUpkeepPerTurn;

        Assert.True(guild.TryBuildFacility(BoardFacility(), out var reason), reason);

        Assert.Equal(100, guild.Gold);
        Assert.Equal(upkeepBefore + 10, guild.BaseUpkeepPerTurn);
        Assert.Single(guild.facilities);
    }

    [Fact]
    public void CannotBuildSameFacilityTwiceOrWithoutFunds()
    {
        var guild = new GuildManager(startGold: 100);
        Assert.True(guild.TryBuildFacility(BoardFacility(), out _));
        Assert.False(guild.TryBuildFacility(BoardFacility(), out var dupReason));
        Assert.Contains("建設済み", dupReason);

        var poorGuild = new GuildManager(startGold: 10);
        Assert.False(poorGuild.TryBuildFacility(BoardFacility(), out var goldReason));
        Assert.Contains("資金", goldReason);
    }

    [Fact]
    public void CannotBuildFacilityBelowRequiredGuildRank()
    {
        var guild = new GuildManager(startGold: 500, startRank: 1);
        var facility = BoardFacility();
        facility.requiredGuildRank = 3;

        Assert.False(guild.TryBuildFacility(facility, out var reason));
        Assert.Contains("ランク", reason);
    }

    [Fact]
    public void QuestBoardCapacityGrowsWithFacilityBonus()
    {
        var guild = new GuildManager(startGold: 500);
        var manager = new QuestManager(guild);
        int baseCapacity = manager.NormalBoardCapacity;

        Assert.True(guild.TryBuildFacility(BoardFacility(), out _));

        Assert.Equal(baseCapacity + 1, manager.NormalBoardCapacity);
    }

    [Fact]
    public void ShopLevelBonusUnlocksHigherTierEquipment()
    {
        var guild = new GuildManager(startGold: 500);
        var tier1 = new EquipmentMasterData { id = "eq_t1", displayName = "初級", price = 10, shopTier = 1 };
        var tier2 = new EquipmentMasterData { id = "eq_t2", displayName = "上級", price = 10, shopTier = 2 };
        var equipment = new[] { tier1, tier2 };
        var consumables = Array.Empty<ConsumableMasterData>();

        ShopService.RefreshIfNeeded(guild, 1, equipment, consumables);
        Assert.Contains("eq_t1", guild.shopEquipmentStock.Keys);
        Assert.DoesNotContain("eq_t2", guild.shopEquipmentStock.Keys);

        Assert.True(guild.TryBuildFacility(ShopFacility(), out _));
        ShopService.RefreshIfNeeded(guild, 6, equipment, consumables);
        Assert.Contains("eq_t2", guild.shopEquipmentStock.Keys);
    }

    [Fact]
    public void RestoringFacilitiesReappliesTheirEffects()
    {
        var guild = new GuildManager(startGold: 500);
        Assert.True(guild.TryBuildFacility(BoardFacility(), out _));

        // 新しいGuildManager（＝素のstatic状態）へ復元すると効果が引き継がれることを確認する。
        var restored = new GuildManager();
        restored.RestoreFacilities(new[] { BoardFacility() });
        var manager = new QuestManager(restored);

        Assert.Equal(1, restored.FacilityUpkeepPerTurn);
        Assert.Equal(4, manager.NormalBoardCapacity);
    }
}
