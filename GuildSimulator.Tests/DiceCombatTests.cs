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

    // TIER_STEP = 5。余剰がこれを超えるごとに成功レベルが1段上がる。
    [Theory]
    [InlineData(-3, BattleResolver.PenetrationTier.Blocked)]
    [InlineData(0, BattleResolver.PenetrationTier.Blocked)]
    [InlineData(1, BattleResolver.PenetrationTier.Regular)]
    [InlineData(5, BattleResolver.PenetrationTier.Regular)]
    [InlineData(6, BattleResolver.PenetrationTier.Hard)]
    [InlineData(10, BattleResolver.PenetrationTier.Hard)]
    [InlineData(11, BattleResolver.PenetrationTier.Extreme)]
    [InlineData(999, BattleResolver.PenetrationTier.Extreme)]
    public void TierRisesWithTheMarginAndStopsAtExtreme(int margin, BattleResolver.PenetrationTier expected)
    {
        Assert.Equal(expected, BattleResolver.TierOf(margin));
    }

    [Fact]
    public void OverwhelmingPenetrationRollsThreeWeaponDiceAndNeverMore()
    {
        // PVがAVを圧倒すれば必ずイクストリームに届くが、振るダイスは3本で頭打ちになる。
        // ここが「セットを繰り返す」案との決定的な違いで、火力の上限が構造的に確定する。
        for (int i = 0; i < 200; i++)
        {
            var r = BattleResolver.ResolvePenetration(
                pv: 1000, av: 0, diceNotation: "1d6", damageBonusBase: 8, critical: false);

            Assert.Equal(BattleResolver.PenetrationTier.Extreme, r.tier);
            Assert.Equal(3, r.weaponRolls);
            Assert.InRange(r.damage, 3, 18);
        }
    }

    [Fact]
    public void ArmourFarAboveThePenetrationValueBlocksTheHitEntirely()
    {
        // 装甲を抜けなければダメージは0。旧方式の「最低保証1」は撤廃されている。
        var r = BattleResolver.ResolvePenetration(
            pv: 0, av: 1000, diceNotation: "1d6", damageBonusBase: 8, critical: false);

        Assert.Equal(BattleResolver.PenetrationTier.Blocked, r.tier);
        Assert.Equal(0, r.weaponRolls);
        Assert.Equal(0, r.damage);
    }

    [Fact]
    public void ACriticalAlwaysFindsAGapAndImpales()
    {
        // 決定的成功は装甲に完全に弾かれない。CoCのインペイルに倣い、
        // 通常のロールに加えて「武器ダイスの最大値＋もう1回のロール」が乗る。
        for (int i = 0; i < 200; i++)
        {
            var r = BattleResolver.ResolvePenetration(
                pv: 0, av: 1000, diceNotation: "1d6", damageBonusBase: 8, critical: true);

            Assert.NotEqual(BattleResolver.PenetrationTier.Blocked, r.tier);
            // レギュラー1本 + 最大値6 + 追加1本 = 8〜18
            Assert.InRange(r.damage, 8, 18);
        }
    }

    // DAMAGE_BONUS_BAND = 12。CoCのSTR+SIZ表に倣い、帯域を上がるごとに加算が1段強くなる。
    [Theory]
    [InlineData(0, "", -1)]
    [InlineData(5, "", -1)]
    [InlineData(6, "", 0)]
    [InlineData(11, "", 0)]
    [InlineData(12, "1d4", 0)]
    [InlineData(23, "1d4", 0)]
    [InlineData(24, "1d6", 0)]
    [InlineData(35, "1d6", 0)]
    [InlineData(36, "2d6", 0)]
    [InlineData(48, "3d6", 0)]
    public void DamageBonusFollowsTheStrengthPlusBuildBands(int strPlusCon, string dice, int flat)
    {
        var (actualDice, actualFlat) = BattleResolver.DamageBonus(strPlusCon);
        Assert.Equal(dice, actualDice);
        Assert.Equal(flat, actualFlat);
    }

    [Fact]
    public void PenetrationTerminatesEvenWhenTheTargetHasNoArmourAtAll()
    {
        // 回帰テスト：AV=0の相手は「PVを逓減させながらセットを繰り返す」案だと無限ループになる。
        // 成功レベル方式は判定が1回で完結するため、PVがいくら高くても必ず有界で返る。
        for (int i = 0; i < 500; i++)
        {
            var r = BattleResolver.ResolvePenetration(
                pv: 0, av: 0, diceNotation: "1d6", damageBonusBase: 0, critical: false);
            Assert.InRange(r.weaponRolls, 0, 3);
        }
    }

    [Fact]
    public void StrongerAttackersReachDeeperTiersOnAverage()
    {
        // 差が大きいほど深く貫通する、が成立していること（除算方式で起きた逆転が起きない）。
        Assert.True(AverageRolls(pv: 20, av: 8) > AverageRolls(pv: 12, av: 8));
        Assert.True(AverageRolls(pv: 12, av: 8) > AverageRolls(pv: 12, av: 16));
    }

    static double AverageRolls(int pv, int av)
    {
        long total = 0;
        const int trials = 20_000;
        for (int i = 0; i < trials; i++)
            total += BattleResolver.ResolvePenetration(pv, av, "1d6", damageBonusBase: 8, critical: false).weaponRolls;
        return (double)total / trials;
    }

    [Fact]
    public void HighHitChanceProducesCriticalHitsWithWeaponDiceLogged()
    {
        // 命中率が上限95%に張り付く組み合わせ：決定的成功（上位1/5）が高確率で発生する。
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

        // ログにはダメージの計算過程（PV vs AV → 貫通判定 → 成功レベル）が残る。
        Assert.Contains(logs, log => log.Contains("物理") && log.Contains("PV") && log.Contains("AV"));
        Assert.Contains(logs, log => log.Contains("決定的成功！"));
    }

    [Fact]
    public void LowHitChanceEventuallyFumblesWithSelfDamage()
    {
        // 命中率が下限5%に張り付く組み合わせ：失敗の一部（出目96以上）が大失敗になる。
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

        Assert.Contains(logs, log => log.Contains("大失敗！"));
    }
}
