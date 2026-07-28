using GuildSimulator.Core;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Systems.Battle;
using Xunit;

namespace GuildSimulator.Tests;

public class DiceCombatTests
{
    [Theory]
    [InlineData("1d6", 1, 6, 0)]
    [InlineData("2d4+1", 2, 4, 1)]
    [InlineData("1d6-1", 1, 6, -1)]
    [InlineData("3d8", 3, 8, 0)]
    public void ParsesDiceNotation(string notation, int count, int sides, int modifier)
    {
        var dice = Dice.Parse(notation);
        Assert.Equal(count, dice.count);
        Assert.Equal(sides, dice.sides);
        Assert.Equal(modifier, dice.modifier);
    }

    [Fact]
    public void EmptyOrInvalidNotationFallsBackToOneDFour()
    {
        Assert.Equal(new Dice(1, 4).ToString(), Dice.Parse("").ToString());
        Assert.Equal(new Dice(1, 4).ToString(), Dice.Parse(null).ToString());
        Assert.Equal(new Dice(1, 4).ToString(), Dice.Parse("garbage").ToString());
    }

    [Fact]
    public void RollStaysWithinMinMaxBounds()
    {
        var dice = Dice.Parse("2d4+1");
        for (int i = 0; i < 500; i++)
        {
            int roll = dice.Roll();
            Assert.InRange(roll, dice.Min, dice.Max);
        }
        Assert.Equal(3, dice.Min);
        Assert.Equal(9, dice.Max);
    }

    // 能力値modifierは基準値8からMODIFIER_STEP(=2)ごとに±1。
    [Theory]
    [InlineData(8, 0)]
    [InlineData(9, 0)]
    [InlineData(10, 1)]
    [InlineData(20, 6)]
    [InlineData(7, -1)]
    [InlineData(6, -1)]
    [InlineData(0, -4)]
    public void AttributeModifierStepsEveryTwoPointsFromTheBaseline(int stat, int expected)
    {
        Assert.Equal(expected, QudCombat.Modifier(stat));
    }

    [Fact]
    public void PenetrationDieIsOneDTenMinusTwoAndExplodesOnItsMaximum()
    {
        // 1d10-2 なので通常は -1〜8。出目10で振り足すため、8を超える値も出る。
        bool sawExploded = false;
        for (int i = 0; i < 20_000; i++)
        {
            int r = QudCombat.RollPenetrationDie();
            Assert.True(r >= -1, $"下限を下回った: {r}");
            if (r > 8) sawExploded = true;
        }
        Assert.True(sawExploded, "爆発（振り足し）が一度も起きていない");
    }

    [Fact]
    public void NaturalTwentyAlwaysHitsAndNaturalOneAlwaysMisses()
    {
        // 出目20は補正前の素の値で判定され、DVがどれだけ高くても命中する。
        for (int i = 0; i < 5_000; i++)
        {
            var r = QudCombat.RollToHit(toHitBonus: 0, dv: 1000);
            Assert.Equal(r.roll == QudCombat.CRITICAL_ROLL, r.hit);
            Assert.Equal(r.roll == QudCombat.CRITICAL_ROLL, r.critical);
        }

        // 逆に出目1は、どれだけ命中補正を積んでも当たらない。
        for (int i = 0; i < 5_000; i++)
        {
            var r = QudCombat.RollToHit(toHitBonus: 1000, dv: 0);
            Assert.Equal(r.roll != QudCombat.FUMBLE_ROLL, r.hit);
        }
    }

    [Fact]
    public void ArmourFarAboveThePenetrationValueBlocksTheHitEntirely()
    {
        // 装甲を抜けなければダメージは0。最低保証はない。
        var r = QudCombat.ResolveAttack(pv: 0, av: 1000, diceNotation: "1d6", critical: false);

        Assert.Equal(0, r.penetrations);
        Assert.Equal(0, r.damage);
    }

    [Fact]
    public void ACriticalAddsPenetrationValueAndGuaranteesAtLeastOnePenetration()
    {
        // 会心はPV+1され、1回も抜けなかった場合でも最低1貫通は通る。
        for (int i = 0; i < 500; i++)
        {
            var r = QudCombat.ResolveAttack(pv: 0, av: 1000, diceNotation: "1d6", critical: true);

            Assert.Equal(QudCombat.CRITICAL_PV_BONUS, r.pv);
            Assert.Equal(1, r.penetrations);
            Assert.InRange(r.damage, 1, 6);
        }
    }

    [Fact]
    public void PenetrationTerminatesEvenWhenTheTargetHasNoArmourAtAll()
    {
        // 回帰テスト：AV=0にPVを叩きつけると3回とも抜け続けてセットが延々と回りうる。
        // セットごとにPVが2ずつ減り、さらにMAX_PENETRATIONSで蓋をしてあるので必ず有界で返る。
        for (int i = 0; i < 500; i++)
        {
            int p = QudCombat.RollPenetrations(pv: 1000, av: 0);
            Assert.InRange(p, 1, QudCombat.MAX_PENETRATIONS);
        }
    }

    [Fact]
    public void StrongerAttackersPenetrateMoreOftenOnAverage()
    {
        // PVが高いほど、AVが低いほど貫通回数が増える。
        Assert.True(AveragePenetrations(pv: 20, av: 8) > AveragePenetrations(pv: 12, av: 8));
        Assert.True(AveragePenetrations(pv: 12, av: 8) > AveragePenetrations(pv: 12, av: 16));
    }

    [Fact]
    public void DamageScalesWithThePenetrationCountNotWithTheAttributes()
    {
        // ダメージは「貫通回数 × 武器ダイス」だけで決まる。能力値は直接は乗らない。
        for (int i = 0; i < 2_000; i++)
        {
            var r = QudCombat.ResolveAttack(pv: 12, av: 4, diceNotation: "1d6", critical: false);
            if (r.penetrations == 0) Assert.Equal(0, r.damage);
            else Assert.InRange(r.damage, r.penetrations * 1, r.penetrations * 6);
        }
    }

    [Fact]
    public void EffectivePvIsCappedByTheWeaponsStatBonusLimit()
    {
        // 短剣に膂力を乗せきれない、を表す上限。上限を超えた能力値modifierは切り捨てられる。
        Assert.Equal(3 + 2, QudCombat.EffectivePv(weaponBasePv: 3, statModifier: 8, maxStatBonus: 2, flatBonus: 0));
        Assert.Equal(3 + 8, QudCombat.EffectivePv(weaponBasePv: 3, statModifier: 8, maxStatBonus: 99, flatBonus: 0));
        // 装備・スキル由来のPV補正は上限の外側で足される。
        Assert.Equal(3 + 2 + 4, QudCombat.EffectivePv(weaponBasePv: 3, statModifier: 8, maxStatBonus: 2, flatBonus: 4));
    }

    static double AveragePenetrations(int pv, int av)
    {
        long total = 0;
        const int trials = 20_000;
        for (int i = 0; i < trials; i++)
            total += QudCombat.RollPenetrations(pv, av);
        return (double)total / trials;
    }

    [Fact]
    public void HighToHitProducesCriticalsAndLogsThePenetrationMath()
    {
        // 命中しやすい組み合わせ：1d20の出目20による会心が十分な回数発生する。
        var adventurer = new AdventurerData(new AdventurerMasterData
        {
            id = "adv", baseName = "剣士",
            vitality = 10, mental = 10, strength = 10, agility = 50, intelligence = 10, constitution = 10,
        })
        {
            CombatHpMax = 10_000_000,
            CombatHp = 10_000_000,
        };
        var enemy = new EnemyData(new EnemyMasterData
        {
            id = "enemy", baseName = "案山子",
            vitality = 10, mental = 10, strength = 1, agility = 0, intelligence = 0, constitution = 0,
        })
        {
            CombatHpMax = 10_000_000,
            CombatHp = 10_000_000,
        };
        var logs = new List<string>();

        BattleResolver.Resolve(
            new IUnitMember?[] { adventurer, null, null, null, null, null },
            new IUnitMember?[] { enemy, null, null, null, null, null },
            logs,
            turn: 1,
            phase: 1,
            new MoraleState(10_000_000));

        // ログにはダメージの計算過程（1d20 vs DV → PV vs AV → 貫通回数）が残る。
        Assert.Contains(logs, log => log.Contains("1d20=") && log.Contains("DV"));
        Assert.Contains(logs, log => log.Contains("物理") && log.Contains("PV") && log.Contains("AV"));
        Assert.Contains(logs, log => log.Contains("回貫通"));
        Assert.Contains(logs, log => log.Contains("会心！"));
    }

    [Fact]
    public void AVeryHighDodgeValueTurnsMostAttacksIntoMisses()
    {
        // DVが命中補正を大きく上回る組み合わせ：出目20以外はほぼ通らない。
        var adventurer = new AdventurerData(new AdventurerMasterData
        {
            id = "adv", baseName = "新米",
            vitality = 10, mental = 10, strength = 1, agility = 0, intelligence = 0, constitution = 0,
        })
        {
            CombatHpMax = 10_000_000,
            CombatHp = 10_000_000,
        };
        var enemy = new EnemyData(new EnemyMasterData
        {
            id = "enemy", baseName = "俊敏な影",
            vitality = 10, mental = 10, strength = 1, agility = 100, intelligence = 0, constitution = 0,
        })
        {
            CombatHpMax = 10_000_000,
            CombatHp = 10_000_000,
        };
        var logs = new List<string>();

        BattleResolver.Resolve(
            new IUnitMember?[] { adventurer, null, null, null, null, null },
            new IUnitMember?[] { enemy, null, null, null, null, null },
            logs,
            turn: 1,
            phase: 1,
            new MoraleState(10_000_000));

        var attacks = logs.Where(l => l.Contains("新米→")).ToList();
        Assert.NotEmpty(attacks);
        Assert.Contains(attacks, log => log.Contains("回避！"));
        // 大半は外れる。当たるのは素の出目20だけ。
        Assert.True(attacks.Count(l => l.Contains("回避！")) > attacks.Count / 2,
            "高DVの相手にほとんど命中してしまっている");
    }
}
