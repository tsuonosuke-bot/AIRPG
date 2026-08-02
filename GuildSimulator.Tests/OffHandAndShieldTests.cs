using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Game.Data;
using Xunit;
using Xunit.Abstractions;

namespace GuildSimulator.Tests;

/// <summary>
/// 二刀流（左手の確率発動）と盾（受けに成功した攻撃にだけ装甲が乗る）の検証。
/// どちらも Caves of Qud の挙動に合わせてある。
/// </summary>
public class OffHandAndShieldTests
{
    readonly ITestOutputHelper output;

    public OffHandAndShieldTests(ITestOutputHelper output) => this.output = output;

    static GameMasterData Load() => MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    static AdventurerData Fighter(GameMasterData db, string weaponId, string? offHandId, int hp = 1_000_000)
    {
        var a = new AdventurerData(new AdventurerMasterData
        {
            id = "adv", baseName = "測定用",
            vitality = 12, mental = 12, strength = 16, agility = 14,
            intelligence = 8, constitution = 12,
        })
        { CombatHpMax = hp, CombatHp = hp };
        a.SetEquipped(EquipSlot.RightHand, db.equipment[weaponId]);
        if (offHandId != null) a.SetEquipped(EquipSlot.LeftHand, db.equipment[offHandId]);
        return a;
    }

    static EnemyData Dummy(int naturalAv, int hp, EquipmentMasterData? shield = null)
    {
        var master = new EnemyMasterData
        {
            id = "dummy", baseName = "案山子",
            vitality = 10, mental = 10, strength = 1, agility = 1,
            intelligence = 1, constitution = 8,
            naturalAv = naturalAv - QudCombat.Modifier(8),
            naturalDamageDice = "1d1-1", naturalPv = 0,
        };
        if (shield != null) { master.DefaultShield = shield; master.defaultShieldId = shield.id; }
        return new EnemyData(master) { CombatHpMax = hp, CombatHp = hp };
    }

    static List<string> Fight(IUnitMember attacker, IUnitMember defender)
    {
        var logs = new List<string>();
        BattleResolver.Resolve(
            new IUnitMember?[] { attacker, null, null, null, null, null },
            new IUnitMember?[] { defender, null, null, null, null, null },
            logs, turn: 1, phase: 1, new MoraleState(1_000_000));
        return logs;
    }

    // ---- 二刀流 ----

    [Fact]
    public void TheOffHandOnlySwingsSometimesAndNotAtAllWithoutAWeaponThere()
    {
        Assert.Equal(15, QudCombat.OFF_HAND_BASE_CHANCE);

        var db = Load();
        var withOffHand = Fight(Fighter(db, "eq_sword_03", "eq_dagger_03"), Dummy(0, 400));
        var withoutOffHand = Fight(Fighter(db, "eq_sword_03", null), Dummy(0, 400));

        Assert.Contains(withOffHand, l => l.Contains("左手"));
        Assert.DoesNotContain(withoutOffHand, l => l.Contains("左手"));
    }

    [Fact]
    public void DaggersAreEasierToSwingInTheOffHandThanHeavierWeapons()
    {
        // 短剣は offHandBonus を持つので、同じスキル無しでも左手が動く頻度が高い。
        var db = Load();
        Assert.True(db.equipment["eq_dagger_03"].offHandBonus > 0);
        Assert.Equal(0, db.equipment["eq_sword_03"].offHandBonus);

        double dagger = OffHandRate(db, "eq_dagger_03");
        double sword = OffHandRate(db, "eq_sword_03");
        output.WriteLine($"左手の発動率: 短剣{dagger:P0} / 剣{sword:P0}");

        Assert.True(dagger > sword, "短剣の二刀流アドバンテージが効いていない");
        // 素の発動率（15%）と、短剣なら +20% で 35% 前後になるはず。
        Assert.InRange(sword, 0.10, 0.20);
        Assert.InRange(dagger, 0.29, 0.41);
    }

    [Fact]
    public void TheDualWieldSkillRaisesTheOffHandRateSubstantially()
    {
        var db = Load();
        var skill = db.skills["skill_dualWield_lv2"];
        Assert.True(skill.add.offHandChance > 0, "二刀流スキルが offHandChance を持っていない");

        double plain = OffHandRate(db, "eq_sword_03");
        double skilled = OffHandRate(db, "eq_sword_03", skill);
        output.WriteLine($"剣の左手発動率: スキル無し{plain:P0} / 二刀流あり{skilled:P0}");

        Assert.True(skilled > plain * 2, "二刀流スキルで発動率が十分に上がっていない");
    }

    /// <summary>手番あたり左手が振られた割合。ログの行数から数える。</summary>
    static double OffHandRate(GameMasterData db, string offHandId, SkillMasterData? skill = null)
    {
        const int battles = 60;
        long swings = 0, offHands = 0;
        for (int i = 0; i < battles; i++)
        {
            var attacker = Fighter(db, "eq_sword_03", offHandId);
            if (skill != null) attacker.LearnPermanentSkill(skill);
            foreach (var line in Fight(attacker, Dummy(0, 400)))
            {
                if (!line.Contains("測定用→")) continue;
                if (line.Contains("左手")) offHands++;
                else swings++;   // 右手の1振り＝1手番（この編成に連撃はない）
            }
        }
        return swings == 0 ? 0 : (double)offHands / swings;
    }

    // ---- 盾 ----

    [Fact]
    public void AShieldsArmourAppliesOnlyOnASuccessfulBlock()
    {
        var db = Load();
        var shield = db.equipment["eq_towershield_02"];
        var logs = Fight(Fighter(db, "eq_sword_03", null), Dummy(2, 100_000, shield));

        var faced = logs
            .Where(l => l.Contains("測定用→") && l.Contains(" vs AV"))
            .Select(l => (
                av: int.Parse(l.Split(" vs AV")[1].Split(new[] { '）', '（' })[0]),
                blocked: l.Contains("で受け")))
            .ToList();
        Assert.NotEmpty(faced);

        // 受けなかった攻撃は素のAVのまま。盾は常時装甲を供給しない。
        var unblocked = faced.Where(f => !f.blocked).Select(f => f.av).Distinct().ToList();
        var blocked = faced.Where(f => f.blocked).Select(f => f.av).Distinct().ToList();
        Assert.NotEmpty(unblocked);
        Assert.NotEmpty(blocked);
        Assert.Single(unblocked);
        Assert.All(blocked, av => Assert.Equal(unblocked[0] + shield.blockAv, av));
    }

    [Fact]
    public void ABiggerShieldBlocksMoreOftenAndHarder()
    {
        var db = Load();
        var small = db.equipment["eq_buckler_02"];
        var large = db.equipment["eq_towershield_02"];

        Assert.True(large.blockChance > small.blockChance, "大盾のほうが受けにくい");
        Assert.True(large.blockAv > small.blockAv, "大盾のほうが装甲が薄い");
        // 代わりに大盾は重く、回避を削る。
        Assert.True(large.weight > small.weight);
        Assert.True(large.bonus.dv < 0 && small.bonus.dv == 0);

        double damageWithout = AverageDamage(db, null);
        double damageSmall = AverageDamage(db, small);
        double damageLarge = AverageDamage(db, large);
        output.WriteLine($"被ダメージ: 盾なし{damageWithout:F1} / 小盾{damageSmall:F1} / 大盾{damageLarge:F1}");

        Assert.True(damageSmall < damageWithout, "小盾が被害を減らしていない");
        Assert.True(damageLarge < damageSmall, "大盾が小盾より守れていない");
    }

    static double AverageDamage(GameMasterData db, EquipmentMasterData? shield)
    {
        const int battles = 40, hp = 100_000;
        long total = 0;
        for (int i = 0; i < battles; i++)
        {
            var dummy = Dummy(2, hp, shield);
            Fight(Fighter(db, "eq_sword_03", null), dummy);
            total += hp - Math.Max(0, dummy.CombatHp);
        }
        return (double)total / battles;
    }

    [Fact]
    public void AnAxeCannotShredTheArmourAShieldProvides()
    {
        // 装甲破壊は「着ている装甲」を削る。構えた盾はその一撃ごとの受けなので剥がせない。
        var db = Load();
        var shield = db.equipment["eq_towershield_02"];
        var logs = Fight(Fighter(db, "eq_axe_03", null), Dummy(8, 100_000, shield));

        var blocked = logs
            .Where(l => l.Contains("測定用→") && l.Contains("で受け"))
            .Select(l => int.Parse(l.Split(" vs AV")[1].Split(new[] { '）', '（' })[0]))
            .ToList();
        var plain = logs
            .Where(l => l.Contains("測定用→") && l.Contains(" vs AV") && !l.Contains("で受け"))
            .Select(l => int.Parse(l.Split(" vs AV")[1].Split(new[] { '）', '（' })[0]))
            .ToList();
        Assert.NotEmpty(blocked);
        Assert.NotEmpty(plain);

        // 素の装甲は削られて下がるが、受けたときは常にその時点の素AV＋盾ぶんになる。
        Assert.True(plain.First() > plain.Last(), "斧が素の装甲を削っていない");
        Assert.Equal(plain.Min() + shield.blockAv, blocked.Min());
    }

    [Fact]
    public void MagicIgnoresShieldsBecauseItIsMeasuredAgainstMagicArmour()
    {
        // 盾は物理AVの話。魔法は mAV と突き合わせるので受けは介在しない。
        var db = Load();
        var logs = Fight(Fighter(db, "eq_fire_03", null), Dummy(2, 100_000, db.equipment["eq_towershield_02"]));
        Assert.Contains(logs, l => l.Contains("魔法 PV"));
        Assert.DoesNotContain(logs, l => l.Contains("で受け"));
    }
}
