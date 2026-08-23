using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

/// <summary>
/// 敵ドロップの品揃えと確率を固定する。
///
/// ドロップが剣ばかりだと、剣以外を振る冒険者にとって戦利品が「売るだけの物」になり、
/// 敵を狩る動機が痩せる。ここでは<b>全武器種と防具・装飾品に敵ドロップの経路があること</b>と、
/// MASTER_DATA.md「敵ドロップの確率」の表どおりに `chance` が入っていることを見る。
/// </summary>
[Collection("Guild static state")]
public sealed class EnemyDropVarietyTests
{
    /// <summary>通常個体（遭遇表に出る敵）の基礎確率。</summary>
    static readonly Dictionary<Rarity, float> NormalChance = new()
    {
        [Rarity.Uncommon] = 0.01f,
        [Rarity.Rare] = 0.006f,
        [Rarity.Unique] = 0.004f,
        [Rarity.Legend] = 0.002f,
    };

    /// <summary>ボス専用個体の基礎確率。1クエストに1回しか判定が起きないぶん高い。</summary>
    static readonly Dictionary<Rarity, float> BossChance = new()
    {
        [Rarity.Uncommon] = 0.025f,
        [Rarity.Rare] = 0.015f,
        [Rarity.Unique] = 0.1f,
        [Rarity.Legend] = 0.04f,
    };

    const float NormalConsumableChance = 0.04f;
    const float BossConsumableChance = 0.06f;

    static GameMasterData Load() => MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void EveryWeaponTypeHasAnEnemyDropRoute()
    {
        var db = Load();

        var dropped = EnemyDroppedEquipment(db)
            .Where(e => e.type == EquipmentType.Weapon)
            .Select(e => e.weaponType)
            .ToHashSet();

        var expected = Enum.GetValues<WeaponType>().Where(t => t != WeaponType.Null).ToArray();
        Assert.All(expected, type =>
            Assert.True(dropped.Contains(type), $"{type} の敵ドロップがありません"));
    }

    [Fact]
    public void ArmorAndAccessoriesHaveEnemyDropRoutesInEveryArmorClass()
    {
        var db = Load();
        var dropped = EnemyDroppedEquipment(db).ToList();

        var armors = dropped.Where(e => e.type == EquipmentType.Armor).ToList();
        Assert.All(new[] { ArmorType.Cloth, ArmorType.LightArmor, ArmorType.Plate }, armorType =>
            Assert.True(armors.Any(e => e.armorType == armorType),
                $"{armorType} の防具ドロップがありません"));

        // 胴だけでなく頭も落ちないと、頭スロットは商店の鉄兜で止まってしまう。
        Assert.Contains(armors, e => e.GetAllowedSlots().Contains(EquipSlot.Head));
        Assert.Contains(armors, e => e.GetAllowedSlots().Contains(EquipSlot.Body));

        Assert.True(dropped.Count(e => e.type == EquipmentType.Accessory) >= 6,
            "装飾品の敵ドロップが少なすぎます");
        Assert.Contains(dropped, e => e.type == EquipmentType.Shield);
    }

    [Fact]
    public void EnemyDropEquipmentIsAlwaysDropOnly()
    {
        var db = Load();

        // 商店に並ぶ品が敵からも落ちると、ドロップが「買えば済む物」になってしまう。
        Assert.All(EnemyDroppedEquipment(db), e =>
            Assert.StartsWith(ShopService.DropOnlyIdPrefix, e.id));
    }

    [Fact]
    public void EveryDropChanceFollowsTheRarityTable()
    {
        var db = Load();
        var bossOnly = BossOnlyEnemyIds(db);

        foreach (var enemy in db.enemies.Values)
        foreach (var drop in enemy.dropTable)
        {
            bool isBoss = bossOnly.Contains(enemy.id);
            float expected = drop.Equipment != null
                ? (isBoss ? BossChance : NormalChance)[drop.Equipment.rarity]
                : isBoss ? BossConsumableChance : NormalConsumableChance;

            Assert.True(Math.Abs(drop.chance - expected) < 0.0001f,
                $"{enemy.id} の {drop.Equipment?.id ?? drop.Consumable?.id}: "
                + $"chance が {drop.chance} で、表の {expected} と違います");
        }
    }

    /// <summary>
    /// 遭遇表に出る敵のドロップが、ボス専用個体のドロップより出やすくなっていないか。
    /// 逆転すると、ボスを倒すより雑魚を狩り続けたほうが得になる。
    /// </summary>
    [Fact]
    public void BossOnlyEnemiesNeverDropLessOftenThanWanderingOnes()
    {
        var db = Load();
        var bossOnly = BossOnlyEnemyIds(db);

        foreach (var rarity in NormalChance.Keys)
            Assert.True(NormalChance[rarity] < BossChance[rarity], $"{rarity} の帯が逆転しています");

        // ボス専用個体が実際に存在しないと、上の帯は絵に描いた餅になる。
        Assert.Contains(db.enemies.Values,
            enemy => enemy.dropTable.Count > 0 && bossOnly.Contains(enemy.id));
    }

    static IEnumerable<EquipmentMasterData> EnemyDroppedEquipment(GameMasterData db) =>
        db.enemies.Values
            .SelectMany(enemy => enemy.dropTable)
            .Select(drop => drop.Equipment)
            .Where(equipment => equipment != null)
            .Select(equipment => equipment!)
            .Distinct();

    /// <summary>遭遇表に載らず、クエストのボスとしてしか出てこない敵。</summary>
    static HashSet<string> BossOnlyEnemyIds(GameMasterData db)
    {
        var encounterUnits = db.dungeons.Values
            .SelectMany(dungeon => dungeon.encounterTable)
            .Select(entry => entry.unitId)
            .ToHashSet();

        var wandering = db.enemyUnits.Values
            .Where(unit => encounterUnits.Contains(unit.id))
            .SelectMany(unit => unit.Formation)
            .Where(member => member != null)
            .Select(member => member!.id)
            .ToHashSet();

        return db.enemies.Values
            .Where(enemy => enemy.dropTable.Count > 0 && !wandering.Contains(enemy.id))
            .Select(enemy => enemy.id)
            .ToHashSet();
    }
}
