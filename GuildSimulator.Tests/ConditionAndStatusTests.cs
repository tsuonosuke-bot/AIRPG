using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using Xunit;

namespace GuildSimulator.Tests;

[Collection("Guild static state")]
public class ConditionAndStatusTests
{
    [Fact]
    public void CombatStatusesTickModifyStatsAndConsumeStun()
    {
        var adventurer = Adventurer();
        adventurer.CombatHpMax = 100;
        adventurer.CombatHp = 100;
        var tracker = new CombatStatusTracker();
        var logs = new List<string>();

        Assert.True(tracker.Apply(adventurer, Status(CombatStatusType.Poisoned, potency: 10),
            "毒試験", currentRound: 1, logs, phase: 1));
        Assert.True(tracker.Apply(adventurer, Status(CombatStatusType.Guarded, potency: 2),
            "守勢試験", currentRound: 1, logs, phase: 1));
        Assert.True(tracker.Apply(adventurer, Status(CombatStatusType.Stunned),
            "凍結試験", currentRound: 1, logs, phase: 1));

        int downed = tracker.ProcessRoundStart(new IUnitMember?[] { adventurer }, 1, logs, 1);
        var modified = tracker.ApplyStatModifiers(adventurer, new StatBlock { av = 3, mav = 2, dv = 10 });

        Assert.Equal(0, downed);
        Assert.Equal(90, adventurer.CombatHp);
        Assert.Equal(5, modified.av);
        Assert.Equal(4, modified.mav);
        Assert.Equal(12, modified.dv);
        Assert.True(tracker.TryConsumeStun(adventurer, logs, 1));
        Assert.False(tracker.TryConsumeStun(adventurer, logs, 1));
        Assert.Contains(logs, line => line.Contains("毒で10ダメージ"));
        Assert.Contains(logs, line => line.Contains("凍結して行動できない"));
    }

    [Fact]
    public void ExistingWeaponTypesExposeAbnormalStatusesAndBuffs()
    {
        Assert.Equal(CombatStatusType.Burning,
            CombatStatusDefaults.OnHit(Weapon(WeaponType.Fire))?.type);
        Assert.Equal(CombatStatusType.Poisoned,
            CombatStatusDefaults.OnHit(Weapon(WeaponType.Dark))?.type);
        Assert.Equal(CombatStatusType.Stunned,
            CombatStatusDefaults.OnHit(Weapon(WeaponType.Water))?.type);
        Assert.Equal(CombatStatusType.Guarded,
            CombatStatusDefaults.BattleStart(Weapon(WeaponType.Earth))?.type);
        Assert.Equal(CombatStatusType.Empowered,
            CombatStatusDefaults.BattleStart(Weapon(WeaponType.Wind))?.type);
        Assert.Equal(CombatStatusType.Regenerating,
            CombatStatusDefaults.OnHeal(Weapon(WeaponType.Light))?.type);

        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        var db = GuildSimulator.Game.Data.MasterLoader.Load(dataDir);
        Assert.Contains(db.skills["skill_beastFang"].onHitStatuses,
            effect => effect.type == CombatStatusType.Bleeding);
    }

    [Fact]
    public void DamageOverTimeReportsTheAdventurerWhoDealtTheFinalBlow()
    {
        var source = Adventurer();
        var victim = new EnemyData(new EnemyMasterData
        {
            id = "dot_target",
            baseName = "毒の標的",
        })
        {
            CombatHpMax = 10,
            CombatHp = 10,
        };
        var tracker = new CombatStatusTracker();
        var logs = new List<string>();
        IUnitMember? creditedSource = null;
        IUnitMember? defeated = null;

        Assert.True(tracker.Apply(
            victim,
            Status(CombatStatusType.Poisoned, potency: 100),
            "毒試験",
            currentRound: 1,
            logs,
            phase: 1,
            sourceMember: source));

        int downed = tracker.ProcessRoundStart(
            new IUnitMember?[] { victim },
            round: 1,
            logs: logs,
            phase: 1,
            onDowned: (killer, target) =>
            {
                creditedSource = killer;
                defeated = target;
            });

        Assert.Equal(1, downed);
        Assert.Same(source, creditedSource);
        Assert.Same(victim, defeated);
        Assert.False(victim.isAlive);
    }

    [Fact]
    public void WeakerDamageOverTimeRefreshDoesNotStealFinalBlowCredit()
    {
        var strongerSource = Adventurer();
        var weakerSource = Adventurer();
        var victim = new EnemyData(new EnemyMasterData
        {
            id = "dot_refresh_target",
            baseName = "毒の標的",
        })
        {
            CombatHpMax = 10,
            CombatHp = 10,
        };
        var tracker = new CombatStatusTracker();
        var logs = new List<string>();
        IUnitMember? creditedSource = null;

        Assert.True(tracker.Apply(
            victim,
            Status(CombatStatusType.Poisoned, potency: 100),
            "強い毒",
            currentRound: 1,
            logs,
            phase: 1,
            sourceMember: strongerSource));
        Assert.True(tracker.Apply(
            victim,
            Status(CombatStatusType.Poisoned, potency: 50),
            "弱い毒",
            currentRound: 1,
            logs,
            phase: 1,
            sourceMember: weakerSource));

        var active = Assert.Single(tracker.GetActive(victim));
        Assert.Equal(100, active.Potency);
        Assert.Same(strongerSource, active.SourceMember);

        int downed = tracker.ProcessRoundStart(
            new IUnitMember?[] { victim },
            round: 1,
            logs: logs,
            phase: 1,
            onDowned: (killer, _) => creditedSource = killer);

        Assert.Equal(1, downed);
        Assert.Same(strongerSource, creditedSource);
    }

    [Fact]
    public void KnockoutBecomesRecoverableInjuryAndCanLeaveScar()
    {
        var guild = new GuildManager();
        var adventurer = Adventurer();
        guild.AddAdventurer(adventurer);

        adventurer.RegisterKnockout(severity: 2);
        var trauma = adventurer.ResolvePendingTrauma(
            partyWiped: true,
            fatalityReductionPercent: 100);

        Assert.False(trauma.Died);
        Assert.True(adventurer.isAlive);
        Assert.False(adventurer.isIncapacitated);
        var injury = Assert.Single(adventurer.injuries);
        injury.remainingRestTurns = 1;
        injury.scarChancePercent = 100;

        var messages = guild.AdvanceRecovery(currentTurn: 2, canRest: _ => true);

        Assert.Empty(adventurer.injuries);
        Assert.Single(adventurer.scars);
        Assert.Contains(messages, message => message.Contains("回復"));
        Assert.Contains(messages, message => message.Contains("称号"));
    }

    [Fact]
    public void FailedQuestFinalizationResolvesKnockoutWithInfirmaryProtection()
    {
        var guild = new GuildManager();
        guild.RestoreFacilities(new[]
        {
            new FacilityMasterData { id = "infirmary", fatalityReductionPercent = 100 },
        });
        var adventurer = Adventurer();
        guild.AddAdventurer(adventurer);
        adventurer.RegisterKnockout(severity: 3);

        var manager = new QuestManager(guild);
        var run = new QuestRun(new QuestMasterData { id = "failed", questName = "壊滅試験" }, 1)
        {
            failed = true,
        };
        run.formation[0] = adventurer;
        manager.RestoreState(new(), new List<QuestRun> { run }, Array.Empty<string>());

        manager.FinalizeQuest(run);

        Assert.True(adventurer.isAlive);
        Assert.False(adventurer.isIncapacitated);
        Assert.NotEmpty(adventurer.injuries);
        Assert.Contains(run.reportEvents, e => e.kind == ExpeditionEventKind.Injury);
    }

    static CombatStatusApplicationData Status(CombatStatusType type, int potency = 1) => new()
    {
        type = type,
        chancePercent = 100,
        durationRounds = 3,
        potency = potency,
    };

    static EquipmentMasterData Weapon(WeaponType type) => new()
    {
        displayName = type.ToString(),
        type = EquipmentType.Weapon,
        weaponType = type,
    };

    static AdventurerData Adventurer() => new(new AdventurerMasterData
    {
        id = Guid.NewGuid().ToString("N"),
        baseName = "状態試験者",
        defaultLevel = 1,
        defaultRank = 1,
        vitality = 10,
        mental = 10,
        strength = 10,
        agility = 10,
        intelligence = 10,
        constitution = 10,
    });
}
