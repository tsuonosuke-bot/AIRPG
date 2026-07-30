using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using Xunit;

namespace GuildSimulator.Tests;

/// <summary>
/// F〜Sの7段階ランクと、クラス習熟度が入る「適正ランク」の検証。
/// 冒険者・クエスト・ギルドの3つのランクは同じ物差しに乗っている。
/// </summary>
public class RankTests
{
    [Theory]
    [InlineData(1, "F")]
    [InlineData(2, "E")]
    [InlineData(3, "D")]
    [InlineData(4, "C")]
    [InlineData(5, "B")]
    [InlineData(6, "A")]
    [InlineData(7, "S")]
    public void EachStoredNumberMapsToItsLetter(int stored, string label)
    {
        Assert.Equal(label, Rank.Label(stored));
    }

    [Fact]
    public void OutOfRangeValuesAreClampedToTheEnds()
    {
        // セーブデータやマスタに範囲外が入っていても、表示が壊れるより端に丸めたほうが安全。
        Assert.Equal("F", Rank.Label(0));
        Assert.Equal("F", Rank.Label(-5));
        Assert.Equal("S", Rank.Label(99));
        Assert.Equal(Rank.Min, Rank.Clamp(0));
        Assert.Equal(Rank.Max, Rank.Clamp(99));
    }

    [Theory]
    // 冒険者D(3) の適正帯は D〜B。格下Eは学ぶものがなく、格上すぎるAは連れ回されているだけ。
    [InlineData(3, 2, false)]
    [InlineData(3, 3, true)]
    [InlineData(3, 4, true)]
    [InlineData(3, 5, true)]
    [InlineData(3, 6, false)]
    public void SuitableRankIsTheBandFromYourOwnRankUpTwo(int adventurerRank, int questRank, bool suitable)
    {
        Assert.Equal(suitable, Rank.IsSuitable(questRank, adventurerRank));
    }

    [Fact]
    public void TheSuitableBandReadsTheSameFromBothSides()
    {
        // 冒険者から見た「受けるべきクエスト」と、クエストから見た「伸びる冒険者」は表裏。
        Assert.Equal("D〜B", Rank.SuitableRangeLabel(3));
        Assert.Equal("F〜D", Rank.SuitableAdventurerRangeLabel(3));

        // 端では潰れて1つになる。
        Assert.Equal("S", Rank.SuitableRangeLabel(Rank.Max));
        Assert.Equal("F", Rank.SuitableAdventurerRangeLabel(Rank.Min));
    }

    [Fact]
    public void ClassMasteryOnlyCountsClearsInsideTheSuitableBand()
    {
        var cls = new ClassMasterData { id = "cls", className = "テスト職" };
        var adventurer = new AdventurerData(new AdventurerMasterData
        {
            id = "adv", baseName = "測定用", defaultRank = 3, // D
        })
        {
            currentClass = cls,
        };

        adventurer.OnClearQuest(2); // E: 格下
        Assert.Equal(0, adventurer.CurrentClassClearCount);

        adventurer.OnClearQuest(6); // A: 格上すぎる
        Assert.Equal(0, adventurer.CurrentClassClearCount);

        adventurer.OnClearQuest(3); // D: 同ランク
        adventurer.OnClearQuest(5); // B: 適正帯の上端
        Assert.Equal(2, adventurer.CurrentClassClearCount);

        // 死者は数えない。
        adventurer.isAlive = false;
        adventurer.OnClearQuest(3);
        Assert.Equal(2, adventurer.CurrentClassClearCount);
    }

    [Fact]
    public void AdventurerRankStopsAtS()
    {
        var adventurer = new AdventurerData(new AdventurerMasterData
        {
            id = "adv", baseName = "測定用", defaultRank = Rank.Min,
        });

        // ランクポイントを注ぎ込んでも S より上には行かない。
        for (int i = 0; i < 100; i++) adventurer.AddRankPoints(1000, out _);

        Assert.Equal(Rank.Max, adventurer.rank);
        Assert.True(adventurer.IsMaxRank);
        Assert.Equal("S", adventurer.RankLabel);
        // 上限に達したらRPも溜めない。溜まり続けると昇格できるように見えてしまう。
        Assert.Equal(0, adventurer.rankPoint);
    }

    [Fact]
    public void GuildRankStopsAtSToo()
    {
        var guild = new GuildManager(startGold: 100, startRank: Rank.Min);
        for (int i = 0; i < 20; i++) guild.RankUp(1, "昇格試験");

        Assert.Equal(Rank.Max, guild.GuildRank);
        Assert.Equal("S", guild.GuildRankLabel);
        Assert.True(guild.IsMaxGuildRank);
    }

    [Fact]
    public void MasterDataRanksAllFitInsideTheSevenSteps()
    {
        // マスタが範囲外の数値を持っていると、表示は丸められて実挙動とずれる。
        var db = Game.Data.MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

        foreach (var quest in db.allQuests)
            Assert.InRange(quest.rank, Rank.Min, Rank.Max);
        foreach (var adventurer in db.allAdventurers)
        {
            Assert.InRange(adventurer.defaultRank, Rank.Min, Rank.Max);
            Assert.InRange(adventurer.recruitGuildRank, Rank.Min, Rank.Max);
        }
        foreach (var facility in db.facilities.Values)
            Assert.InRange(facility.requiredGuildRank, Rank.Min, Rank.Max);
    }
}
