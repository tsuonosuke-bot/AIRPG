using GuildSimulator.Core;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

[Collection("Guild static state")]
public class FEnemyBalanceTests
{
    [Fact]
    public void DamageDiceAndPvMatchTheFEnemyDesign()
    {
        var db = MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

        var slime = new EnemyData(db.enemies["enemy_slime"]);
        Assert.Equal(1, slime.Threat);
        Assert.Equal("1d6", slime.DamageDice);
        Assert.Equal(1, EffectivePv(slime, isFront: true));

        var goblin = new EnemyData(db.enemies["enemy_goblin"]);
        Assert.Equal(1, goblin.Threat);
        Assert.Null(goblin.Weapon);
        Assert.Equal("1d4+1", goblin.DamageDice);
        Assert.Equal(4, goblin.WeaponBasePv);
        Assert.Equal(4, EffectivePv(goblin, isFront: true));

        var thrower = new EnemyData(db.enemies["enemy_goblin_thrower"]);
        Assert.Equal(1, thrower.Threat);
        Assert.Equal("1d2", thrower.DamageDice);
        Assert.Equal(8, EffectivePv(thrower, isFront: false));
        Assert.Equal(6, thrower.master.exp);

        var lupus = new EnemyData(db.enemies["enemy_forest_wolf"]);
        Assert.Equal(1, lupus.Threat);
        Assert.Equal("ルプス", lupus.Name);
        Assert.Equal("1d3", lupus.DamageDice);
        Assert.Equal(4, EffectivePv(lupus, isFront: true));
        Assert.Equal(30, lupus.GetFinalCombatStats().hp);
        Assert.Equal(0, lupus.GetFinalCombatStats().av);
        Assert.Equal(6, lupus.master.exp);

        var malfisa = new EnemyData(db.enemies["enemy_giant_spider"]);
        Assert.Equal(1, malfisa.Threat);
        Assert.Equal("マルフィサ", malfisa.Name);
        Assert.Equal("1d2+1", malfisa.DamageDice);
        Assert.Equal(3, EffectivePv(malfisa, isFront: true));
        Assert.Equal(30, malfisa.GetFinalCombatStats().hp);
        Assert.Equal(1, malfisa.GetFinalCombatStats().av);
        Assert.Equal(6, malfisa.master.exp);
    }

    [Fact]
    public void GrasslandSoloLossRatesStayNearTheRequestedTargets()
    {
        var db = MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var adventurers = db.allAdventurers
            .Where(master => master.defaultRank == 1 && master.defaultLevel == 1)
            .ToList();
        Assert.Equal(29, adventurers.Count);

        var targets = new[]
        {
            (EnemyId: "enemy_slime", Min: 3d, Max: 9d),
            (EnemyId: "enemy_raven", Min: 7d, Max: 16d),
            (EnemyId: "enemy_horned_rabbit", Min: 6d, Max: 14d),
            (EnemyId: "enemy_goblin_stray", Min: 15d, Max: 28d),
            (EnemyId: "enemy_goblin", Min: 34d, Max: 47d),
            (EnemyId: "enemy_goblin_thrower", Min: 34d, Max: 47d),
        };

        const int runsPerMatchup = 200;
        for (int enemyIndex = 0; enemyIndex < targets.Length; enemyIndex++)
        {
            var target = targets[enemyIndex];
            var enemyMaster = db.enemies[target.EnemyId];
            int losses = 0;
            foreach (var (adventurerMaster, adventurerIndex) in adventurers.Select((master, index) => (master, index)))
            {
                for (int run = 0; run < runsPerMatchup; run++)
                {
                    using var random = GameRandom.UseSeed(
                        827_000 + enemyIndex * 1_000_000 + adventurerIndex * runsPerMatchup + run);
                    var adventurer = new AdventurerData(adventurerMaster);
                    var enemy = new EnemyData(enemyMaster);
                    var adventurerSide = Place(adventurer, UsesRearPosition(adventurer));
                    var enemySide = Place(enemy, enemy.Skills.Any(skill => skill.backOnly && !skill.frontOnly));
                    InitializeCombat(adventurerSide, allies: true);
                    InitializeCombat(enemySide, allies: false);

                    var result = BattleResolver.Resolve(
                        adventurerSide,
                        enemySide,
                        new List<string>(),
                        turn: 1,
                        phase: 1,
                        new MoraleState(UnitCalculator.CalcPerMember(adventurerSide, true).Sum(x => x.stats.san)),
                        ExpeditionPolicy.ObjectiveFirst);
                    bool won = !result.adventurersRetreated
                        && adventurer.isAlive
                        && !adventurer.isIncapacitated
                        && !enemy.isAlive;
                    if (!won) losses++;
                }
            }

            double lossRate = losses * 100d / (adventurers.Count * runsPerMatchup);
            Assert.True(lossRate >= target.Min && lossRate <= target.Max,
                $"{enemyMaster.baseName}: Fランク1対1敗北率 {lossRate:F1}% が目標帯 {target.Min:F0}〜{target.Max:F0}% の外です");
        }
    }

    [Fact]
    public void EveryEnemyReceivesTheRaisedGlobalPressure()
    {
        Assert.Equal(1.35f, UnitCalculator.EnemyHpMultiplier);
        Assert.Equal(2, UnitCalculator.EnemyToHitBonus);
        Assert.Equal(1, UnitCalculator.EnemyPvBonus);
    }

    [Fact]
    public void FEnemyPlacementsMatchTheEarlyDungeonProgression()
    {
        var db = MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

        var thrower = db.dungeons["dungeon_meadow"].encounterTable
            .Single(entry => entry.unitId == "unit_goblin_thrower_ambush");
        Assert.Equal(2, thrower.weight);
        Assert.Equal(6, thrower.minPhase);
        Assert.Equal(0, thrower.maxPhase);

        var lupusCull = db.allQuests.Single(quest => quest.id == "quest_wolf_cull");
        Assert.Equal(lupusCull.totalPhases, lupusCull.bossPhase);
        Assert.Equal("unit_wolf_pair", lupusCull.BossEnemy?.id);
    }

    [Fact]
    public void FlowingForestUsesRankedDepthBandsAfterArea16()
    {
        var db = MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

        Assert.DoesNotContain("dungeon_deep_woods", db.dungeons.Keys);
        var forest = db.dungeons["dungeon_woods"];
        Assert.Equal("流れる森", forest.dungeonName);

        Assert.All(
            forest.encounterTable.Where(entry => entry.minPhase < 16),
            entry => Assert.InRange(entry.maxPhase, 1, 15));

        var deepPlacements = new[]
        {
            (UnitId: "unit_wolf_pack", MinPhase: 16, MaxPhase: 0),
            (UnitId: "unit_spider_nest", MinPhase: 16, MaxPhase: 0),
            (UnitId: "unit_ice_ghoul_lone", MinPhase: 18, MaxPhase: 0),
            (UnitId: "unit_treant_grove", MinPhase: 19, MaxPhase: 24),
            (UnitId: "unit_kernos_lone", MinPhase: 25, MaxPhase: 0),
            (UnitId: "unit_kernos_thralls", MinPhase: 27, MaxPhase: 0),
        };
        foreach (var expected in deepPlacements)
        {
            var entry = forest.encounterTable.Single(candidate =>
                candidate.unitId == expected.UnitId && candidate.minPhase == expected.MinPhase);
            Assert.Equal(expected.MaxPhase, entry.maxPhase);
        }

        var scout = db.allQuests.Single(quest => quest.id == "quest_deep_woods_scout");
        Assert.Same(forest, scout.Dungeon);
        Assert.Equal(18, scout.totalPhases);

        var kernos = db.allQuests.Single(quest => quest.id == "quest_kernos_hunt");
        Assert.Same(forest, kernos.Dungeon);
        Assert.Equal(7, kernos.rank);
        Assert.Equal(38, kernos.totalPhases);
        Assert.Equal(38, kernos.bossPhase);
    }

    static int EffectivePv(EnemyData enemy, bool isFront)
    {
        var members = new IUnitMember?[6];
        members[isFront ? 0 : 3] = enemy;
        var stats = UnitCalculator.CalcPerMember(members, isAllySide: false).Single().stats;
        return QudCombat.EffectivePv(
            enemy.WeaponBasePv,
            enemy.AttackStatModifier,
            enemy.MaxStatBonus,
            stats.pv);
    }

    static IUnitMember?[] Place(IUnitMember member, bool isBack)
    {
        var formation = new IUnitMember?[6];
        formation[isBack ? 3 : 0] = member;
        return formation;
    }

    static bool UsesRearPosition(AdventurerData adventurer) =>
        adventurer.Weapon is { } weapon
        && (weapon.IsMagicWeapon || weapon.weaponType == WeaponType.Bow);

    static void InitializeCombat(IUnitMember?[] members, bool allies)
    {
        foreach (var (member, stats) in UnitCalculator.CalcPerMember(members, allies))
        {
            member.CombatHpMax = stats.hp;
            member.CombatHp = stats.hp;
        }
    }
}
