using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

/// <summary>
/// レア冒険者ミカヤと、彼女のユニークスキル「銀色の髪の乙女」の回帰テスト。
/// 数値そのもの（回復1.1倍・回復時の士気+2%）と、E帯の顔ぶれの中での立ち位置を固定する。
/// </summary>
public class MikayaHealerTests
{
    const string MikayaId = "adv_0030";
    const string SkillId = "skill_unique_silverMaiden";

    static GameMasterData Load() => MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    static AdventurerMasterData Mikaya(GameMasterData db) =>
        db.allAdventurers.Single(a => a.id == MikayaId);

    /// <summary>
    /// 素質＝Lv1換算の7能力合計。体格と容姿はレベルで伸びないので素質の一部として
    /// 最初から配られており、5能力だけで比べると体格に振った冒険者が不当に低く見える。
    /// </summary>
    static int Talent(AdventurerMasterData a) =>
        a.vitality + a.mental + a.strength + a.agility + a.intelligence
        + a.constitution + a.appearance
        - (a.defaultLevel - 1) * AdventurerData.StatPointsPerLevel;

    static IUnitMember?[] Side(params IUnitMember?[] members)
    {
        var side = new IUnitMember?[6];
        for (int i = 0; i < members.Length && i < side.Length; i++) side[i] = members[i];
        return side;
    }

    static AdventurerData Plain(string name, int maxHp = 1_000, int? currentHp = null) =>
        new(new AdventurerMasterData
        {
            id = name,
            baseName = name,
            vitality = 20,
            mental = 20,
            strength = 20,
            agility = 20,
            intelligence = 20,
            constitution = 20,
        })
        {
            CombatHpMax = maxHp,
            CombatHp = currentHp ?? maxHp,
        };

    /// <summary>殴っても痛くない案山子。士気が損害で減らないので、回復ぶんだけを測れる。</summary>
    static EnemyData Harmless(int maxHp, int? currentHp = null, EquipmentMasterData? weapon = null,
        SkillMasterData? skill = null)
    {
        var master = new EnemyMasterData
        {
            id = "案山子",
            baseName = "案山子",
            vitality = 10,
            mental = 10,
            strength = 1,
            agility = 1,
            intelligence = 1,
            constitution = 8,
            naturalPv = 0,
            naturalDamageDice = "1d1-1",
            DefaultWeapon = weapon,
        };
        if (skill != null) master.Skills.Add(skill);
        return new EnemyData(master) { CombatHpMax = maxHp, CombatHp = currentHp ?? maxHp };
    }

    [Fact]
    public void MikayaIsARareERankHealerCarryingHerOwnSkill()
    {
        var db = Load();
        var mikaya = Mikaya(db);

        Assert.Equal("ミカヤ", mikaya.baseName);
        Assert.Equal(Rarity.Rare, mikaya.rarity);
        Assert.Equal(2, mikaya.defaultRank);   // E
        Assert.Equal(2, mikaya.recruitGuildRank);
        Assert.InRange(mikaya.defaultLevel, 6, 10); // E帯の冒険者レベル
        Assert.Equal(Gender.Female, mikaya.gender);
        Assert.Equal("class_Healer", mikaya.defaultClassId);
        Assert.NotNull(mikaya.DefaultClass);
        Assert.NotNull(mikaya.Race);
        Assert.Contains(mikaya.defaultClassId, mikaya.Race!.allowedClassIds);
        Assert.True(mikaya.DefaultWeapon?.IsHealWeapon, "回復役なので光魔法の杖を持って現れる");

        // ユニークスキルは名簿から直に持ち込む（職業マスタリーの解禁ではない）。
        Assert.Contains(SkillId, mikaya.skillIds);
        Assert.Contains(mikaya.Skills, s => s.id == SkillId);
        Assert.DoesNotContain(db.classes.Values.SelectMany(c => c.classSkills), e => e.skillId == SkillId);
        Assert.DoesNotContain(db.allAdventurers.Where(a => a.id != MikayaId), a => a.skillIds.Contains(SkillId));
    }

    [Fact]
    public void MikayaCarriesTheRarityPremiumOnTopOfHerLevels()
    {
        var db = Load();
        var mikaya = Mikaya(db);

        // 強さは「素質＋レベル」の2階建て。レアリティは素質の側にだけ乗る。
        Assert.Equal(70, Talent(mikaya));
        Assert.Equal(
            Talent(mikaya) + (mikaya.defaultLevel - 1),
            mikaya.vitality + mikaya.mental + mikaya.strength + mikaya.agility
            + mikaya.intelligence + mikaya.constitution + mikaya.appearance);

        // 帯より1段上のレアリティは素質+5。比べる相手は帯の標準レアリティ
        // （E帯ならUncommon）で、同じ枠のレア同士を比べても差は出ない。
        int bandStandard = db.allAdventurers
            .Where(a => a.defaultRank == mikaya.defaultRank && a.rarity == Rarity.Uncommon)
            .Max(Talent);
        Assert.InRange(Talent(mikaya) - bandStandard, 4, 5);

        // 上乗せは1段だけ。70が名簿全体の素質上限で、レアでもここより上には行かない。
        Assert.Equal(70, db.allAdventurers.Max(Talent));

        // 小柄な体格。SIZは成長しないので、雇った瞬間の値がそのまま彼女の体つきになる。
        Assert.True(
            mikaya.constitution <= db.allAdventurers.Where(a => a.id != MikayaId).Min(a => a.constitution),
            "小柄な女性なので体格は名簿でいちばん小さい側に置く");
    }

    [Fact]
    public void SilverMaidenRaisesHerHealingByOneTenth()
    {
        var db = Load();
        var staff = db.equipment["eq_Light_01"];

        var plain = Plain("神官");
        plain.SetEquipped(EquipSlot.RightHand, staff);
        int before = UnitCalculator.CalcPerMember(Side(plain), isAllySide: true)[0].stats.heal;

        var maiden = Plain("ミカヤ");
        maiden.SetEquipped(EquipSlot.RightHand, staff);
        maiden.LearnPermanentSkill(db.skills[SkillId]);
        int after = UnitCalculator.CalcPerMember(Side(maiden), isAllySide: true)[0].stats.heal;

        Assert.Equal((int)Math.Floor(before * 1.1f), after);
        Assert.True(after > before, "回復量は1.1倍になる");
    }

    [Fact]
    public void SilverMaidenGivesBackALittleMoraleOnEveryHeal()
    {
        var db = Load();
        // 手当て1回で回復目標(HP70%)を越えてしまうと機会が1〜2回しかなく、
        // 手元が狂う出目1（5%）が続いただけでテストが落ちる。深く削っておいて
        // 何度も手当てが起きる形にし、1回ぶんの成功に賭けないようにする。
        var patient = Plain("患者", 10_000, 2_000);
        var maiden = Plain("ミカヤ");
        maiden.SetEquipped(EquipSlot.RightHand, db.equipment["eq_Light_01"]);
        maiden.LearnPermanentSkill(db.skills[SkillId]);

        var morale = new MoraleState(1_000, 500);
        var logs = new List<string>();
        BattleResolver.Resolve(
            Side(patient, maiden), Side(Harmless(1_000_000)), logs, turn: 1, phase: 1, morale);

        Assert.Contains(logs, line => line.Contains("ミカヤ→患者") && line.Contains("回復"));
        Assert.Contains(logs, line => line.Contains("銀色の髪の乙女で士気 +"));

        // 微量。1回の手当てで戻るのは最大値の2%＝20で、削られた500を埋め尽くしはしない。
        Assert.InRange(morale.Current, 520, 1_000);
    }

    [Fact]
    public void EnemyHealersDoNotLiftThePartyMorale()
    {
        var db = Load();
        var hero = Plain("冒険者");
        var wounded = Harmless(100_000, 20_000);
        var enemyMaiden = Harmless(
            1_000_000, weapon: db.equipment["eq_Light_01"], skill: db.skills[SkillId]);

        var morale = new MoraleState(1_000, 500);
        var logs = new List<string>();
        BattleResolver.Resolve(
            Side(hero), Side(wounded, enemyMaiden), logs, turn: 1, phase: 1, morale);

        Assert.Contains(logs, line => line.Contains("案山子→案山子") && line.Contains("回復"));
        Assert.DoesNotContain(logs, line => line.Contains("銀色の髪の乙女で士気 +"));

        // 士気はパーティ側の指標。敵が何度癒し合っても、こちらの粘りは1点も戻らない。
        Assert.True(morale.Current <= 500, $"敵の回復で士気が戻ってしまった（{morale.Current}）");
    }
}
