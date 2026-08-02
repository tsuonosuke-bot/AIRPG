using GuildSimulator.Core.GameData;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

public class FEnemyBalanceTests
{
    [Fact]
    public void DamageDiceAndPvMatchTheFEnemyDesign()
    {
        var db = MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

        var slime = new EnemyData(db.enemies["enemy_slime"]);
        Assert.Equal(1, slime.Threat);
        Assert.Equal("1d2", slime.DamageDice);
        Assert.Equal(0, EffectivePv(slime, isFront: true));

        var goblin = new EnemyData(db.enemies["enemy_goblin"]);
        Assert.Equal(1, goblin.Threat);
        Assert.Null(goblin.Weapon);
        Assert.Equal("1d2", goblin.DamageDice);
        Assert.Equal(AdventurerData.UNARMED_PV, goblin.WeaponBasePv);
        Assert.Equal(0, EffectivePv(goblin, isFront: true));

        var thrower = new EnemyData(db.enemies["enemy_goblin_thrower"]);
        Assert.Equal(1, thrower.Threat);
        Assert.Equal("1d2", thrower.DamageDice);
        Assert.Equal(2, EffectivePv(thrower, isFront: false));
        Assert.Equal(6, thrower.master.exp);

        var lupus = new EnemyData(db.enemies["enemy_forest_wolf"]);
        Assert.Equal(1, lupus.Threat);
        Assert.Equal("ルプス", lupus.Name);
        Assert.Equal("1d3", lupus.DamageDice);
        Assert.Equal(3, EffectivePv(lupus, isFront: true));
        Assert.Equal(30, lupus.GetFinalCombatStats().hp);
        Assert.Equal(0, lupus.GetFinalCombatStats().av);
        Assert.Equal(6, lupus.master.exp);

        var malfisa = new EnemyData(db.enemies["enemy_giant_spider"]);
        Assert.Equal(1, malfisa.Threat);
        Assert.Equal("マルフィサ", malfisa.Name);
        Assert.Equal("1d2+1", malfisa.DamageDice);
        Assert.Equal(2, EffectivePv(malfisa, isFront: true));
        Assert.Equal(30, malfisa.GetFinalCombatStats().hp);
        Assert.Equal(1, malfisa.GetFinalCombatStats().av);
        Assert.Equal(6, malfisa.master.exp);
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
}
