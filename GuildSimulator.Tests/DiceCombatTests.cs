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

    [Fact]
    public void WeaponDiceIsTheBaseValueSoBiggerDiceHitsHarder()
    {
        // 火力の土台は武器。同じ能力なら、ダイスの出目がそのまま威力差になる。
        var weak = BattleResolver.ComputeDamage(diceRoll: 3, atkStat: 10, maxAtkBonus: 0, defStat: 0, levelDiff: 0);
        var strong = BattleResolver.ComputeDamage(diceRoll: 12, atkStat: 10, maxAtkBonus: 0, defStat: 0, levelDiff: 0);
        Assert.True(strong.final > weak.final);
    }

    [Fact]
    public void AttackStatAmplifiesTheWeaponDiceRatherThanBeingAddedToIt()
    {
        // 能力は加算ではなく倍率。攻撃力が上がるほど同じダイスから引き出せる威力が増える。
        var low = BattleResolver.ComputeDamage(diceRoll: 6, atkStat: 0, maxAtkBonus: 0, defStat: 0, levelDiff: 0);
        var high = BattleResolver.ComputeDamage(diceRoll: 6, atkStat: 20, maxAtkBonus: 0, defStat: 0, levelDiff: 0);

        Assert.Equal(1f, low.amplify, 3);
        Assert.Equal(6, low.raw);
        Assert.True(high.amplify > low.amplify);
        Assert.True(high.raw > low.raw);
    }

    [Fact]
    public void MaxAtkBonusCapsHowMuchAbilityAWeaponCanCarry()
    {
        // 短剣（上限5）は腕力を乗せきれず、上限を超えた攻撃力ぶんは威力に反映されない。
        var atCap = BattleResolver.ComputeDamage(diceRoll: 6, atkStat: 5, maxAtkBonus: 5, defStat: 0, levelDiff: 0);
        var overCap = BattleResolver.ComputeDamage(diceRoll: 6, atkStat: 40, maxAtkBonus: 5, defStat: 0, levelDiff: 0);
        Assert.Equal(atCap.final, overCap.final);

        // 上限0は「無制限」。同じ攻撃力でも大剣なら伸びきる。
        var uncapped = BattleResolver.ComputeDamage(diceRoll: 6, atkStat: 40, maxAtkBonus: 0, defStat: 0, levelDiff: 0);
        Assert.True(uncapped.final > overCap.final);
    }

    [Fact]
    public void DefenceIsSubtractedFromTheAmplifiedTotal()
    {
        // 防御は「増幅後の合計」から引かれる。
        var bare = BattleResolver.ComputeDamage(diceRoll: 10, atkStat: 10, maxAtkBonus: 0, defStat: 0, levelDiff: 0);
        var armored = BattleResolver.ComputeDamage(diceRoll: 10, atkStat: 10, maxAtkBonus: 0, defStat: 6, levelDiff: 0);

        Assert.Equal(bare.raw, armored.raw);
        Assert.Equal(bare.raw - 6, armored.final);
    }

    [Fact]
    public void HeavyArmourNeverFullyBlocksButKeepsDamageLow()
    {
        // 防御が素の威力を上回っても、素の威力の15%は必ず通る（無限消耗戦の安全弁）。
        var crushed = BattleResolver.ComputeDamage(diceRoll: 40, atkStat: 0, maxAtkBonus: 0, defStat: 500, levelDiff: 0);
        Assert.Equal(40, crushed.raw);
        Assert.Equal(6, crushed.final); // ceil(40 * 0.15)

        // 素の威力が小さければ最低保証は1に落ちるが、0にはならない。
        var scratch = BattleResolver.ComputeDamage(diceRoll: 1, atkStat: 0, maxAtkBonus: 0, defStat: 500, levelDiff: 0);
        Assert.Equal(1, scratch.final);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(0)]
    [InlineData(5)]
    public void DamageIsAlwaysAtLeastOneWhateverTheLevelGap(int levelDiff)
    {
        var result = BattleResolver.ComputeDamage(
            diceRoll: 1, atkStat: 0, maxAtkBonus: 0, defStat: 9999, levelDiff: levelDiff);
        Assert.True(result.final >= 1);
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

        // ログにはダメージの計算過程（ダイス→増幅→防御の減算）が残る。
        Assert.Contains(logs, log => log.Contains("物理") && log.Contains("防御"));
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
