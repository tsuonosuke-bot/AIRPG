using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

/// <summary>斥候術から反撃まで、条件発動型スキル8種の回帰テスト。</summary>
public class ExtendedSkillEffectTests
{
    static GameMasterData Load() => MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    static AdventurerData Adventurer(string name, int maxHp = 1_000, int? currentHp = null) =>
        new(new AdventurerMasterData
        {
            id = name,
            baseName = name,
            vitality = 20,
            mental = 20,
            strength = 30,
            agility = 30,
            intelligence = 30,
            constitution = 20,
        })
        {
            CombatHpMax = maxHp,
            CombatHp = currentHp ?? maxHp,
        };

    static EnemyData Dummy(string name = "案山子", int hp = 1_000_000) =>
        new(new EnemyMasterData
        {
            id = name,
            baseName = name,
            vitality = 10,
            mental = 10,
            strength = 1,
            agility = 1,
            intelligence = 1,
            constitution = 8,
            naturalPv = 0,
            naturalDamageDice = "1d1-1",
        })
        {
            CombatHpMax = hp,
            CombatHp = hp,
        };

    static IUnitMember?[] Side(params IUnitMember?[] members)
    {
        var side = new IUnitMember?[6];
        for (int i = 0; i < members.Length && i < side.Length; i++) side[i] = members[i];
        return side;
    }

    static List<string> Fight(IUnitMember?[] allies, IUnitMember?[] enemies)
    {
        var logs = new List<string>();
        BattleResolver.Resolve(
            allies, enemies, logs, turn: 1, phase: 1, new MoraleState(1_000_000));
        return logs;
    }

    [Fact]
    public void AllEightSkillsLoadAndHaveAClassUnlock()
    {
        var db = Load();
        string[] ids =
        {
            "skill_scouting", "skill_campcraft", "skill_butchery", "skill_protection",
            "skill_executioner", "skill_purification", "skill_backsToWall", "skill_counter",
        };

        foreach (string id in ids)
        {
            Assert.True(db.skills.ContainsKey(id), $"{id} が読み込まれていない");
            Assert.Contains(db.classes.Values.SelectMany(c => c.classSkills), entry => entry.skillId == id);
        }
    }

    [Fact]
    public void ScoutingCampcraftAndButcheryChangeExpeditionCalculations()
    {
        var db = Load();
        var scout = Adventurer("遠征係");
        scout.LearnPermanentSkill(db.skills["skill_scouting"]);
        scout.LearnPermanentSkill(db.skills["skill_campcraft"]);
        scout.LearnPermanentSkill(db.skills["skill_butchery"]);

        var effects = PartySkillEffects.Of(new[] { scout });
        Assert.Equal(0.75f, effects.ChanceMultiplierFor(DungeonEventType.EnemyEncounter), 3);
        Assert.Equal(1.4f, effects.ChanceMultiplierFor(DungeonEventType.Heal), 3);
        Assert.Equal(1.2f, effects.RestHealMultiplier, 3);

        var common = new RewardEntryData
        {
            type = RewardType.Equipment,
            chance = 0.1f,
            Equipment = new EquipmentMasterData { rarity = Rarity.Common },
        };
        var rare = new RewardEntryData
        {
            type = RewardType.Equipment,
            chance = 0.1f,
            Equipment = new EquipmentMasterData { rarity = Rarity.Rare },
        };
        Assert.Equal(0.13f, effects.EnemyDropChanceFor(common), 3);
        Assert.Equal(0.182f, effects.EnemyDropChanceFor(rare), 3);
    }

    [Fact]
    public void CampcraftRaisesActualRestHealing()
    {
        var db = Load();
        int plain = RestOnce(Adventurer("通常", 100, 1));
        var camper = Adventurer("野営係", 100, 1);
        camper.LearnPermanentSkill(db.skills["skill_campcraft"]);
        int skilled = RestOnce(camper);

        Assert.Equal(50, plain);
        Assert.Equal(60, skilled);
    }

    static int RestOnce(AdventurerData adventurer)
    {
        var quest = new QuestMasterData
        {
            id = "rest_test",
            totalPhases = 1,
            fixedEvents = { new QuestPhaseEvent { phase = 1, type = QuestEventType.ForceHeal } },
        };
        var run = new QuestRun(quest, startedTurn: 1) { morale = new MoraleState(100) };
        run.formation[0] = adventurer;
        int before = adventurer.CombatHp;
        new QuestProgressor().AdvanceOnePhase(run, currentTurn: 1);
        return adventurer.CombatHp - before;
    }

    [Fact]
    public void ProtectionRedirectsAnAttackFromACriticalAlly()
    {
        var db = Load();
        var protector = Adventurer("守護役", 100_000);
        protector.SetEquipped(EquipSlot.RightHand, db.equipment["eq_sword_01"]);
        protector.SetEquipped(EquipSlot.LeftHand, db.equipment["eq_towershield_02"]);
        protector.LearnPermanentSkill(db.skills["skill_protection"]);

        var critical = Adventurer("瀕死役", 100, 10);
        critical.LearnPermanentSkill(new SkillMasterData
        {
            id = "test_loud",
            skillName = "目立つ",
            add = new StatBlock { threatWeight = 1_000 },
        });

        var logs = Fight(Side(protector, critical), Side(Dummy()));
        Assert.Contains(logs, line => line.Contains("守護役が瀕死役を庇った") && line.Contains("庇護"));
    }

    [Fact]
    public void ExecutionerAndBacksToWallAddPvOnlyWhenTheirConditionsAreMet()
    {
        var db = Load();
        var attacker = Adventurer("追い込み役", 100, 30);
        attacker.SetEquipped(EquipSlot.RightHand, db.equipment["eq_sword_01"]);
        attacker.LearnPermanentSkill(db.skills["skill_executioner"]);
        attacker.LearnPermanentSkill(db.skills["skill_backsToWall"]);
        attacker.LearnPermanentSkill(new SkillMasterData
        {
            id = "test_bleed",
            skillName = "試験出血",
            onHitStatuses =
            {
                new CombatStatusApplicationData
                {
                    type = CombatStatusType.Bleeding,
                    target = CombatStatusTarget.Enemy,
                    chancePercent = 100,
                    durationRounds = 99,
                    potency = 1,
                },
            },
        });

        var logs = Fight(Side(attacker), Side(Dummy()));
        Assert.Contains(logs, line => line.Contains("背水 PV+3"));
        Assert.Contains(logs, line => line.Contains("処刑人+背水 PV+5"));
    }

    [Fact]
    public void PurificationRemovesOneHarmfulStatusAfterHealing()
    {
        var db = Load();
        var patient = Adventurer("患者", 100, 20);
        patient.LearnPermanentSkill(new SkillMasterData
        {
            id = "test_poisoned",
            skillName = "試験毒",
            battleStartStatuses =
            {
                new CombatStatusApplicationData
                {
                    type = CombatStatusType.Poisoned,
                    target = CombatStatusTarget.Self,
                    chancePercent = 100,
                    durationRounds = 99,
                    potency = 1,
                },
            },
        });

        var healer = Adventurer("浄化役", 1_000);
        var healWeapon = db.equipment.Values.First(item => item.IsHealWeapon);
        healer.SetEquipped(EquipSlot.RightHand, healWeapon);
        healer.LearnPermanentSkill(db.skills["skill_purification"]);

        var logs = Fight(Side(patient, healer), Side(Dummy()));
        Assert.Contains(logs, line => line.Contains("患者 に毒を付与"));
        Assert.Contains(logs, line => line.Contains("浄化役→患者")
            && (line.Contains("回復") || line.Contains("治療")));
        Assert.Contains(logs, line => line.Contains("患者 の毒を浄化した") && line.Contains("浄化"));
    }

    [Fact]
    public void CounterattacksAfterAShieldFullyStopsTheHit()
    {
        var db = Load();
        var defender = Adventurer("反撃役", 100_000);
        defender.SetEquipped(EquipSlot.RightHand, db.equipment["eq_sword_01"]);
        defender.SetEquipped(EquipSlot.LeftHand, db.equipment["eq_towershield_02"]);
        var counter = db.skills["skill_counter"];
        counter.battle.counterChancePercent = 100;
        counter.add.blockChance = 100;
        counter.add.blockNegate = 100;
        defender.LearnPermanentSkill(counter);

        var logs = Fight(Side(defender), Side(Dummy()));
        Assert.Contains(logs, line => line.Contains("完全に受けきった"));
        Assert.Contains(logs, line => line.Contains("反撃役→案山子 反撃"));
    }
}
