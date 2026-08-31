using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

/// <summary>
/// 隊商人の固有性である「行軍」（幌馬車）の検証。
/// 速くなるのは1ターンに踏み込むエリア数だけで、踏むエリアの総数は変えない
/// ——道中の出来事を飛ばして報酬だけ持ち帰る抜け道にはしない、というのが要点。
/// </summary>
public class CaravanMarchTests
{
    static GameMasterData Load() => MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    static AdventurerData Adventurer(string name) =>
        new(new AdventurerMasterData
        {
            id = name,
            baseName = name,
            defaultLevel = 1,
            defaultRank = 1,
            vitality = 10,
            mental = 10,
            strength = 10,
            agility = 10,
            intelligence = 10,
            constitution = 10,
            appearance = 10,
        });

    static SkillMasterData Wagon(int phases) => new()
    {
        id = $"wagon{phases}",
        skillName = $"試験用馬車{phases}",
        expedition = new SkillExpeditionEffect { phasesPerTurnBonus = phases },
    };

    [Fact]
    public void MarchSkillsStackAcrossThePartyAndRaisePhasesPerTurn()
    {
        var quest = new QuestMasterData { id = "q", totalPhases = 40, phasesPerTurn = 5 };

        var driver = Adventurer("御者");
        driver.LearnPermanentSkill(Wagon(1));
        var second = Adventurer("荷馬");
        second.LearnPermanentSkill(Wagon(2));

        Assert.Equal(5, PartySkillEffects.None.PhasesPerTurnFor(quest));
        Assert.Equal(6, PartySkillEffects.Of(new[] { driver }).PhasesPerTurnFor(quest));
        Assert.Equal(8, PartySkillEffects.Of(new[] { driver, second }).PhasesPerTurnFor(quest));
    }

    [Fact]
    public void MarchBonusIsCappedSoALogisticsOnlyPartyCannotOutrunTheGame()
    {
        var quest = new QuestMasterData { id = "q", totalPhases = 40, phasesPerTurn = 5 };
        var party = Enumerable.Range(0, 6).Select(i =>
        {
            var a = Adventurer($"御者{i}");
            a.LearnPermanentSkill(Wagon(3));
            return a;
        }).ToArray();

        var effects = PartySkillEffects.Of(party);
        Assert.Equal(PartySkillEffects.MaxPhasesPerTurnBonus, effects.phasesPerTurnBonus);
        Assert.Equal(quest.phasesPerTurn + PartySkillEffects.MaxPhasesPerTurnBonus,
            effects.PhasesPerTurnFor(quest));
    }

    [Fact]
    public void ThePartyAlwaysCoversAtLeastOneAreaPerTurn()
    {
        var quest = new QuestMasterData { id = "q", totalPhases = 40, phasesPerTurn = 2 };
        var stuck = Adventurer("重荷");
        stuck.LearnPermanentSkill(Wagon(-9));

        Assert.Equal(1, PartySkillEffects.Of(new[] { stuck }).PhasesPerTurnFor(quest));
    }

    [Fact]
    public void AWagonShortensTheExpeditionWithoutSkippingAnyArea()
    {
        var quest = new QuestMasterData
        {
            id = "q",
            totalPhases = 40,
            phasesPerTurn = 5,
            Dungeon = new DungeonMasterData(),
        };

        Assert.Equal(8, PartySkillEffects.None.EstimatedTurnsFor(quest));

        var plain = RunOneTurn(quest, wagon: null);
        var wagon = RunOneTurn(quest, wagon: Wagon(2));

        Assert.Equal(5, plain.currentPhase);
        Assert.Equal(7, wagon.currentPhase);
        // 総エリア数は据え置き。速いのは行軍だけで、道中が短くなるわけではない。
        Assert.Equal(quest.totalPhases, plain.PhaseLimit);
        Assert.Equal(quest.totalPhases, wagon.PhaseLimit);
    }

    static QuestRun RunOneTurn(QuestMasterData quest, SkillMasterData? wagon)
    {
        var guild = new GuildManager(startGold: 100);
        var manager = new QuestManager(guild);
        var member = Adventurer("隊員");
        if (wagon != null) member.LearnPermanentSkill(wagon);
        var formation = new AdventurerData?[6];
        formation[0] = member;
        Assert.True(manager.TryStartQuest(quest, formation, 1, out _));
        manager.AdvanceAll(2);
        return manager.activeQuests.Single();
    }

    [Fact]
    public void TheCaravanerClassIsTheOneThatLearnsTheWagon()
    {
        var db = Load();
        var caravaner = db.classes["class_Caravaner"];

        var wagonEntries = caravaner.classSkills
            .Where(entry => entry.Skill?.family == "wagon")
            .OrderBy(entry => entry.requiredClearCount)
            .ToList();

        Assert.Equal(2, wagonEntries.Count);
        Assert.All(wagonEntries, entry => Assert.True(entry.Skill!.expedition.phasesPerTurnBonus > 0));
        // 就いた瞬間に手に入ると、転職で覚えるだけ覚えて戻る抜き取りができてしまう。
        Assert.All(wagonEntries, entry => Assert.True(entry.requiredClearCount > 0));

        var others = db.classes.Values
            .Where(cls => cls.id != "class_Caravaner")
            .SelectMany(cls => cls.classSkills)
            .Where(entry => entry.Skill?.family == "wagon");
        Assert.Empty(others);
    }
}
