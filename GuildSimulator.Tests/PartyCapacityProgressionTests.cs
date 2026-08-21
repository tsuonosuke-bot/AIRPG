using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

[Collection("Guild static state")]
public sealed class PartyCapacityProgressionTests
{
    [Fact]
    public void NewGuildStartsAtThreeAndThreeFacilitiesRaiseCapacityToSix()
    {
        var db = LoadMaster();
        var guild = new GuildManager(startGold: 10_000, startRank: Rank.Max);
        var facilities = PartySlotFacilities(db);

        Assert.Equal(GuildManager.BasePartyCapacity, guild.PartyCapacity);
        Assert.Equal(3, facilities.Count);

        for (int i = 0; i < facilities.Count; i++)
        {
            Assert.True(guild.TryBuildFacility(facilities[i], out var reason), reason);
            Assert.Equal(GuildManager.BasePartyCapacity + i + 1, guild.PartyCapacity);
        }

        Assert.Equal(GuildManager.MaximumPartyCapacity, guild.PartyCapacity);
    }

    [Fact]
    public void ThreeMembersCanFreelyUseFrontAndRearPositionsBeforeAnyUpgrade()
    {
        var guild = new GuildManager();
        var manager = new QuestManager(guild);
        var members = AddMembers(guild, 3);
        var formation = new AdventurerData?[GuildManager.MaximumPartyCapacity];
        formation[0] = members[0];
        formation[3] = members[1];
        formation[5] = members[2];

        Assert.True(manager.TryStartQuest(Quest("free-placement"), formation, 1, out var error), error);

        var run = Assert.Single(manager.activeQuests);
        Assert.Same(members[0], run.formation[0]);
        Assert.Same(members[1], run.formation[3]);
        Assert.Same(members[2], run.formation[5]);
        Assert.Equal(3, run.EnumerateMembers().Count());
    }

    [Fact]
    public void FourthMemberIsRejectedUntilFirstSlotFacilityIsBuilt()
    {
        var db = LoadMaster();
        var guild = new GuildManager(startGold: 1_000, startRank: 2);
        var manager = new QuestManager(guild);
        var members = AddMembers(guild, 4);
        var formation = Formation(members);

        Assert.False(manager.TryStartQuest(Quest("four-before-upgrade"), formation, 1, out var beforeError));
        Assert.Contains("上限は3人", beforeError);
        Assert.Empty(manager.activeQuests);

        var firstUpgrade = db.facilities["fac_party_slot_01"];
        Assert.True(guild.TryBuildFacility(firstUpgrade, out var buildError), buildError);
        Assert.Equal(4, guild.PartyCapacity);
        Assert.True(manager.TryStartQuest(Quest("four-after-upgrade"), formation, 1, out var afterError), afterError);
        Assert.Single(manager.activeQuests);
    }

    [Fact]
    public void FormationArrayLongerThanSixIsRejected()
    {
        var guild = new GuildManager();
        var manager = new QuestManager(guild);
        var member = Assert.Single(AddMembers(guild, 1));
        var formation = new AdventurerData?[GuildManager.MaximumPartyCapacity + 1];
        formation[0] = member;

        Assert.False(manager.TryStartQuest(Quest("seven-slots"), formation, 1, out var error));
        Assert.Contains($"最大{GuildManager.MaximumPartyCapacity}枠", error);
        Assert.Empty(manager.activeQuests);
    }

    [Fact]
    public void SameAdventurerCannotOccupyTwoFormationSlots()
    {
        var guild = new GuildManager();
        var manager = new QuestManager(guild);
        var member = Assert.Single(AddMembers(guild, 1));
        var formation = new AdventurerData?[GuildManager.MaximumPartyCapacity];
        formation[0] = member;
        formation[3] = member;

        Assert.False(manager.TryStartQuest(Quest("duplicate-member"), formation, 1, out var error));
        Assert.Contains("同じ冒険者", error);
        Assert.Empty(manager.activeQuests);
    }

    [Fact]
    public void ARankQuestRecommendsSixMembers()
    {
        const int aRank = 6;
        var members = Enumerable.Range(1, GuildManager.MaximumPartyCapacity)
            .Select(index => Adventurer($"a-rank-{index}", aRank))
            .ToList();
        var quest = Quest("a-rank-recommendation", aRank);

        var assessment = DungeonDifficulty.EvaluateParty(quest, members);

        Assert.Equal("A", Rank.Label(quest.rank));
        Assert.Equal(GuildManager.MaximumPartyCapacity, assessment.RecommendedSize);
    }

    [Fact]
    public void PartySlotFacilityMasterUsesECAUnlockRanks()
    {
        var facilities = PartySlotFacilities(LoadMaster());

        Assert.Collection(
            facilities,
            facility => AssertSlotFacility(facility, "fac_party_slot_01", 2, "E"),
            facility => AssertSlotFacility(facility, "fac_party_slot_02", 4, "C"),
            facility => AssertSlotFacility(facility, "fac_party_slot_03", 6, "A"));
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 4)]
    [InlineData(3, 4)]
    [InlineData(4, 5)]
    [InlineData(5, 5)]
    [InlineData(6, 6)]
    [InlineData(7, 6)]
    public void RankProgressionCeilingMatchesECAUnlocks(int rank, int expectedCapacity)
    {
        Assert.Equal(expectedCapacity, GuildManager.PartyCapacityCeilingForRank(rank));
    }

    [Theory]
    [InlineData("quest_old_city_garrison")]
    [InlineData("quest_promotion_3")]
    public void DRankQuestNeverRecommendsTheStillLockedFifthMember(string questId)
    {
        var quest = LoadMaster().allQuests.Single(candidate => candidate.id == questId);

        var assessment = DungeonDifficulty.EvaluateParty(quest, Array.Empty<AdventurerData>());

        Assert.Equal(3, quest.rank);
        Assert.Equal(4, assessment.RecommendedSize);
    }

    [Fact]
    public void MasterValidatorRejectsNegativePartySlotBonus()
    {
        var db = LoadMaster();
        db.facilities["fac_party_slot_01"].partySlotBonus = -1;

        var errors = MasterValidator.Validate(db);

        Assert.Contains(errors, error =>
            error.Contains("fac_party_slot_01") && error.Contains("0以上"));
    }

    [Fact]
    public void MasterValidatorRejectsPartySlotBonusAboveThreeInTotal()
    {
        var db = LoadMaster();
        db.facilities["invalid_extra_party_slot"] = new FacilityMasterData
        {
            id = "invalid_extra_party_slot",
            displayName = "不正な追加枠",
            partySlotBonus = 1,
        };

        var errors = MasterValidator.Validate(db);

        Assert.Contains(errors, error => error.Contains("強化合計が最大人数を超えています"));
    }

    [Fact]
    public void BuiltPartyCapacityFacilitiesSurviveSaveLoad()
    {
        var db = LoadMaster();
        var guild = new GuildManager(startGold: 10_000, startRank: Rank.Max);
        guild.RestoreFacilities(PartySlotFacilities(db).Take(2));
        var manager = new QuestManager(guild);

        string json = SaveManager.Serialize(
            guild,
            manager,
            currentTurn: 12,
            new List<AdventurerMasterData>());
        var loaded = SaveManager.Deserialize(json, db);

        Assert.Equal(5, loaded.Guild.PartyCapacity);
        Assert.Equal(
            new[] { "fac_party_slot_01", "fac_party_slot_02" },
            loaded.Guild.facilities.Select(facility => facility.id).OrderBy(id => id));
    }

    [Fact]
    public void ExistingSixMemberExpeditionLoadsWithoutBeingTrimmedAtBaseCapacity()
    {
        var db = LoadMaster();
        var guild = new GuildManager(startGold: 1_000, startRank: Rank.Min);
        var members = db.allAdventurers.Take(GuildManager.FormationSlotCount)
            .Select(master => new AdventurerData(master))
            .ToList();
        foreach (var member in members)
            guild.AddAdventurer(member);

        var manager = new QuestManager(guild);
        var run = new QuestRun(
            db.allQuests.Single(quest => quest.id == "quest_slime_cull"),
            startedTurn: 1);
        for (int slot = 0; slot < members.Count; slot++)
            run.formation[slot] = members[slot];
        manager.RestoreState(
            new List<QuestBoardEntry>(),
            new List<QuestRun> { run },
            Array.Empty<string>());

        string json = SaveManager.Serialize(
            guild,
            manager,
            currentTurn: 2,
            new List<AdventurerMasterData>());
        var loaded = SaveManager.Deserialize(json, db);

        Assert.Equal(GuildManager.BasePartyCapacity, loaded.Guild.PartyCapacity);
        var loadedRun = Assert.Single(loaded.QuestManager.activeQuests);
        Assert.Equal(GuildManager.FormationSlotCount, loadedRun.EnumerateMembers().Count());
        Assert.All(
            loadedRun.EnumerateMembers(),
            member => Assert.True(loaded.QuestManager.IsAdventurerBusy(member.id)));

        int phaseBeforeAdvance = loadedRun.currentPhase;
        loaded.QuestManager.AdvanceAll(currentTurn: 3);
        Assert.True(
            loadedRun.currentPhase > phaseBeforeAdvance || !loadedRun.IsInProgress,
            "復元した6人遠征が編成上限によって停止しています");
    }

    static GameMasterData LoadMaster()
    {
        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        return MasterLoader.Load(dataDir);
    }

    static List<FacilityMasterData> PartySlotFacilities(GameMasterData db) =>
        db.facilities.Values
            .Where(facility => facility.partySlotBonus > 0)
            .OrderBy(facility => facility.requiredGuildRank)
            .ThenBy(facility => facility.id)
            .ToList();

    static void AssertSlotFacility(
        FacilityMasterData facility,
        string expectedId,
        int expectedRank,
        string expectedRankLabel)
    {
        Assert.Equal(expectedId, facility.id);
        Assert.Equal(1, facility.partySlotBonus);
        Assert.Equal(expectedRank, facility.requiredGuildRank);
        Assert.Equal(expectedRankLabel, Rank.Label(facility.requiredGuildRank));
    }

    static List<AdventurerData> AddMembers(GuildManager guild, int count)
    {
        var members = Enumerable.Range(1, count)
            .Select(index => Adventurer($"party-capacity-{index}"))
            .ToList();
        foreach (var member in members)
            guild.AddAdventurer(member);
        return members;
    }

    static AdventurerData Adventurer(string id, int rank = Rank.Min) =>
        new(new AdventurerMasterData
        {
            id = id,
            baseName = id,
            defaultLevel = 1,
            defaultRank = rank,
            vitality = 10,
            mental = 10,
            strength = 10,
            agility = 10,
            intelligence = 10,
            constitution = 10,
        });

    static AdventurerData?[] Formation(IReadOnlyList<AdventurerData> members)
    {
        var formation = new AdventurerData?[GuildManager.MaximumPartyCapacity];
        for (int i = 0; i < members.Count; i++)
            formation[i] = members[i];
        return formation;
    }

    static QuestMasterData Quest(string id, int rank = Rank.Min) => new()
    {
        id = id,
        questName = id,
        rank = rank,
        totalPhases = 1,
        phasesPerTurn = 1,
    };
}
