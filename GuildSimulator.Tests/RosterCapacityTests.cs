using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

/// <summary>
/// ギルドに抱えられる冒険者の人数（在籍上限）。初期は4人で、宿舎系の施設を建てるたびに増える。
/// 遠征へ出せる人数（<see cref="GuildManager.PartyCapacity"/>）とは別の蓋であることに注意。
/// </summary>
[Collection("Guild static state")]
public sealed class RosterCapacityTests
{
    [Fact]
    public void NewGuildStartsAtFourAndLodgingFacilitiesRaiseCapacityToEight()
    {
        var db = LoadMaster();
        var guild = new GuildManager(startGold: 20_000, startRank: Rank.Max);
        var facilities = LodgingFacilities(db);

        Assert.Equal(4, GuildManager.BaseRosterCapacity);
        Assert.Equal(GuildManager.BaseRosterCapacity, guild.RosterCapacity);

        for (int i = 0; i < facilities.Count; i++)
        {
            Assert.True(guild.TryBuildFacility(facilities[i], out var reason), reason);
            Assert.Equal(GuildManager.BaseRosterCapacity + i + 1, guild.RosterCapacity);
        }

        Assert.Equal(GuildManager.MaximumRosterCapacity, guild.RosterCapacity);
    }

    [Fact]
    public void HiringIsBlockedOnceTheRosterIsFullAndTheReasonPointsAtTheFacilities()
    {
        var guild = new GuildManager();
        AddMembers(guild, GuildManager.BaseRosterCapacity - 1);

        Assert.True(guild.CanHireAdventurer(out _));

        AddMembers(guild, 1);

        Assert.True(guild.IsRosterFull);
        Assert.False(guild.CanHireAdventurer(out string reason));
        Assert.Contains("在籍上限", reason);
        Assert.Contains("4/4人", reason);
        Assert.Contains("施設", reason);
    }

    [Fact]
    public void BuildingALodgingImmediatelyReopensHiring()
    {
        var db = LoadMaster();
        var guild = new GuildManager(startGold: 20_000, startRank: Rank.Max);
        AddMembers(guild, GuildManager.BaseRosterCapacity);

        Assert.False(guild.CanHireAdventurer(out _));

        Assert.True(guild.TryBuildFacility(LodgingFacilities(db)[0], out var reason), reason);

        Assert.True(guild.CanHireAdventurer(out _));
        Assert.Equal(GuildManager.BaseRosterCapacity + 1, guild.RosterCapacity);
    }

    [Fact]
    public void TheDeadDoNotOccupyARosterSlot()
    {
        // 埋葬待ちの故人が席を塞ぐと、埋葬費が払えないだけで雇入れまで止まってしまう。
        var guild = new GuildManager();
        var members = AddMembers(guild, GuildManager.BaseRosterCapacity);
        Assert.False(guild.CanHireAdventurer(out _));

        members[0].isAlive = false;

        Assert.Equal(GuildManager.BaseRosterCapacity - 1, guild.RosterCount);
        Assert.True(guild.CanHireAdventurer(out _));
    }

    [Fact]
    public void DismissingSomeoneFreesASlotAndDropsTheirUpkeep()
    {
        var guild = new GuildManager();
        var members = AddMembers(guild, GuildManager.BaseRosterCapacity);
        int upkeepBefore = guild.AdventurerUpkeepPerTurn;
        int goldBefore = guild.Gold;
        Assert.False(guild.CanHireAdventurer(out _));

        Assert.True(guild.TryDismissAdventurer(members[0], out var reason), reason);

        Assert.Equal(GuildManager.BaseRosterCapacity - 1, guild.RosterCount);
        Assert.DoesNotContain(members[0], guild.adventurers);
        Assert.True(guild.CanHireAdventurer(out _));
        Assert.True(guild.AdventurerUpkeepPerTurn < upkeepBefore);
        // 維持費が払えなくなった状況の逃げ道なので、解雇そのものに費用は取らない。
        Assert.Equal(goldBefore, guild.Gold);
        Assert.Contains(guild.economyLogs, log => log.Contains("解雇") && log.Contains(members[0].name));
    }

    [Fact]
    public void TheDeadAreBuriedRatherThanDismissed()
    {
        var guild = new GuildManager();
        var members = AddMembers(guild, 1);
        members[0].isAlive = false;

        Assert.False(guild.TryDismissAdventurer(members[0], out var reason));

        Assert.Contains("埋葬", reason);
        Assert.Contains(members[0], guild.adventurers);
    }

    [Fact]
    public void DismissingSomeoneWhoLeftTheGuildIsRefused()
    {
        var guild = new GuildManager();
        var members = AddMembers(guild, 1);
        Assert.True(guild.TryDismissAdventurer(members[0], out _));

        Assert.False(guild.TryDismissAdventurer(members[0], out var reason));

        Assert.Contains("見つかりません", reason);
    }

    [Fact]
    public void TheRosterCanAlwaysFieldAFullParty()
    {
        // 在籍上限が編成上限を下回ると、建てた編成枠が永久に使えない飾りになる。
        Assert.True(GuildManager.BaseRosterCapacity >= GuildManager.BasePartyCapacity);
        Assert.True(GuildManager.MaximumRosterCapacity >= GuildManager.MaximumPartyCapacity);
    }

    [Theory]
    [InlineData("fac_lodging_01", 1, "F")]
    [InlineData("fac_lodging_02", 2, "E")]
    [InlineData("fac_lodging_03", 4, "C")]
    [InlineData("fac_lodging_04", 6, "A")]
    public void LodgingFacilitiesUnlockOneSlotEachAcrossTheRanks(
        string expectedId, int expectedRank, string expectedRankLabel)
    {
        var db = LoadMaster();
        var facilities = LodgingFacilities(db);

        Assert.Equal(GuildManager.RosterCapacityUpgradeMaximum, facilities.Count);

        var facility = facilities.Single(f => f.id == expectedId);
        Assert.Equal(1, facility.rosterSlotBonus);
        Assert.Equal(expectedRank, facility.requiredGuildRank);
        Assert.Equal(expectedRankLabel, Rank.Label(facility.requiredGuildRank));
    }

    [Fact]
    public void BuiltLodgingFacilitiesSurviveSaveLoad()
    {
        var db = LoadMaster();
        var guild = new GuildManager(startGold: 20_000, startRank: Rank.Max);
        guild.RestoreFacilities(LodgingFacilities(db).Take(2));
        var manager = new QuestManager(guild);

        string json = SaveManager.Serialize(
            guild,
            manager,
            currentTurn: 7,
            new List<AdventurerMasterData>());
        var loaded = SaveManager.Deserialize(json, db);

        Assert.Equal(GuildManager.BaseRosterCapacity + 2, loaded.Guild.RosterCapacity);
    }

    [Fact]
    public void MasterValidatorRejectsNegativeRosterSlotBonus()
    {
        var db = LoadMaster();
        db.facilities["fac_lodging_01"].rosterSlotBonus = -1;

        var errors = MasterValidator.Validate(db);

        Assert.Contains(errors, error =>
            error.Contains("fac_lodging_01") && error.Contains("0以上"));
    }

    [Fact]
    public void MasterValidatorRejectsRosterSlotBonusBeyondTheMaximum()
    {
        var db = LoadMaster();
        db.facilities["invalid_extra_lodging"] = new FacilityMasterData
        {
            id = "invalid_extra_lodging",
            displayName = "不正な追加宿舎",
            rosterSlotBonus = 1,
        };

        var errors = MasterValidator.Validate(db);

        Assert.Contains(errors, error => error.Contains("在籍枠の強化合計が在籍上限を超えています"));
    }

    [Fact]
    public void MasterValidatorRejectsLodgingsThatNeverReachTheMaximum()
    {
        var db = LoadMaster();
        db.facilities.Remove("fac_lodging_04");

        var errors = MasterValidator.Validate(db);

        Assert.Contains(errors, error => error.Contains("在籍枠の強化合計が"));
    }

    static GameMasterData LoadMaster()
    {
        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        return MasterLoader.Load(dataDir);
    }

    static List<FacilityMasterData> LodgingFacilities(GameMasterData db) =>
        db.facilities.Values
            .Where(facility => facility.rosterSlotBonus > 0)
            .OrderBy(facility => facility.requiredGuildRank)
            .ThenBy(facility => facility.id)
            .ToList();

    static List<AdventurerData> AddMembers(GuildManager guild, int count)
    {
        var members = Enumerable.Range(guild.adventurers.Count + 1, count)
            .Select(index => new AdventurerData(new AdventurerMasterData
            {
                id = $"roster-{index}",
                baseName = $"roster-{index}",
                defaultLevel = 1,
                vitality = 10,
                mental = 10,
                strength = 10,
                agility = 10,
                intelligence = 10,
                constitution = 10,
            }))
            .ToList();
        foreach (var member in members)
            guild.AddAdventurer(member);
        return members;
    }
}
