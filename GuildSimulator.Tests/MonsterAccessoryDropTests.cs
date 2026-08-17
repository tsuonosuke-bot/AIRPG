using GuildSimulator.Core;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

/// <summary>スライム・ゴブリン・ケルノス固有のアンコモン装飾品を固定する。</summary>
[Collection("Guild static state")]
public sealed class MonsterAccessoryDropTests
{
    static readonly string[] EquipmentIds =
    {
        "eq_drop_slime_core_ring",
        "eq_drop_goblin_ashfire_ring",
        "eq_drop_kernos_warhorn_ring",
    };

    static GameMasterData Load() => MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void NewMonsterAccessoriesAreUncommonAndDropOnly()
    {
        var db = Load();
        var items = EquipmentIds.Select(id => db.equipment[id]).ToList();

        Assert.All(items, item =>
        {
            Assert.Equal(EquipmentType.Accessory, item.type);
            Assert.Equal(Rarity.Uncommon, item.rarity);
            Assert.StartsWith(ShopService.DropOnlyIdPrefix, item.id);
            Assert.Equal(new[] { EquipSlot.Accessory }, item.GetAllowedSlots());
            Assert.True(item.price > 0);
            Assert.True(item.weight > 0);
        });

        Assert.Equal((1, 75, 1, 10), ItemShape(db.equipment[EquipmentIds[0]],
            item => item.bonus.offHandChance));
        Assert.Equal((2, 140, 1, 1), ItemShape(db.equipment[EquipmentIds[1]],
            item => item.bonus.mpv));
        Assert.Equal((2, 170, 2, 8), ItemShape(db.equipment[EquipmentIds[2]],
            item => item.bonus.blockNegate));
        Assert.Empty(MasterValidator.Validate(db));
    }

    [Theory]
    [InlineData("enemy_slime", "eq_drop_slime_core_ring", 0.015)]
    [InlineData("enemy_goblin_mage", "eq_drop_goblin_ashfire_ring", 0.03)]
    [InlineData("enemy_kernos", "eq_drop_kernos_warhorn_ring", 0.02)]
    public void AccessoriesComeFromTheirNamedMonster(string enemyId, string equipmentId, double chance)
    {
        var db = Load();
        var drop = Assert.Single(db.enemies[enemyId].dropTable,
            entry => entry.Equipment?.id == equipmentId);

        Assert.Equal(RewardType.Equipment, drop.type);
        Assert.Equal(chance, drop.chance, 4);
        Assert.Equal(1, drop.quantity);
        Assert.False(drop.unique);
    }

    [Fact]
    public void MonsterAccessoryEffectsReachFinalCombatStats()
    {
        var db = Load();
        var adventurer = Adventurer();
        var baseline = adventurer.GetFinalCombatStats();

        adventurer.SetEquipped(EquipSlot.Accessory, db.equipment[EquipmentIds[0]]);
        Assert.Equal(baseline.offHandChance + 10, adventurer.GetFinalCombatStats().offHandChance);

        adventurer.SetEquipped(EquipSlot.Accessory, db.equipment[EquipmentIds[1]]);
        Assert.Equal(baseline.mpv + 1, adventurer.GetFinalCombatStats().mpv);

        adventurer.SetEquipped(EquipSlot.Accessory, db.equipment[EquipmentIds[2]]);
        Assert.Equal(baseline.blockNegate + 8, adventurer.GetFinalCombatStats().blockNegate);
    }

    [Fact]
    public void EnemyDropRollsDoNotAdvanceTheCombatRandomSequence()
    {
        int expectedSecond;
        using (GameRandom.UseSeed(24680))
        {
            _ = GameRandom.Range(0, 1_000_000);
            expectedSecond = GameRandom.Range(0, 1_000_000);
        }

        int actualSecond;
        using (GameRandom.UseSeed(24680))
        {
            _ = GameRandom.Range(0, 1_000_000);
            for (int i = 0; i < 20; i++) _ = GameRandom.NextDropFloat();
            actualSecond = GameRandom.Range(0, 1_000_000);
        }

        Assert.Equal(expectedSecond, actualSecond);
    }

    static (int tier, int price, int weight, int effect) ItemShape(
        EquipmentMasterData item,
        Func<EquipmentMasterData, int> effect) =>
        (item.shopTier, item.price, item.weight, effect(item));

    static AdventurerData Adventurer() => new(new AdventurerMasterData
    {
        id = "monster_accessory_test",
        baseName = "固有装備確認",
        defaultLevel = 1,
        defaultRank = Rank.Min,
        vitality = 10,
        mental = 10,
        strength = 10,
        agility = 10,
        intelligence = 10,
        constitution = 10,
        appearance = 10,
    });
}
