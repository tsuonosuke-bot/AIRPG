using GuildSimulator.Core.GameData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

/// <summary>
/// E帯（threat 2）はチュートリアルを抜けた最初の段なので、帯からはみ出すと
/// 「歯応え」ではなく理不尽になる。F帯と違い、ここは<b>帯を全数検査</b>する。
/// </summary>
public class EEnemyBalanceTests
{
    static GameMasterData Load() => MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void EveryEEnemyStaysInsideTheBand()
    {
        var db = Load();
        var band = RankBandTable.ForThreat(2)!;

        var eEnemies = db.enemies.Values.Where(master => master.threat == 2).ToList();
        Assert.NotEmpty(eEnemies);

        foreach (var master in eEnemies)
        {
            // MasterBandChecker と同じく、装備込みの実際の戦闘値で見る。
            var enemy = new EnemyData(master);
            var stats = enemy.GetBaseCombatStats() + enemy.GetEquipmentBonus();
            int pv = enemy.WeaponBasePv + Math.Min(enemy.AttackStatModifier, enemy.MaxStatBonus);

            Assert.True(band.Hp.Contains(stats.hp), $"{master.id}: HP {stats.hp} が帯 {band.Hp} の外");
            Assert.True(band.Av.Contains(stats.av), $"{master.id}: AV {stats.av} が帯 {band.Av} の外");
            Assert.True(band.Dv.Contains(stats.dv), $"{master.id}: DV {stats.dv} が帯 {band.Dv} の外");
            Assert.True(band.Pv.Contains(pv), $"{master.id}: PV {pv} が帯 {band.Pv} の外");
            Assert.True(band.Exp.Contains(master.exp), $"{master.id}: exp {master.exp} が帯 {band.Exp} の外");
        }
    }

    [Fact]
    public void EGoblinsCarryTheirSignatureGear()
    {
        var db = Load();

        var soldier = new EnemyData(db.enemies["enemy_goblin_soldier"]);
        Assert.Equal(2, soldier.Threat);
        Assert.Equal("eq_sword_01", soldier.Weapon?.id);

        var archer = new EnemyData(db.enemies["enemy_goblin_archer"]);
        Assert.Equal(2, archer.Threat);
        Assert.Equal("eq_bow_01", archer.Weapon?.id);

        var mage = new EnemyData(db.enemies["enemy_goblin_mage"]);
        Assert.Equal(2, mage.Threat);
        Assert.Equal("ゴブリン魔導士", mage.Name);
        Assert.Equal("eq_fire_01", mage.Weapon?.id);
        Assert.True(mage.IsMagicAttack);
    }

    /// <summary>
    /// 牙そのものが魔力を帯びた獣は、装備を持たないまま魔法攻撃で殴る。
    /// 貫通に乗る能力値もSTRではなくINTになる。
    /// </summary>
    [Fact]
    public void ArcaneBeastsBiteWithMagicInsteadOfGear()
    {
        var db = Load();

        var mageWolf = new EnemyData(db.enemies["enemy_mage_wolf"]);
        Assert.Null(mageWolf.Weapon);
        Assert.True(mageWolf.IsMagicAttack);
        Assert.Equal(QudCombat.Modifier(mageWolf.master.intelligence), mageWolf.AttackStatModifier);

        var iceGhoul = new EnemyData(db.enemies["enemy_ice_ghoul"]);
        Assert.Equal(3, iceGhoul.Threat);
        Assert.True(iceGhoul.IsMagicAttack);

        // 素の攻撃種別を書いていない獣は今までどおり物理のまま。
        var lupus = new EnemyData(db.enemies["enemy_forest_wolf"]);
        Assert.False(lupus.IsMagicAttack);
        Assert.Equal(QudCombat.Modifier(lupus.master.strength), lupus.AttackStatModifier);
    }

    [Fact]
    public void VenomousBeastsApplyPoisonAndCollapseIntoOneFang()
    {
        var db = Load();

        var spider = new EnemyData(db.enemies["enemy_poison_spider"]);
        Assert.Contains(spider.Skills, skill =>
            skill.onHitStatuses.Any(status => status.type == CombatStatusType.Poisoned));

        // 毒牙と突然変異の毒牙は同じ系統。両方書いても上位だけが効く。
        Assert.Equal(db.skills["skill_venomFang_lv1"].family, db.skills["skill_venomFang_lv2"].family);

        var poisonFang = new EnemyData(db.enemies["enemy_poison_fang"]);
        Assert.Equal(3, poisonFang.Threat);
        Assert.Contains(poisonFang.Skills, skill => skill.id == "skill_venomFang_lv2");
    }

    /// <summary>コボルトは冒険者側のヴァルグと役割が被るので廃止した。</summary>
    [Fact]
    public void KoboldsAreGoneAndTheMineIsFilledByVermin()
    {
        var db = Load();

        Assert.False(db.enemies.ContainsKey("enemy_kobold"));
        Assert.False(db.enemies.ContainsKey("enemy_kobold_digger"));
        Assert.False(db.enemies.ContainsKey("enemy_kobold_shaman"));

        Assert.Equal(2, db.enemies["enemy_rock_eater"].threat);
        Assert.Equal(2, db.enemies["enemy_cave_sporecap"].threat);
        Assert.Equal(3, db.enemies["enemy_rock_eater_queen"].threat);

        // 廃坑の遭遇表とボスから、消えた敵への参照が残っていないこと。
        var referenced = db.enemyUnits.Values.SelectMany(unit => unit.formationIds)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet();
        Assert.All(referenced, id => Assert.True(db.enemies.ContainsKey(id!), $"未解決の敵 {id}"));
    }
}
