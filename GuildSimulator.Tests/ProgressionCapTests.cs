using GuildSimulator.Cli;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Game.Data;
using GuildSimulator.Game.Presentation;
using GuildSimulator.Game.Screens;
using Xunit;

namespace GuildSimulator.Tests;

public sealed class ProgressionCapTests
{
    [Theory]
    [InlineData(1, 5, 700)]
    [InlineData(2, 10, 1800)]
    [InlineData(3, 16, 3300)]
    [InlineData(4, 24, 5200)]
    [InlineData(5, 32, 6300)]
    [InlineData(6, 40, 7500)]
    [InlineData(7, 50, 8800)]
    public void EachRankHasOneAuthoritativeLevelAndMasteryCap(
        int rank, int expectedLevelCap, int expectedMasteryCap)
    {
        Assert.Equal(expectedLevelCap, Rank.LevelCap(rank));
        Assert.Equal(expectedMasteryCap, Rank.MasteryCap(rank));
    }

    [Fact]
    public void LevelStopsAtCurrentRankCapAndResumesAfterPromotion()
    {
        var adventurer = Adventurer("level-cap", currentClass: null);
        while (adventurer.level < Rank.LevelCap(Rank.Min))
            Assert.True(adventurer.AddExperience(adventurer.RequiredExpForNextLevel, out _));

        Assert.Equal(5, adventurer.level);
        Assert.True(adventurer.IsAtLevelCap);
        Assert.Equal(0, adventurer.experience);
        Assert.False(adventurer.AddExperience(10_000, out int blockedLevelUps));
        Assert.Equal(0, blockedLevelUps);
        Assert.Equal(5, adventurer.level);
        Assert.Equal(0, adventurer.experience);

        MakeEligibleForPromotion(adventurer);
        Assert.True(adventurer.TryRankUp(out _));
        Assert.Equal(10, adventurer.LevelCap);
        Assert.False(adventurer.IsAtLevelCap);
        Assert.True(adventurer.AddExperience(adventurer.RequiredExpForNextLevel, out int resumedLevelUps));
        Assert.Equal(1, resumedLevelUps);
        Assert.Equal(6, adventurer.level);
    }

    [Fact]
    public void MasteryStopsAtCurrentRankCapAndPromotionUnlocksTheNextBand()
    {
        var cls = new ClassMasterData { id = "cap-class", className = "上限試験職" };
        var adventurer = Adventurer("mastery-cap", cls);
        adventurer.intelligence = 0;

        ClassMasteryProgress progress = default;
        for (int i = 0; i < 7; i++)
            progress = adventurer.OnClearQuest(Rank.Min);

        Assert.Equal(100, progress.PointsGained);
        Assert.Equal(700, adventurer.CurrentClassMastery);
        Assert.True(adventurer.IsAtMasteryCap);

        progress = adventurer.OnClearQuest(Rank.Min);
        Assert.Equal(0, progress.PointsGained);
        Assert.Equal(700, progress.TotalPoints);

        MakeEligibleForPromotion(adventurer);
        Assert.True(adventurer.TryRankUp(out var promotion));
        Assert.Equal(1000, promotion.MasteryGained);
        Assert.Equal(1700, adventurer.CurrentClassMastery);
        Assert.Equal(1800, adventurer.MasteryCap);

        progress = adventurer.OnClearQuest(adventurer.rank);
        Assert.Equal(100, progress.PointsGained);
        Assert.Equal(1800, progress.TotalPoints);
        Assert.Equal(0, adventurer.OnClearQuest(adventurer.rank).PointsGained);
    }

    [Fact]
    public void LoadingProgressClampsEveryClassMasteryToTheCurrentRankCap()
    {
        var cls = new ClassMasterData { id = "saved-class", className = "復元試験職" };
        var adventurer = Adventurer("restore-cap", cls);

        adventurer.RestoreProgress(
            Array.Empty<(SkillMasterData skill, ClassMasterData? ownerClass)>(),
            new Dictionary<string, int> { [cls.id] = 99_999 });

        Assert.Equal(Rank.MasteryCap(Rank.Min), adventurer.CurrentClassMastery);
        Assert.Equal(Rank.MasteryCap(Rank.Min), adventurer.ExportClassMasteryPoints()[cls.id]);
    }

    static void MakeEligibleForPromotion(AdventurerData adventurer)
    {
        var requirement = adventurer.NextRankRequirement!.Value;
        adventurer.higherRankClears = requirement.higherRankClears;
        adventurer.suitableRankClearsTotal = requirement.suitableTotalClears;
    }

    static AdventurerData Adventurer(string id, ClassMasterData? currentClass) =>
        new(new AdventurerMasterData
        {
            id = id,
            baseName = "上限確認者",
            defaultLevel = 1,
            defaultRank = Rank.Min,
            vitality = 10,
            mental = 10,
            strength = 10,
            agility = 10,
            intelligence = 0,
            constitution = 10,
            appearance = 10,
            DefaultClass = currentClass,
            defaultClassId = currentClass?.id ?? "",
        });
}

[Collection("Console presentation")]
public sealed class ProgressionCapPresentationTests
{
    [Fact]
    public async Task AdventurerDetailShowsBothCapsAndThePromotionGateForTheNextSkill()
    {
        var nextSkill = new SkillMasterData { id = "cap-next-skill", skillName = "次段階の奥義" };
        var cls = new ClassMasterData { id = "cap-ui-class", className = "上限表示職" };
        cls.classSkills.Add(new ClassSkillEntry
        {
            skillId = nextSkill.id,
            Skill = nextSkill,
            requiredClearCount = 1200,
        });
        var master = new AdventurerMasterData
        {
            id = "cap-ui",
            baseName = "表示確認者",
            defaultLevel = 5,
            defaultRank = Rank.Min,
            vitality = 10,
            mental = 10,
            strength = 10,
            agility = 10,
            intelligence = 10,
            constitution = 10,
            appearance = 10,
            DefaultClass = cls,
            defaultClassId = cls.id,
        };
        var adventurer = new AdventurerData(master);
        adventurer.RestoreProgress(
            Array.Empty<(SkillMasterData skill, ClassMasterData? ownerClass)>(),
            new Dictionary<string, int> { [cls.id] = Rank.MasteryCap(Rank.Min) });
        var guild = new GuildManager(startGold: 100);
        guild.AddAdventurer(adventurer);

        string text = await CaptureConsoleAsync(
            "1\n0\n0\n",
            () => AdventurerScreen.ShowAsync(new GameMasterData(), guild));

        Assert.Contains("レベル      : 5/5", text);
        Assert.Contains("Fランク上限・昇格で上限Lv10", text);
        Assert.Contains("クラス習熟度: 700/700", text);
        Assert.Contains("Eへ昇格すると上限1800まで解放", text);
        Assert.Contains("次段階の奥義", text);
        Assert.Contains("Eランクへの昇格が必要", text);
    }

    static async Task<string> CaptureConsoleAsync(string inputText, Func<Task> action)
    {
        var originalIn = Console.In;
        var originalOut = Console.Out;
        using var input = new StringReader(inputText);
        using var output = new StringWriter();
        try
        {
            Console.SetIn(input);
            Console.SetOut(output);
            Ui.Use(new ConsoleGameIo());
            await action();
            return output.ToString();
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
        }
    }
}
