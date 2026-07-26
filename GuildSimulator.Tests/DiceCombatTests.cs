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

        Assert.Contains(logs, log => log.Contains("武器ダイス"));
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
