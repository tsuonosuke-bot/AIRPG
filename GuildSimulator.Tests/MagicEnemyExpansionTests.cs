using GuildSimulator.Core.GameData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

public class MagicEnemyExpansionTests
{
    static readonly IReadOnlyDictionary<string, int> AddedEnemyThreats =
        new Dictionary<string, int>
        {
            ["enemy_ember_wisp"] = 2,
            ["enemy_storm_harpy"] = 3,
            ["enemy_amashiro_arc_sentinel"] = 4,
            ["enemy_crypt_oracle"] = 5,
            ["enemy_tempest_siren"] = 6,
            ["enemy_void_dragon"] = 7,
        };

    static readonly string[] AddedUnitIds =
    {
        "unit_ember_wisp_pair",
        "unit_ember_wisp_ambush",
        "unit_storm_harpy_pair",
        "unit_storm_harpy_school",
        "unit_amashiro_arc_sentinel",
        "unit_amashiro_arc_line",
        "unit_crypt_oracle",
        "unit_crypt_oracle_procession",
        "unit_tempest_siren",
        "unit_tempest_chorus",
        "unit_void_dragon",
        "unit_void_dragon_retinue",
    };

    static readonly IReadOnlyDictionary<string, (string EquipmentId, float Chance)> AddedDrops =
        new Dictionary<string, (string, float)>
        {
            ["enemy_ember_wisp"] = ("eq_drop_wisp_flarewand", 0.01f),
            ["enemy_storm_harpy"] = ("eq_drop_harpy_windspear", 0.006f),
            ["enemy_amashiro_arc_sentinel"] = ("eq_drop_arc_sentinel_shield", 0.006f),
            ["enemy_crypt_oracle"] = ("eq_drop_oracle_veil", 0.006f),
            ["enemy_tempest_siren"] = ("eq_drop_siren_windwand", 0.006f),
            ["enemy_void_dragon"] = ("eq_drop_void_dragon_helm", 0.002f),
        };

    static GameMasterData Load() =>
        MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void MageLupusIsAnERankNaturalMagicAttacker()
    {
        var db = Load();
        var mageLupus = new EnemyData(db.enemies["enemy_mage_wolf"]);

        Assert.Equal("メイジルプス", mageLupus.Name);
        Assert.Equal(2, mageLupus.Threat);
        Assert.Null(mageLupus.Weapon);
        Assert.Equal(AttackKind.Magic, mageLupus.master.naturalAttackKind);
        Assert.True(mageLupus.IsMagicAttack);
    }

    [Fact]
    public void EveryRankFromEToSHasAtLeastTwoMagicEnemies()
    {
        var db = Load();

        foreach (int threat in Enumerable.Range(2, 6))
        {
            var magicEnemies = db.enemies.Values
                .Where(enemy => enemy.threat == threat)
                .Where(enemy => new EnemyData(enemy).IsMagicAttack)
                .ToList();

            Assert.True(magicEnemies.Count >= 2,
                $"{Rank.Label(threat)}ランクの魔法敵が{magicEnemies.Count}種類しかいません");
        }
    }

    [Fact]
    public void AddedEnemiesUseNaturalMagicAtTheirIntendedThreats()
    {
        var db = Load();

        foreach ((string id, int threat) in AddedEnemyThreats)
        {
            var enemy = new EnemyData(db.enemies[id]);
            Assert.Equal(threat, enemy.Threat);
            Assert.Null(enemy.Weapon);
            Assert.Equal(AttackKind.Magic, enemy.master.naturalAttackKind);
            Assert.True(enemy.IsMagicAttack);
            Assert.False(string.IsNullOrWhiteSpace(enemy.master.description));
            Assert.False(string.IsNullOrWhiteSpace(enemy.DamageDice));
        }
    }

    [Fact]
    public void AddedMagicEnemiesStayInsideTheirRankBands()
    {
        var warnings = MasterBandChecker.Check(Load());

        foreach (string enemyId in AddedEnemyThreats.Keys)
            Assert.DoesNotContain(warnings, warning => warning.Contains(enemyId));
    }

    [Fact]
    public void AddedEnemiesEachHaveOneResolvedDropOnlyEquipment()
    {
        var db = Load();

        foreach ((string enemyId, (string equipmentId, float chance)) in AddedDrops)
        {
            var drop = Assert.Single(db.enemies[enemyId].dropTable);

            Assert.Equal(equipmentId, drop.equipmentId);
            Assert.NotNull(drop.Equipment);
            Assert.StartsWith("eq_drop_", drop.Equipment!.id);
            Assert.Equal(1, drop.quantity);
            Assert.False(drop.unique);
            Assert.True(Math.Abs(drop.chance - chance) < 0.0001f,
                $"{enemyId} のドロップ確率が通常敵のレアリティ表と一致しません");
        }
    }

    [Fact]
    public void AddedDropsAreSplitBetweenUnderrepresentedWeaponsAndDefenses()
    {
        var db = Load();
        var equipment = AddedDrops.Values
            .Select(drop => db.equipment[drop.EquipmentId])
            .ToList();
        var weapons = equipment.Where(item => item.type == EquipmentType.Weapon).ToList();
        var defenses = equipment.Where(item => item.type is EquipmentType.Armor or EquipmentType.Shield).ToList();

        Assert.Equal(3, weapons.Count);
        Assert.Equal(3, defenses.Count);
        Assert.Equal(
            new[] { WeaponType.Spear, WeaponType.Fire, WeaponType.Wind }.OrderBy(type => type),
            weapons.Select(item => item.weaponType).OrderBy(type => type));
        Assert.Contains(defenses, item => item.type == EquipmentType.Shield);
        Assert.Contains(defenses, item => item.type == EquipmentType.Armor
            && item.armorType == ArmorType.Cloth
            && item.GetAllowedSlots().Contains(EquipSlot.Head));
        Assert.Contains(defenses, item => item.type == EquipmentType.Armor
            && item.armorType == ArmorType.Plate
            && item.GetAllowedSlots().Contains(EquipSlot.Head));
    }

    [Fact]
    public void AddedDropEquipmentStaysInsideItsTierBand()
    {
        var warnings = MasterBandChecker.Check(Load());

        foreach (string equipmentId in AddedDrops.Values.Select(drop => drop.EquipmentId))
            Assert.DoesNotContain(warnings, warning => warning.Contains(equipmentId));
    }

    [Theory]
    [InlineData("enemy_ember_wisp")]
    [InlineData("enemy_storm_harpy")]
    [InlineData("enemy_amashiro_arc_sentinel")]
    [InlineData("enemy_crypt_oracle")]
    [InlineData("enemy_tempest_siren")]
    [InlineData("enemy_void_dragon")]
    public void AddedEnemiesResolveTheirAttacksThroughTheMagicCombatPath(string enemyId)
    {
        var db = Load();
        var enemy = new EnemyData(db.enemies[enemyId]);
        var target = new AdventurerData(new GuildSimulator.Core.MasterData.AdventurerMasterData
        {
            id = "magic_target",
            baseName = "魔法受け役",
            vitality = 100,
            mental = 8,
            strength = 1,
            agility = 1,
            intelligence = 1,
            constitution = 100,
        })
        {
            CombatHpMax = 100_000,
            CombatHp = 100_000,
        };
        var logs = new List<string>();
        using var random = GuildSimulator.Core.GameRandom.UseSeed(20260825);

        BattleResolver.Resolve(
            new IUnitMember?[] { enemy, null, null, null, null, null },
            new IUnitMember?[] { target, null, null, null, null, null },
            logs,
            turn: 1,
            phase: 1,
            new MoraleState(1_000_000));

        Assert.Contains(logs,
            line => line.Contains($"{enemy.Name}→") && line.Contains("魔法 PV"));
    }

    [Fact]
    public void EveryRankFromEToSHasAtLeastThreeMagicUnits()
    {
        var db = Load();

        foreach (int threat in Enumerable.Range(2, 6))
        {
            var magicUnits = db.enemyUnits.Values
                .Where(unit => unit.Threat == threat)
                .Where(unit => unit.Formation
                    .Where(enemy => enemy != null)
                    .Any(enemy => new EnemyData(enemy!).IsMagicAttack))
                .ToList();

            Assert.True(magicUnits.Count >= 3,
                $"{Rank.Label(threat)}ランクの魔法ユニットが{magicUnits.Count}編成しかありません");
        }
    }

    [Fact]
    public void EveryAddedMagicUnitIsReachableFromADungeon()
    {
        var db = Load();
        var reachable = db.dungeons.Values
            .SelectMany(dungeon => dungeon.encounterTable)
            .Select(entry => entry.unitId)
            .ToHashSet();

        foreach (string unitId in AddedUnitIds)
        {
            Assert.Contains(unitId, reachable);
            var unit = db.enemyUnits[unitId];
            Assert.Contains(unit.Formation,
                enemy => enemy != null && new EnemyData(enemy).IsMagicAttack);
        }
    }
}
