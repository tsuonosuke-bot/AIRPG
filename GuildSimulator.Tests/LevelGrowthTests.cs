using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Game.Data;
using Xunit;
using Xunit.Abstractions;

namespace GuildSimulator.Tests;

/// <summary>
/// レベルアップの成長。1レベルにつき1能力だけが+1され、どこが伸びるかは
/// 種族とクラスの重みつき抽選で決まる（プレイヤーには選べない）。
/// </summary>
public class LevelGrowthTests
{
    readonly ITestOutputHelper output;

    public LevelGrowthTests(ITestOutputHelper output) => this.output = output;

    static GameMasterData Load() => MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    static AdventurerData Make(ClassMasterData? cls = null, RaceMasterData? race = null) =>
        new(new AdventurerMasterData
        {
            id = "adv", baseName = "測定用",
            vitality = 10, mental = 10, strength = 10, agility = 10,
            intelligence = 10, constitution = 10,
        })
        {
            currentClass = cls,
            race = race,
            // このクラスは成長抽選そのものを検証する。ランク上限は専用テストで扱う。
            rank = Rank.Max,
        };

    static int StatTotal(AdventurerData a) =>
        a.vitality + a.mental + a.strength + a.agility + a.intelligence;

    [Fact]
    public void EachLevelRaisesExactlyOneAbilityByOne()
    {
        var db = Load();
        var a = Make(db.classes["class_warrior"], db.races["Race_Human"]);
        int before = StatTotal(a);
        int conBefore = a.constitution;

        const int levels = 30;
        for (int i = 0; i < levels; i++) a.AddExperience(a.RequiredExpForNextLevel, out _);

        Assert.Equal(1 + levels, a.level);
        Assert.Equal(before + levels * AdventurerData.StatPointsPerLevel, StatTotal(a));
        // 体格は成長対象外。素の装甲値と積載上限は雇用時の素質で決まる。
        Assert.Equal(conBefore, a.constitution);
    }

    [Fact]
    public void GrowthIsAboutHalfOfWhatTwoLevelsUsedToGive()
    {
        // 旧仕様は5能力を独立抽選していて、斧戦士/ヒューマンで毎レベル約2.35点伸びていた。
        // レベル差だけでキャラの優劣が決まらないよう、1点に絞ってある。
        Assert.Equal(1, AdventurerData.StatPointsPerLevel);
    }

    [Fact]
    public void ClassAndRaceOnlyTiltTheOddsTheyDoNotLockTheOutcome()
    {
        var db = Load();
        var counts = new Dictionary<StatType, int>();
        foreach (var t in AdventurerData.GrowableStats) counts[t] = 0;

        const int trials = 4000;
        for (int i = 0; i < trials; i++)
        {
            // 魔術師/エルフは知力に大きく寄っている（int +0.50 / +0.15）。
            var a = Make(db.classes["class_Sorcerer"], db.races["Race_Elf"]);
            var before = (a.vitality, a.mental, a.strength, a.agility, a.intelligence);
            a.AddExperience(a.RequiredExpForNextLevel, out _);

            if (a.vitality > before.vitality) counts[StatType.Vitality]++;
            if (a.mental > before.mental) counts[StatType.Mental]++;
            if (a.strength > before.strength) counts[StatType.Strength]++;
            if (a.agility > before.agility) counts[StatType.Agility]++;
            if (a.intelligence > before.intelligence) counts[StatType.Intelligence]++;
        }

        foreach (var (stat, n) in counts)
            output.WriteLine($"{stat,-14}{(double)n / trials:P1}");

        // 得意な能力がいちばん伸びやすい。
        int best = counts.Values.Max();
        Assert.Equal(best, counts[StatType.Intelligence]);

        // それでも他の能力が伸びないわけではない。偏りすぎると育ちの個体差が消えてしまう。
        foreach (var (stat, n) in counts)
            Assert.True(n > 0, $"{stat} が一度も伸びていない（重み0の能力ができている）");

        // 筋力は成長率0でも下駄（BaseGrowthWeight）のぶんだけ伸びる余地が残る。
        Assert.True((double)counts[StatType.Strength] / trials > 0.02,
            "不得手な能力がまったく伸びない");
        Assert.True((double)counts[StatType.Intelligence] / trials < 0.75,
            "得意な能力に偏りすぎている");
    }

    [Fact]
    public void TwoAdventurersOfTheSameClassAndLevelDiverge()
    {
        // 同じ職業・同じレベルでも、どこが伸びたかで別のキャラクターになる。
        // 「レベルさえ上げれば代わりが効く」を避けるのが1レベル1能力にした目的。
        var db = Load();
        var cls = db.classes["class_Swordman"];
        var race = db.races["Race_Human"];

        int different = 0;
        const int pairs = 200;
        for (int i = 0; i < pairs; i++)
        {
            var a = Make(cls, race);
            var b = Make(cls, race);
            for (int lv = 0; lv < 10; lv++)
            {
                a.AddExperience(a.RequiredExpForNextLevel, out _);
                b.AddExperience(b.RequiredExpForNextLevel, out _);
            }
            if ((a.vitality, a.mental, a.strength, a.agility, a.intelligence)
                != (b.vitality, b.mental, b.strength, b.agility, b.intelligence)) different++;
        }

        output.WriteLine($"Lv11で能力配分が違った組: {different}/{pairs}");
        Assert.True((double)different / pairs > 0.9, "同条件の冒険者がほとんど同じ育ち方をしている");
    }
}
