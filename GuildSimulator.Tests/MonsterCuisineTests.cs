using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

/// <summary>
/// 地下大迷宮の5人と、彼らが分け合うユニークスキル「モンスター食」の回帰テスト。
/// この効果だけは人数ぶん素直に足し合わせないので、畳み込みの規則をここで固定する。
/// </summary>
public class MonsterCuisineTests
{
    const string SkillId = "skill_unique_monsterCuisine";
    static readonly string[] Diners =
    {
        "adv_0003", // マルシル
        "adv_0031", // ライオス
        "adv_0032", // ファリン
        "adv_0033", // チルチャック
        "adv_0034", // センシ
    };

    static GameMasterData Load() => MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    static int Talent(AdventurerMasterData a) =>
        a.vitality + a.mental + a.strength + a.agility + a.intelligence
        + a.constitution + a.appearance
        - (a.defaultLevel - 1) * AdventurerData.StatPointsPerLevel;

    static AdventurerData Diner(GameMasterData db, string id) =>
        new(db.allAdventurers.Single(a => a.id == id));

    [Fact]
    public void TheFivePartyMembersAllSitAtTheSameERankTable()
    {
        var db = Load();
        foreach (string id in Diners)
        {
            var master = db.allAdventurers.Single(a => a.id == id);
            Assert.Equal(2, master.defaultRank);       // E
            Assert.Equal(2, master.recruitGuildRank);
            Assert.InRange(master.defaultLevel, 6, 10);
            Assert.Contains(SkillId, master.skillIds);

            // 全員が帯より1段上のレアリティなので、素質は上限の70で揃う。
            Assert.Equal(70, Talent(master));
            Assert.True(master.recruitWeight < 60,
                $"{master.baseName} は帯の既定60より出にくくする");

            // 種族と職業の組み合わせが成立していること。
            Assert.NotNull(master.Race);
            Assert.Contains(master.defaultClassId, master.Race!.allowedClassIds);
        }

        // 固有スキルなので、職業マスタリーとしては誰も解禁できない。
        Assert.DoesNotContain(db.classes.Values.SelectMany(c => c.classSkills), e => e.skillId == SkillId);
    }

    [Fact]
    public void TheMealGrowsWithEveryCompanionWhoKnowsIt()
    {
        var db = Load();
        var solo = Diner(db, "adv_0031");
        Assert.Equal(5, PartySkillEffects.Of(new[] { solo }).PostBattleHealPercent);

        // 5人そろうと 5% + 4人ぶんの上乗せ = 9%。足し算なら25%になるので、そうなっていないこと。
        var full = Diners.Select(id => Diner(db, id)).ToArray();
        Assert.Equal(9, PartySkillEffects.Of(full).PostBattleHealPercent);

        // 持っていない隊員は食卓の人数に数えない。
        var outsider = Diner(db, "adv_0025"); // エルシャ
        Assert.Equal(5, PartySkillEffects.Of(new[] { solo, outsider }).PostBattleHealPercent);
        Assert.Equal(0, PartySkillEffects.Of(new[] { outsider }).PostBattleHealPercent);
    }

    [Fact]
    public void TheHealIsAShareOfMaxHpAndNeverRoundsDownToNothing()
    {
        var db = Load();
        var effects = PartySkillEffects.Of(new[] { Diner(db, "adv_0034") });

        Assert.Equal(5, effects.PostBattleHealFor(100));
        Assert.Equal(10, effects.PostBattleHealFor(200));
        // 端数は切り上げ。効いているのに0を返すと「食べたのに何も起きない」になる。
        Assert.Equal(1, effects.PostBattleHealFor(1));
        Assert.Equal(0, PartySkillEffects.None.PostBattleHealFor(1_000));
    }

    [Fact]
    public void ManyCooksStillCannotOutgrowTheCap()
    {
        var greedy = new SkillMasterData
        {
            id = "test_feast",
            skillName = "試験の大盤振る舞い",
            expedition = { postBattleHealPercent = 20, postBattleHealPerCompanionPercent = 20 },
        };
        var party = Enumerable.Range(0, 6)
            .Select(i =>
            {
                var a = new AdventurerData(new AdventurerMasterData { id = $"cook{i}", baseName = $"cook{i}" });
                a.LearnPermanentSkill(greedy);
                return a;
            })
            .ToArray();

        Assert.Equal(
            PartySkillEffects.MaxPostBattleHealPercent,
            PartySkillEffects.Of(party).PostBattleHealPercent);
    }

    [Fact]
    public void WinningAFightFeedsTheSurvivorsInAnActualQuest()
    {
        var db = Load();
        var cook = Diner(db, "adv_0034"); // センシ

        // 噛みつかない案山子1体。勝ちは確実で、削られないぶん回復量をそのまま観測できる。
        var scarecrow = new EnemyMasterData
        {
            id = "test_scarecrow", baseName = "案山子",
            vitality = 1, mental = 1, strength = 1, agility = 1,
            intelligence = 1, constitution = 1,
            naturalPv = 0, naturalDamageDice = "1d1-1",
        };
        var quest = new QuestMasterData
        {
            // エリアを2つにして1ターンでは終わらせない。帰還処理まで進むとHPが
            // 精算されてしまい、戦闘直後にいくら戻ったのかが読めなくなる。
            id = "q", questName = "試験", totalPhases = 2, phasesPerTurn = 1,
            fixedEvents = { new QuestPhaseEvent { phase = 1, type = QuestEventType.ForceEnemyEncounter } },
            Dungeon = new DungeonMasterData
            {
                encounterTable =
                {
                    new EncounterEntry
                    {
                        unitId = "test_unit", weight = 1,
                        Unit = new EnemyUnitTemplate
                        {
                            id = "test_unit", unitName = "案山子",
                            Formation = { scarecrow },
                        },
                    },
                },
            },
        };

        var manager = new QuestManager(new GuildManager(startGold: 100));
        var formation = new AdventurerData?[6];
        formation[0] = cook;
        Assert.True(manager.TryStartQuest(quest, formation, 1, out _));

        // 戦闘前に半分まで削っておく。満タンだと回復しても何も動かず、通ったか分からない。
        int maxHp = cook.CombatHpMax;
        cook.CombatHp = maxHp / 2;
        manager.AdvanceAll(2);

        var run = manager.activeQuests.Single();
        Assert.Contains(run.logs, line => line.Contains("戦利品を捌いて腹を満たした"));
        Assert.Equal(maxHp / 2 + PartySkillEffects.Of(new[] { cook }).PostBattleHealFor(maxHp), cook.CombatHp);
    }
}
