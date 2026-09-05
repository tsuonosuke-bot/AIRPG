using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

/// <summary>
/// 得物と結びついたユニークスキル（アルマーズ／デュランダル／マーニ・カティ／太陽少年）と、
/// その代償である「被会心率」の検証。
///
/// 被会心率は会心域(critRange)と違って<b>守り手側</b>に付く数値で、命中したあとに
/// 別途振る。会心域は1d20の出目そのものを動かすので5%刻みでしか表せず、
/// 2%のような細かい代償を書けないためこの経路を足した。
/// </summary>
public class SignatureWeaponTests
{
    static GameMasterData Load() => MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    static AdventurerData Fighter(string name, int hp = 100_000) =>
        new(new AdventurerMasterData
        {
            id = name, baseName = name,
            vitality = 20, mental = 20, strength = 20,
            agility = 20, intelligence = 20, constitution = 20,
        })
        { CombatHpMax = hp, CombatHp = hp };

    static IUnitMember?[] Side(params IUnitMember?[] members)
    {
        var side = new IUnitMember?[6];
        for (int i = 0; i < members.Length && i < side.Length; i++) side[i] = members[i];
        return side;
    }

    [Theory]
    [InlineData("adv_0035", "skill_unique_armads", "eq_greataxe_01")]    // ヘクトル
    [InlineData("adv_0036", "skill_unique_durandal", "eq_greatsword_01")] // エリウッド
    [InlineData("adv_0037", "skill_unique_maniKatti", "eq_sword_01")]     // リンディス
    [InlineData("adv_0039", "skill_unique_solarBoy", "eq_bow_01")]        // ジャンゴ
    public void EachSignatureSkillIsActiveWithTheWeaponItsOwnerStartsHolding(
        string adventurerId, string skillId, string expectedWeaponId)
    {
        var db = Load();
        var master = db.allAdventurers.Single(a => a.id == adventurerId);

        // 初期装備がスキルの条件を満たしていないと、雇った瞬間は何も起きない。
        Assert.Equal(expectedWeaponId, master.defaultWeaponId);
        Assert.Contains(skillId, master.skillIds);

        var owner = new AdventurerData(master);
        var skill = db.skills[skillId];
        Assert.True(UnitCalculator.MeetsGearRequirements(skill, owner),
            $"{master.baseName} の初期装備では「{skill.skillName}」が働かない");

        // 得物を持ち替えれば眠る。固有スキルであっても構えの条件からは自由にならない。
        owner.SetEquipped(EquipSlot.RightHand, db.equipment["eq_dagger_01"]);
        Assert.False(UnitCalculator.MeetsGearRequirements(skill, owner));

        // 固有スキルなので職業マスタリーとしては誰も解禁できず、持ち主も1人だけ。
        Assert.DoesNotContain(db.classes.Values.SelectMany(c => c.classSkills), e => e.skillId == skillId);
        Assert.Single(db.allAdventurers, a => a.skillIds.Contains(skillId));
    }

    [Fact]
    public void TheTwoLegendaryWeaponsBuyTheirEdgeWithAnOpening()
    {
        var db = Load();
        var armads = db.skills["skill_unique_armads"];
        var durandal = db.skills["skill_unique_durandal"];

        Assert.Equal(2, armads.add.armorPierce);       // 相手のAVを2無視する
        Assert.Equal(2, durandal.add.pv);              // 自身のPVを2上げる
        Assert.True(armads.requireTwoHanded && durandal.requireTwoHanded);

        // 代償。振り切ったあとの隙で、当たった一撃が会心へ跳ね上がりやすくなる。
        Assert.Equal(2, armads.add.incomingCritChancePercent);
        Assert.Equal(2, durandal.add.incomingCritChancePercent);

        // マーニ・カティは代償なしのぶん、増えるのは会心域だけ（1点=5%なので2点で10%）。
        var maniKatti = db.skills["skill_unique_maniKatti"];
        Assert.Equal(2, maniKatti.add.critRange);
        Assert.Equal(0, maniKatti.add.incomingCritChancePercent);
        Assert.True(maniKatti.add.critRange <= QudCombat.MAX_CRIT_RANGE);
    }

    [Fact]
    public void AnOpeningTurnsLandedHitsIntoCriticalsButNeverRescuesAMiss()
    {
        // 必中・必発の極端な値で、経路が通っていることだけを見る。
        var landed = new QudCombat.HitResult(roll: 10, total: 30, hit: true, critical: false);
        var missed = new QudCombat.HitResult(roll: 3, total: 5, hit: false, critical: false);
        var already = new QudCombat.HitResult(roll: 20, total: 40, hit: true, critical: true);

        Assert.True(QudCombat.RaiseToCritical(landed, 100).critical);
        Assert.False(QudCombat.RaiseToCritical(landed, 0).critical);
        // 外れた攻撃が会心になるのは筋が通らない。
        Assert.False(QudCombat.RaiseToCritical(missed, 100).critical);
        Assert.False(QudCombat.RaiseToCritical(missed, 100).hit);
        // すでに会心なら何も変わらない。
        Assert.True(QudCombat.RaiseToCritical(already, 0).critical);
    }

    [Fact]
    public void TheOpeningShowsUpAsCriticalsInAnActualFight()
    {
        var db = Load();
        var reckless = Fighter("隙だらけ");
        reckless.LearnPermanentSkill(new SkillMasterData
        {
            id = "test_opening",
            skillName = "試験の隙",
            add = { incomingCritChancePercent = 100 },
        });
        var attacker = Fighter("案山子役", hp: 100_000);
        attacker.SetEquipped(EquipSlot.RightHand, db.equipment["eq_sword_01"]);

        var logs = new List<string>();
        BattleResolver.Resolve(
            Side(attacker), Side(reckless), logs, turn: 1, phase: 1, new MoraleState(1_000_000));

        Assert.Contains(logs, line => line.Contains("案山子役→隙だらけ") && line.Contains("会心"));
    }
}
