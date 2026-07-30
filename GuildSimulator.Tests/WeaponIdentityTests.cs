using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Cli;
using GuildSimulator.Game.Data;
using GuildSimulator.Game.Presentation;
using GuildSimulator.Game.Screens;
using Xunit;
using Xunit.Abstractions;

namespace GuildSimulator.Tests;

/// <summary>
/// 武器クラスの個性とパワーバランスの検証。
///
///   剣   : 尖った長所も短所もない基準点。どのAV帯でも中庸
///   短剣 : 命中が高く会心域が広く連撃する。ただし1振りが軽く、硬い相手には通らない
///   槍   : 相手の装甲を無視して突く。硬い相手ほど強く、素肌の相手には旨みがない
///   斧   : 当てるたびに相手の装甲そのものを削る。当たりにくいが、粘るほど効く
///
/// 「個性が立っている」と「4種の総合力が揃っている」を同時に測る。
/// 出力を見るには: dotnet test --filter WeaponIdentity --logger "console;verbosity=detailed"
/// </summary>
[Collection("Console presentation")]
public class WeaponIdentityTests
{
    readonly ITestOutputHelper output;

    public WeaponIdentityTests(ITestOutputHelper output) => this.output = output;

    const int Battles = 600;

    static readonly string[] SwordLine = { "eq_sword_01", "eq_sword_02", "eq_sword_03" };
    static readonly string[] DaggerLine = { "eq_dagger_01", "eq_dagger_02", "eq_dagger_03" };
    static readonly string[] SpearLine = { "eq_spear_01", "eq_spear_02", "eq_spear_03" };
    static readonly string[] AxeLine = { "eq_axe_01", "eq_axe_02", "eq_axe_03" };

    static GameMasterData Load() => MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void TheFourMeleeClassesLandInTheSamePowerBandAtEveryTier()
    {
        var db = Load();
        // enemies.json の実データに合わせた想定。序盤の敵はHP20〜40・AV0前後、
        // 終盤は老樹人や石のゴーレムのようにHP90〜115・AV7〜12まで上がる。
        // 装甲破壊のような「積み上がる」効果は戦闘の長さで効きが変わるので、
        // 測定も実際の戦闘と同じ長さで行う。
        var stages = new[]
        {
            (tier: 0, str: 12, agi: 8, hp: 30, avs: new[] { 0, 1, 3 }),
            (tier: 1, str: 15, agi: 10, hp: 60, avs: new[] { 0, 2, 5 }),
            (tier: 2, str: 19, agi: 12, hp: 110, avs: new[] { 0, 4, 8, 12 }),
        };

        foreach (var (tier, str, agi, hp, avs) in stages)
        {
            output.WriteLine($"--- Tier{tier + 1}（STR{str} AGI{agi} 敵HP{hp}）撃破までの攻撃回数（少ないほど強い） ---");
            output.WriteLine($"{"武器",-8}" + string.Join("", avs.Select(a => $"AV{a,-8}")) + "  総合力");

            double swordPower = 0;
            foreach (var (name, line) in new[]
                     { ("剣", SwordLine), ("短剣", DaggerLine), ("槍", SpearLine), ("斧", AxeLine) })
            {
                var weapon = db.equipment[line[tier]];
                var rounds = avs.Select(av => RoundsToKill(db, weapon, str, agi, av, hp)).ToArray();
                // 「1回の攻撃で削れる割合」の平均を総合力とする。回数そのものの平均だと、
                // 相性の悪いAV帯だけで順位が決まってしまう。
                double power = rounds.Average(r => 1.0 / r);
                if (name == "剣") swordPower = power;

                output.WriteLine($"{name,-10}" + string.Join("", rounds.Select(r => $"{r,-10:F1}"))
                    + $"  {power / swordPower * 100,5:F0}%");

                Assert.InRange(power / swordPower, 0.85, 1.20);
            }
        }
    }

    [Fact]
    public void EachClassIsTheBestPickInTheSituationItWasDesignedFor()
    {
        var db = Load();
        const int str = 19, agi = 12, hp = 110;

        double Power(string id, int av) => 1.0 / RoundsToKill(db, db.equipment[id], str, agi, av, hp);

        // 「素肌の相手での強さ」に対する「硬い相手での強さ」の比。値が大きいほど装甲に強い得物。
        // 絶対値ではなく比で見るのは、総合力が揃っている前提で相性の向きだけを問いたいため。
        double Slope(string id) => Power(id, 12) / Power(id, 0);

        double sword = Slope("eq_sword_03");
        double dagger = Slope("eq_dagger_03");
        double spear = Slope("eq_spear_03");
        double axe = Slope("eq_axe_03");
        output.WriteLine($"装甲への強さ（AV12/AV0）: 剣{sword:F3} 短剣{dagger:F3} 槍{spear:F3} 斧{axe:F3}");

        // 短剣は1振りが軽い。硬い相手ほど剣に見劣りしていく。
        Assert.True(dagger < sword, "短剣が硬い相手に強すぎる（低威力の短所が出ていない）");

        // 槍は装甲を無視し、斧は装甲そのものを削る。どちらも硬い相手ほど相対的に強くなる。
        Assert.True(spear > sword, "槍の装甲貫通が硬い相手で効いていない");
        Assert.True(axe > sword, "斧の装甲破壊が硬い相手で効いていない");

        // 素肌の相手では逆転する。当てにくく通しにくい重量級より、手数の出る軽い得物が働く。
        double lightSoft = Power("eq_dagger_03", 0);
        Assert.True(Power("eq_spear_03", 0) < lightSoft, "素肌の相手で槍が短剣を上回っている");
        Assert.True(Power("eq_axe_03", 0) < lightSoft, "素肌の相手で斧が短剣を上回っている");
    }

    [Fact]
    public void TheArmourAnAxeStripsStaysStrippedForEveryoneElse()
    {
        // 槍の装甲貫通はその一撃かぎりだが、斧が削った装甲は戦闘が終わるまで戻らない。
        // 「斧使いが前を削り、仲間がそこへ叩き込む」というパーティ単位の噛み合いを確かめる。
        var db = Load();
        var logs = new List<string>();
        var axeman = Fighter(db, db.equipment["eq_axe_03"], strength: 19, agility: 12, hp: 100_000);
        axeman.name = "斧使い";
        var ally = Fighter(db, db.equipment["eq_sword_03"], strength: 19, agility: 12, hp: 100_000);
        ally.name = "仲間";

        BattleResolver.Resolve(
            new IUnitMember?[] { axeman, ally, null, null, null, null },
            new IUnitMember?[] { ArmouredDummy(naturalAv: 12, hp: 100_000), null, null, null, null, null },
            logs, turn: 1, phase: 1, new MoraleState(100_000));

        // 剣を持った仲間が向き合ったAVが、戦闘の途中で下がっている。
        var allyFacedAv = logs
            .Where(l => l.Contains("仲間→") && l.Contains(" vs AV"))
            .Select(l => int.Parse(l.Split(" vs AV")[1].Split(new[] { '）', '（' })[0]))
            .ToList();
        Assert.NotEmpty(allyFacedAv);
        Assert.True(allyFacedAv.First() > allyFacedAv.Last(),
            $"斧が削った装甲が仲間の攻撃に反映されていない（AV{allyFacedAv.First()}→{allyFacedAv.Last()}）");
        Assert.Equal(allyFacedAv.First() - QudCombat.MAX_ARMOR_SHRED, allyFacedAv.Last());
    }

    [Fact]
    public void AWiderCriticalRangeMakesLowRollsCriticalButNeverTheFumble()
    {
        Assert.Equal(20, QudCombat.CriticalThreshold(0));
        Assert.Equal(18, QudCombat.CriticalThreshold(2));
        // 広げすぎても頭打ちになる。会心が必中と同義になってしまうため。
        Assert.Equal(20 - QudCombat.MAX_CRIT_RANGE, QudCombat.CriticalThreshold(99));

        int criticals = 0;
        const int trials = 20_000;
        for (int i = 0; i < trials; i++)
        {
            // DVを吊り上げて、会心以外では当たらない状況を作る。
            var r = QudCombat.RollToHit(toHitBonus: 0, dv: 1000, critRange: 2);
            Assert.Equal(r.critical, r.hit);
            Assert.Equal(r.roll >= 18, r.critical);
            if (r.critical) criticals++;
        }
        // 18〜20の3面ぶんなので15%前後。
        Assert.InRange((double)criticals / trials, 0.12, 0.18);

        // 出目1は補正でも会心域でも救われない。
        for (int i = 0; i < 5_000; i++)
        {
            var r = QudCombat.RollToHit(toHitBonus: 1000, dv: 0, critRange: QudCombat.MAX_CRIT_RANGE);
            if (r.roll == QudCombat.FUMBLE_ROLL) Assert.False(r.hit || r.critical);
        }
    }

    [Fact]
    public void ArmorPierceIgnoresArmourInsteadOfAddingPenetrationValue()
    {
        // 装甲貫通は相手のAVを差し引く。PVを上げるのと違い、素肌の相手には何も起きない。
        var pierced = Average(() => QudCombat.ResolveAttack(6, av: 10, "1d6", false, armorPierce: 4));
        var plain = Average(() => QudCombat.ResolveAttack(6, av: 10, "1d6", false));
        var equivalent = Average(() => QudCombat.ResolveAttack(6, av: 6, "1d6", false));
        Assert.True(pierced > plain, "装甲貫通が硬い相手に効いていない");
        Assert.InRange(pierced, equivalent * 0.85, equivalent * 1.15);

        var softPierced = Average(() => QudCombat.ResolveAttack(6, av: 0, "1d6", false, armorPierce: 4));
        var softPlain = Average(() => QudCombat.ResolveAttack(6, av: 0, "1d6", false));
        Assert.InRange(softPierced, softPlain * 0.9, softPlain * 1.1);

        // AVを下回るところまでしか削れない（マイナスのAVでボーナスは出ない）。
        var overPierced = Average(() => QudCombat.ResolveAttack(6, av: 2, "1d6", false, armorPierce: 50));
        var zeroAv = Average(() => QudCombat.ResolveAttack(6, av: 0, "1d6", false));
        Assert.InRange(overPierced, zeroAv * 0.9, zeroAv * 1.1);
    }

    [Fact]
    public void FollowUpSwingsHitSofterThanTheFirstOne()
    {
        // 追撃はPVが下がるので、手数がそのまま火力の掛け算にはならない。
        Assert.Equal(10, QudCombat.FollowUpPv(10, 0));
        Assert.Equal(10 - QudCombat.FOLLOW_UP_PV_PENALTY, QudCombat.FollowUpPv(10, 1));
        Assert.Equal(10 - QudCombat.FOLLOW_UP_PV_PENALTY * 2, QudCombat.FollowUpPv(10, 2));

        var first = Average(() => QudCombat.ResolveAttack(QudCombat.FollowUpPv(8, 0), 6, "1d6", false));
        var second = Average(() => QudCombat.ResolveAttack(QudCombat.FollowUpPv(8, 1), 6, "1d6", false));
        Assert.True(second < first, "追撃が本命と同じ重さで入っている");
    }

    [Fact]
    public void AnAxeStripsArmourForTheWholePartyButOnlyUpToTheCap()
    {
        var db = Load();
        var logs = new List<string>();
        // 斧使いと素手の仲間を並べ、装甲を固めた相手を殴る。削れた装甲は仲間の攻撃にも効く。
        var axeman = Fighter(db, db.equipment["eq_axe_03"], strength: 19, agility: 12, hp: 100_000);
        var dummy = ArmouredDummy(naturalAv: 12, hp: 100_000);

        BattleResolver.Resolve(
            new IUnitMember?[] { axeman, null, null, null, null, null },
            new IUnitMember?[] { dummy, null, null, null, null, null },
            logs, turn: 1, phase: 1, new MoraleState(100_000));

        var shredLogs = logs.Where(l => l.Contains("装甲が砕けた")).ToList();
        Assert.NotEmpty(shredLogs);

        // 累計は上限で止まる。長引いた戦闘で装甲が無意味になることはない。
        int highest = shredLogs
            .Select(l => int.Parse(l.Split("累計-")[1].TrimEnd('）')))
            .Max();
        Assert.Equal(QudCombat.MAX_ARMOR_SHRED, highest);
        Assert.DoesNotContain(logs, l => l.Contains($"累計-{QudCombat.MAX_ARMOR_SHRED + 1}"));
    }

    [Fact]
    public void ADaggerSwingsTwiceInASingleAction()
    {
        var db = Load();
        var logs = new List<string>();
        var rogue = Fighter(db, db.equipment["eq_dagger_03"], strength: 14, agility: 20, hp: 100_000);
        var dummy = ArmouredDummy(naturalAv: 0, hp: 100_000);

        BattleResolver.Resolve(
            new IUnitMember?[] { rogue, null, null, null, null, null },
            new IUnitMember?[] { dummy, null, null, null, null, null },
            logs, turn: 1, phase: 1, new MoraleState(100_000));

        Assert.Contains(logs, l => l.Contains(rogue.Name) && l.Contains("追撃"));

        // 剣に持ち替えれば追撃は起きない。連撃は短剣というクラスの性質であって、使い手の性質ではない。
        var swordsman = Fighter(db, db.equipment["eq_sword_03"], strength: 14, agility: 20, hp: 100_000);
        var swordLogs = new List<string>();
        BattleResolver.Resolve(
            new IUnitMember?[] { swordsman, null, null, null, null, null },
            new IUnitMember?[] { ArmouredDummy(naturalAv: 0, hp: 100_000), null, null, null, null, null },
            swordLogs, turn: 1, phase: 1, new MoraleState(100_000));
        Assert.DoesNotContain(swordLogs, l => l.Contains(swordsman.Name) && l.Contains("追撃"));
    }

    [Fact]
    public void EveryWeaponOfTheSameClassSharesTheSameIdentity()
    {
        // マスタデータ側の取り決め。Tier差は basePv とダメージダイスだけで表し、個性は武器種で固定する。
        var db = Load();
        var errors = MasterValidator.Validate(db);
        Assert.DoesNotContain(errors, e => e.Contains("武器クラスの個性") || e.Contains("maxStatBonus"));

        Assert.Equal(new WeaponTraits(0, 0, 2, 1), db.equipment["eq_dagger_01"].Traits);
        Assert.Equal(new WeaponTraits(2, 0, 0, 0), db.equipment["eq_spear_01"].Traits);
        Assert.Equal(new WeaponTraits(0, 2, 0, 0), db.equipment["eq_axe_01"].Traits);
        Assert.Equal(WeaponTraits.None, db.equipment["eq_sword_01"].Traits);
    }

    [Fact]
    public async Task TheHelpScreenExplainsEveryWeaponClassAndMasteryFromTheMasterData()
    {
        // ヘルプは数値を書き写さずマスタから組み立てている。実際に描画して、
        // 武器クラスの個性とマスタリーの習得条件が漏れなく出ることを確かめる。
        var db = Load();

        // 7=武器の種類 → 空行でページ送り → 8=職業とマスタリー → 空行 → 0=戻る
        string text = await CaptureHelpAsync(db, "7\n\n8\n\n0\n");

        foreach (var word in new[] { "剣", "短剣", "槍", "斧", "弓" })
            Assert.Contains(word, text);
        foreach (var word in new[] { "連撃", "会心域", "装甲貫通", "装甲破壊" })
            Assert.Contains(word, text);
        Assert.Contains($"合計-{QudCombat.MAX_ARMOR_SHRED}", text);
        Assert.Contains($"-{QudCombat.FOLLOW_UP_PV_PENALTY}ずつ下がる", text);

        // 習得条件の要点。どれか1つでも欠けると、プレイヤーは習熟度が増えない理由に辿り着けない。
        foreach (var word in new[] { "クラス習熟度", "正規クリア", "生存", "冒険者のランク以上", "クラスチェンジ" })
            Assert.Contains(word, text);

        // 職業スキルはマスタから列挙する。全職業・全スキルが必要習熟度つきで並ぶ。
        foreach (var cls in db.classes.Values)
        {
            Assert.Contains(cls.className, text);
            foreach (var entry in cls.classSkills.Where(e => e.Skill != null))
                Assert.Contains($"習熟度{entry.requiredClearCount,2} {entry.Skill!.skillName}", text);
        }
    }

    static async Task<string> CaptureHelpAsync(GameMasterData db, string input)
    {
        var originalIn = Console.In;
        var originalOut = Console.Out;
        using var reader = new StringReader(input);
        using var writer = new StringWriter();
        try
        {
            Console.SetIn(reader);
            Console.SetOut(writer);
            Ui.Use(new ConsoleGameIo());
            await HelpScreen.ShowAsync(db);
            return writer.ToString();
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
        }
    }

    // ---- 測定の下ごしらえ ----

    /// <summary>1体を倒しきるまでにかかった攻撃回数の平均。実戦と同じ BattleResolver を通す。</summary>
    static double RoundsToKill(
        GameMasterData db, EquipmentMasterData weapon, int str, int agi, int av, int dummyHp)
    {
        long rounds = 0;
        for (int i = 0; i < Battles; i++)
        {
            var attacker = Fighter(db, weapon, str, agi, hp: 1_000_000);
            var dummy = ArmouredDummy(av, dummyHp);
            var result = BattleResolver.Resolve(
                new IUnitMember?[] { attacker, null, null, null, null, null },
                new IUnitMember?[] { dummy, null, null, null, null, null },
                new List<string>(), turn: 1, phase: 1, new MoraleState(1_000_000));
            rounds += result.rounds;
        }
        return (double)rounds / Battles;
    }

    /// <summary>能力値と得物だけが違う、スキルも防具も持たない冒険者。</summary>
    static AdventurerData Fighter(
        GameMasterData db, EquipmentMasterData weapon, int strength, int agility, int hp)
    {
        var a = new AdventurerData(new AdventurerMasterData
        {
            id = "test_fighter", baseName = "測定用",
            vitality = 12, mental = 12, strength = strength, agility = agility,
            intelligence = 8, constitution = 12,
        })
        {
            CombatHpMax = hp,
            CombatHp = hp,
        };
        a.SetEquipped(EquipSlot.RightHand, weapon);
        return a;
    }

    /// <summary>装甲だけを持ち、反撃してこない案山子。</summary>
    static EnemyData ArmouredDummy(int naturalAv, int hp) =>
        new(new EnemyMasterData
        {
            id = "test_dummy", baseName = "案山子",
            vitality = 10, mental = 10, strength = 1, agility = 1,
            intelligence = 1, constitution = 8,
            naturalAv = naturalAv - QudCombat.Modifier(8), // CON由来のAVを差し引いて狙った実効AVに合わせる
            naturalDamageDice = "1d1-1",
            naturalPv = 0,
        })
        {
            CombatHpMax = hp,
            CombatHp = hp,
        };

    static double Average(Func<QudCombat.AttackResult> attack)
    {
        const int trials = 20_000;
        long total = 0;
        for (int i = 0; i < trials; i++) total += attack().damage;
        return (double)total / trials;
    }
}
