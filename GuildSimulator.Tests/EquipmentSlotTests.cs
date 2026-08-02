using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

/// <summary>
/// 右手・左手のルール。両手武器は左手を塞ぎ、盾は左手にしか構えられず、
/// 左手に握った武器は攻撃にだけ使われて数値ボーナスは供給しない。
/// </summary>
[Collection("Guild static state")]
public class EquipmentSlotTests
{
    static GameMasterData Load() => MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    static AdventurerData Fighter() => new(new AdventurerMasterData
    {
        id = "adv", baseName = "測定用",
        vitality = 14, mental = 10, strength = 16, agility = 12,
        intelligence = 8, constitution = 14,
    });

    static GuildManager GuildWith(params EquipmentMasterData[] items)
    {
        var guild = new GuildManager(startGold: 1000);
        foreach (var item in items) guild.AddEquipment(item, 1);
        return guild;
    }

    [Fact]
    public void EquippingATwoHandedWeaponClearsTheOffHand()
    {
        var db = Load();
        var dagger = db.equipment["eq_dagger_01"];
        var greatsword = db.equipment["eq_greatsword_01"];
        var adv = Fighter();
        var guild = GuildWith(dagger, greatsword);

        Assert.True(EquipService.TryEquip(adv, dagger, EquipSlot.LeftHand, guild, out _));
        Assert.NotNull(adv.OffHandWeapon);

        // 両手武器へ持ち替えると左手は自動的に空き、外した短剣は倉庫へ戻る。
        Assert.True(EquipService.TryEquip(adv, greatsword, EquipSlot.RightHand, guild, out _));
        Assert.Null(adv.GetEquipped(EquipSlot.LeftHand));
        Assert.True(guild.Has(dagger));
    }

    [Fact]
    public void NothingCanBeEquippedInTheOffHandWhileHoldingATwoHandedWeapon()
    {
        var db = Load();
        var greatsword = db.equipment["eq_greatsword_01"];
        var shield = db.equipment["eq_buckler_01"];
        var adv = Fighter();
        var guild = GuildWith(greatsword, shield);

        Assert.True(EquipService.TryEquip(adv, greatsword, EquipSlot.RightHand, guild, out _));
        Assert.False(EquipService.TryEquip(adv, shield, EquipSlot.LeftHand, guild, out var reason));
        Assert.Contains("両手", reason);
        Assert.Null(adv.Shield);
    }

    [Fact]
    public void ShieldsGoInTheOffHandOnly()
    {
        var db = Load();
        var shield = db.equipment["eq_buckler_01"];
        var adv = Fighter();
        var guild = GuildWith(shield);

        Assert.False(EquipService.TryEquip(adv, shield, EquipSlot.RightHand, guild, out _));
        Assert.True(EquipService.TryEquip(adv, shield, EquipSlot.LeftHand, guild, out _));
        Assert.NotNull(adv.Shield);
        // 盾は武器ではないので、二刀流の対象にはならない。
        Assert.Null(adv.OffHandWeapon);
    }

    [Fact]
    public void AWeaponInTheOffHandGivesNoStatBonus()
    {
        // 短剣は命中+3。2本持っても+6にはならない（左手は攻撃にしか使わない）。
        var db = Load();
        var dagger = db.equipment["eq_dagger_01"];
        var adv = Fighter();
        var guild = GuildWith(dagger, dagger);

        EquipService.TryEquip(adv, dagger, EquipSlot.RightHand, guild, out _);
        int oneHanded = adv.GetEquipmentBonus().toHit;

        EquipService.TryEquip(adv, dagger, EquipSlot.LeftHand, guild, out _);
        Assert.Equal(oneHanded, adv.GetEquipmentBonus().toHit);

        // 重さだけは両方ぶんかかる。積載を圧迫する形で二刀流の代償が残る。
        Assert.Equal(dagger.weight * 2, adv.AllEquippedItems().Sum(e => e.weight));
    }

    [Fact]
    public void AShieldInTheOffHandGivesItsBonusButNotItsArmour()
    {
        // 大盾はDVを下げる（常時）が、装甲は受けに成功したときだけ乗るので素のAVには現れない。
        var db = Load();
        var shield = db.equipment["eq_towershield_01"];
        var adv = Fighter();
        var guild = GuildWith(shield);

        int avBefore = adv.GetEquipmentBonus().av;
        int dvBefore = adv.GetEquipmentBonus().dv;

        EquipService.TryEquip(adv, shield, EquipSlot.LeftHand, guild, out _);

        Assert.Equal(avBefore, adv.GetEquipmentBonus().av);
        Assert.True(adv.GetEquipmentBonus().dv < dvBefore, "大盾のDVペナルティが乗っていない");
        Assert.True(shield.blockAv > 0, "受け成功時の装甲が設定されていない");
    }

    [Fact]
    public void BowsAndMagicAreTwoHandedSoTheyCannotUseShieldsOrDualWield()
    {
        var db = Load();
        foreach (var weapon in db.equipment.Values.Where(e => e.type == EquipmentType.Weapon))
        {
            bool ranged = weapon.weaponType == WeaponType.Bow;
            bool magic = weapon.IsMagicWeapon || weapon.attackKind == AttackKind.Heal;
            if (ranged || magic)
                Assert.True(weapon.isTwoHanded, $"{weapon.id} が片手武器のままです");
        }

        var wand = db.equipment["eq_fire_01"];
        var shield = db.equipment["eq_buckler_01"];
        var adv = Fighter();
        var guild = GuildWith(wand, shield);

        EquipService.TryEquip(adv, wand, EquipSlot.RightHand, guild, out _);
        Assert.False(EquipService.TryEquip(adv, shield, EquipSlot.LeftHand, guild, out _));
    }

    [Fact]
    public void TwoHandedWeaponsKeepTheirWeaponTypeSoMasteriesStillApply()
    {
        // 大剣は Sword のままなので「剣マスタリー」が効く。職業側に手を入れずに済ませるための取り決め。
        var db = Load();
        Assert.Equal(WeaponType.Sword, db.equipment["eq_greatsword_03"].weaponType);
        Assert.Equal(WeaponType.Axe, db.equipment["eq_greataxe_03"].weaponType);
        Assert.Equal(WeaponType.Spear, db.equipment["eq_longspear_03"].weaponType);

        var swordMastery = db.skills["skill_swordMastery_lv2"];
        Assert.True(swordMastery.requireWeaponType);
        Assert.Equal(WeaponType.Sword, swordMastery.requiredWeaponType);

        // 両手版は片手版より能力値上限が高い。膂力を乗せきれる、が両手武器の取り柄。
        Assert.True(db.equipment["eq_greatsword_03"].maxStatBonus
            > db.equipment["eq_sword_03"].maxStatBonus);
    }

    /// <summary>過積載ぶんだけ削られた回避値。0なら積載に余裕がある。</summary>
    static int OverweightDvLoss(AdventurerData a) =>
        a.GetBaseCombatStats().dv + a.GetEquipmentBonus().dv - a.GetFinalCombatStats().dv;

    [Fact]
    public void ShieldsAndTwoHandedWeaponsMakeCarryCapacityMatter()
    {
        // 盾と両手武器が入ったことで、積載上限（SIZ + (STR+VIT)/2）が実際の制約になる。
        var db = Load();
        var weakling = new AdventurerData(new AdventurerMasterData
        {
            id = "weak", baseName = "非力",
            vitality = 8, mental = 10, strength = 8, agility = 12,
            intelligence = 8, constitution = 7,
        });
        var strongman = Fighter();

        // 重鎧＋兜＋両手斧は、Lv1では力自慢でも背負いきれない贅沢な組み合わせ。
        foreach (var adv in new[] { weakling, strongman })
        {
            adv.SetEquipped(EquipSlot.Body, db.equipment["eq_plate_02"]);
            adv.SetEquipped(EquipSlot.Head, db.equipment["eq_helm_01"]);
            adv.SetEquipped(EquipSlot.RightHand, db.equipment["eq_greataxe_03"]);
        }
        Assert.True(OverweightDvLoss(weakling) > 0, "非力な冒険者が重装で過積載になっていない");
        Assert.True(OverweightDvLoss(strongman) < OverweightDvLoss(weakling),
            "筋力・体格が積載の余裕に効いていない");

        // 軽い鎧と片手剣＋小盾なら、力自慢は過積載にならない。
        strongman.SetEquipped(EquipSlot.Body, db.equipment["eq_leather_01"]);
        strongman.SetEquipped(EquipSlot.Head, null);
        strongman.SetEquipped(EquipSlot.RightHand, db.equipment["eq_sword_03"]);
        strongman.SetEquipped(EquipSlot.LeftHand, db.equipment["eq_buckler_01"]);
        Assert.Equal(0, OverweightDvLoss(strongman));
    }
}
