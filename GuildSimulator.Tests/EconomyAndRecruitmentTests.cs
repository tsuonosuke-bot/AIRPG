using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Game.Screens;
using Xunit;

namespace GuildSimulator.Tests;

[Collection("Guild static state")]
public class EconomyAndRecruitmentTests
{
    [Theory]
    [InlineData(1, 5)]
    [InlineData(3, 15)]
    [InlineData(10, 50)]
    public void UpkeepIsFiveGoldPerCurrentLevel(int level, int expected)
    {
        Assert.Equal(expected, GuildManager.CalculateAdventurerUpkeep(level));
    }

    [Fact]
    public void GuildUpkeepChangesWhenAdventurerLevelChanges()
    {
        var adventurer = new AdventurerData(Master("adv", level: 1));
        var guild = new GuildManager();
        guild.AddAdventurer(adventurer);

        Assert.Equal(5, guild.AdventurerUpkeepPerTurn);
        Assert.Equal(15, guild.BaseUpkeepPerTurn);

        adventurer.level = 2;

        Assert.Equal(10, guild.AdventurerUpkeepPerTurn);
        Assert.Equal(20, guild.BaseUpkeepPerTurn);
    }

    [Fact]
    public void GuildPaysBaseUpkeepEvenWithoutAdventurers()
    {
        var guild = new GuildManager(startGold: 50);

        int paid = guild.PayUpkeepForAll(currentTurn: 2);

        Assert.Equal(10, paid);
        Assert.Equal(40, guild.Gold);
    }

    [Fact]
    public void EstimatedQuestNetUsesCurrentGuildUpkeep()
    {
        var guild = new GuildManager();
        guild.AddAdventurer(new AdventurerData(Master("a", level: 1)));
        guild.AddAdventurer(new AdventurerData(Master("b", level: 1)));

        Assert.Equal(20, guild.EffectiveUpkeepPerTurn);
        Assert.Equal(-10, guild.EstimateNetAfterUpkeep(rewardGold: 30, turns: 2));
        Assert.Equal(20, guild.EstimateNetAfterUpkeep(rewardGold: 60, turns: 2));
    }

    [Theory]
    [InlineData(1, 55)]
    [InlineData(3, 165)]
    public void HireCostKeepsItsExistingLevelRate(int level, int expected)
    {
        Assert.Equal(expected, RecruitScreen.CalcHireCost(Master("hire", level)));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 2)]
    [InlineData(6, 3)]
    [InlineData(7, 3)]
    [InlineData(10, 4)]
    [InlineData(14, 5)]
    public void InitialLevelDeterminesRecruitUnlockRank(int level, int expectedRank)
    {
        Assert.Equal(expectedRank, RecruitmentSystem.RequiredGuildRankForLevel(level));
    }

    [Fact]
    public void FirstTurnCanDrawThreeDistinctRankOneCandidates()
    {
        var pool = new[]
        {
            Master("a", 1, recruitGuildRank: 1),
            Master("b", 1, recruitGuildRank: 1),
            Master("c", 1, recruitGuildRank: 1),
            Master("d", 1, recruitGuildRank: 1),
        };
        var guild = new GuildManager(startRank: 1);

        var candidates = RecruitmentSystem.DrawCandidates(pool, guild, 3, (min, _) => min);

        Assert.Equal(3, candidates.Count);
        Assert.Equal(3, candidates.Distinct().Count());
        Assert.All(candidates, candidate => Assert.Equal(1, candidate.recruitGuildRank));
    }

    [Fact]
    public void LockedZeroWeightAndHiredAdventurersAreExcluded()
    {
        var eligible = Master("eligible", 1, recruitGuildRank: 1);
        var locked = Master("locked", 3, recruitGuildRank: 2);
        var disabled = Master("disabled", 1, recruitGuildRank: 1, recruitWeight: 0);
        var hired = Master("hired", 1, recruitGuildRank: 1);
        var guild = new GuildManager(startRank: 1);
        guild.AddAdventurer(new AdventurerData(hired));

        var candidates = RecruitmentSystem.DrawCandidates(
            new[] { eligible, locked, disabled, hired },
            guild,
            4,
            (min, _) => min);

        Assert.Equal(new[] { eligible }, candidates);
    }

    [Fact]
    public void RecruitmentWeightControlsTheWeightedRoll()
    {
        var common = Master("common", 1, recruitWeight: 100);
        var rare = Master("rare", 1, recruitWeight: 1);
        var guild = new GuildManager(startRank: 1);

        var candidates = RecruitmentSystem.DrawCandidates(
            new[] { common, rare },
            guild,
            1,
            (_, max) => max - 1);

        Assert.Equal(rare, candidates.Single());
    }

    [Fact]
    public void PaidRerollSpendsGoldAndDrawsCandidates()
    {
        var pool = new[]
        {
            Master("a", 1),
            Master("b", 1),
        };
        var guild = new GuildManager(startGold: 100);

        bool rerolled = RecruitmentSystem.TryRerollCandidates(
            pool, guild, 1, out var candidates, (min, _) => min);

        Assert.True(rerolled);
        Assert.Equal(80, guild.Gold);
        Assert.Equal("a", Assert.Single(candidates).id);
        Assert.Contains(guild.economyLogs, log => log.Contains("雇入れ候補の再抽選: -20G"));
    }

    [Fact]
    public void PaidRerollDoesNotChangeGoldWhenFundsAreInsufficient()
    {
        var guild = new GuildManager(startGold: 19);

        bool rerolled = RecruitmentSystem.TryRerollCandidates(
            new[] { Master("a", 1) }, guild, 1, out var candidates);

        Assert.False(rerolled);
        Assert.Equal(19, guild.Gold);
        Assert.Empty(candidates);
    }

    static AdventurerMasterData Master(
        string id,
        int level,
        int recruitGuildRank = 1,
        int recruitWeight = 100) =>
        new()
        {
            id = id,
            baseName = id,
            defaultLevel = level,
            recruitGuildRank = recruitGuildRank,
            recruitWeight = recruitWeight,
        };
}
