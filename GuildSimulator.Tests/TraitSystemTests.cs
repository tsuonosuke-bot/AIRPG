using GuildSimulator.Core;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Cli;
using GuildSimulator.Game.Data;
using GuildSimulator.Game.Presentation;
using GuildSimulator.Game.Screens;
using Xunit;

namespace GuildSimulator.Tests;

/// <summary>
/// 特性 —— 遠征での戦い方から生える恒久的な変化。
///
/// 守るべき芯は「<b>代償は先払い</b>」。特性は原則として諸刃であり、欠点のない素直な強化は
/// リスク記録（瀕死・戦闘不能・仲間の死線）を潜った者にしか現れない。
/// 数値をいじるだけでこの原則が崩れないよう、マスタ全体をここで検査する。
/// </summary>
public class TraitSystemTests
{
    static GameMasterData Load() => MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    static AdventurerMasterData Master(string id = "adv", string name = "測定用") => new()
    {
        id = id, baseName = name,
        vitality = 12, mental = 12, strength = 14, agility = 12,
        intelligence = 10, constitution = 12,
    };

    static TraitMasterData Trait(
        string id, SkillMasterData skill, params (ExpeditionRecordType record, int atLeast)[] reqs)
    {
        var trait = new TraitMasterData
        {
            id = id, traitName = id, skillId = skill.id, Skill = skill,
        };
        foreach (var (record, atLeast) in reqs)
            trait.requirements.Add(new TraitRequirementData { record = record, atLeast = atLeast });
        return trait;
    }

    // ---- マスタデータの設計規則 ----

    [Fact]
    public void PureUpgradeTraitsAlwaysCostRiskUpFront()
    {
        var db = Load();
        Assert.NotEmpty(db.traits);

        var freeLunch = db.traits.Values
            .Where(t => t.Skill != null && t.IsPureUpgrade && !t.RequiresRisk)
            .Select(t => t.id)
            .ToList();

        Assert.True(freeLunch.Count == 0,
            "欠点がないのにリスク記録を要求していない特性: " + string.Join(", ", freeLunch));
    }

    [Fact]
    public void EveryTraitIsActuallyReachable()
    {
        var db = Load();
        foreach (var trait in db.traits.Values)
        {
            Assert.NotNull(trait.Skill);
            Assert.NotEmpty(trait.requirements);
            Assert.All(trait.requirements, r => Assert.True(r.atLeast >= 1, $"{trait.id}: atLeastが0以下"));
        }
    }

    [Fact]
    public void TraitFamiliesNeverCollideWithClassMasteries()
    {
        // 同じ family は最上位1つしか効かない。職業マスタリーと family を共有すると、
        // 特性がマスタリーを黙って押しのけてしまう。
        var db = Load();
        var traitSkillIds = db.traits.Values.Select(t => t.skillId).ToHashSet();

        foreach (var trait in db.traits.Values)
        {
            string family = trait.Skill!.family;
            Assert.False(string.IsNullOrWhiteSpace(family), $"{trait.id}: familyが空");

            var sharers = db.skills.Values
                .Where(s => s.family == family && !traitSkillIds.Contains(s.id))
                .Select(s => s.id)
                .ToList();
            Assert.True(sharers.Count == 0,
                $"{trait.id}: family '{family}' を特性以外と共有: {string.Join(", ", sharers)}");
        }
    }

    [Fact]
    public void MasterDataHasBothDoubleEdgedAndRiskEarnedTraits()
    {
        // 諸刃だけでも純粋強化だけでも設計が成り立たない。両方あることを固定しておく。
        var db = Load();
        Assert.Contains(db.traits.Values, t => !t.IsPureUpgrade);
        Assert.Contains(db.traits.Values, t => t.IsPureUpgrade && t.RequiresRisk);
    }

    [Fact]
    public void ValidatorRejectsAPureUpgradeThatDemandsNoRisk()
    {
        var db = Load();
        var freebie = new SkillMasterData
        {
            id = "skill_trait_freebie", skillName = "ただ飯",
            family = "trait_freebie", level = 1,
            add = new StatBlock { pv = 3 },
        };
        db.skills[freebie.id] = freebie;
        db.traits["trait_freebie"] = Trait(
            "trait_freebie", freebie, (ExpeditionRecordType.Kills, 5));

        var errors = MasterValidator.Validate(db);
        Assert.Contains(errors, e => e.Contains("trait_freebie") && e.Contains("リスク記録"));
    }

    [Fact]
    public void ValidatorAcceptsAPureUpgradePaidForWithRisk()
    {
        var db = Load();
        // どの型でも意味が通る数値にする。物理専用の数値で全型向けに宣言すると、
        // 「術者には利点がない」で別のエラーになってしまう。
        var earned = new SkillMasterData
        {
            id = "skill_trait_earned", skillName = "身銭を切った",
            family = "trait_earned", level = 1,
            add = new StatBlock { emergencyHeal = 6 },
        };
        db.skills[earned.id] = earned;
        db.traits["trait_earned"] = Trait(
            "trait_earned", earned, (ExpeditionRecordType.NearDeathRounds, 5));

        var errors = MasterValidator.Validate(db);
        Assert.DoesNotContain(errors, e => e.Contains("trait_earned"));
    }

    [Fact]
    public void DrawbacksAreReadFromTheNumbersNotFromAFlag()
    {
        var threatened = new SkillMasterData
        {
            id = "s1", add = new StatBlock { critPv = 1, threatWeight = 25 },
        };
        Assert.NotEmpty(TraitAnalysis.Evaluate(threatened, null).Drawbacks);

        var weakened = new SkillMasterData
        {
            id = "s2", add = new StatBlock { pv = 1 },
            mul = new StatMultiplier { hp = 1f, san = 0.9f, heal = 1f },
        };
        Assert.NotEmpty(TraitAnalysis.Evaluate(weakened, null).Drawbacks);

        var clean = new SkillMasterData { id = "s3", add = new StatBlock { emergencyHeal = 8 } };
        Assert.Empty(TraitAnalysis.Evaluate(clean, null).Drawbacks);
    }

    // ---- 担い手の型 ----

    [Fact]
    public void PhysicalOnlyNumbersAreDeadWeightForCasters()
    {
        // pv・背水PV・弱った敵へのPV は魔法攻撃の経路に乗らない（BattleResolver が magic で分岐する）。
        var brawn = new SkillMasterData
        {
            id = "s", add = new StatBlock { pv = 2 },
            battle = new SkillBattleEffect { lowHpThresholdPercent = 50, lowHpPv = 2 },
        };

        Assert.NotEmpty(TraitAnalysis.Evaluate(brawn, TraitLens.Physical).Benefits);
        Assert.Empty(TraitAnalysis.Evaluate(brawn, TraitLens.Magic).Benefits);
        Assert.Empty(TraitAnalysis.Evaluate(brawn, TraitLens.Heal).Benefits);
    }

    [Fact]
    public void ADrawbackThatCostsOneBuildNothingIsNoDrawbackForThatBuild()
    {
        // これが「引き際を知る」で起きていた穴。術者にとって PV-1 はタダなので、
        // リスクを払わない純粋強化が成立してしまっていた。
        var free = new SkillMasterData { id = "s", add = new StatBlock { dv = 1, pv = -1 } };

        Assert.NotEmpty(TraitAnalysis.Evaluate(free, TraitLens.Physical).Drawbacks);
        Assert.Empty(TraitAnalysis.Evaluate(free, TraitLens.Magic).Drawbacks);
        Assert.NotEmpty(TraitAnalysis.Evaluate(free, TraitLens.Magic).Benefits);
    }

    [Fact]
    public void ValidatorRejectsATraitThatIsAllCostForOneOfItsBuilds()
    {
        var db = Load();
        var lopsided = new SkillMasterData
        {
            id = "skill_trait_lopsided", skillName = "片手落ち",
            family = "trait_lopsided", level = 1,
            add = new StatBlock { pv = 2 },
            mul = new StatMultiplier { hp = 1f, san = 0.9f, heal = 1f },
        };
        db.skills[lopsided.id] = lopsided;
        var trait = Trait("trait_lopsided", lopsided, (ExpeditionRecordType.Kills, 5));
        trait.builds.Add(TraitLens.Physical);
        trait.builds.Add(TraitLens.Magic);   // 術者には士気の代償だけが残る
        db.traits[trait.id] = trait;

        var errors = MasterValidator.Validate(db);
        Assert.Contains(errors, e => e.Contains("trait_lopsided") && e.Contains("魔法型")
            && e.Contains("利点が1つも残りません"));
    }

    [Fact]
    public void ValidatorRejectsAFreeLunchForOneOfItsBuilds()
    {
        var db = Load();
        var free = new SkillMasterData
        {
            id = "skill_trait_freeride", skillName = "ただ乗り",
            family = "trait_freeride", level = 1,
            add = new StatBlock { dv = 1, pv = -1 },
        };
        db.skills[free.id] = free;
        var trait = Trait("trait_freeride", free, (ExpeditionRecordType.Retreats, 4));
        trait.builds.Add(TraitLens.Physical);
        trait.builds.Add(TraitLens.Magic);   // 術者には PV-1 がタダ
        db.traits[trait.id] = trait;

        var errors = MasterValidator.Validate(db);
        Assert.Contains(errors, e => e.Contains("trait_freeride") && e.Contains("魔法型")
            && e.Contains("代償がない"));
    }

    [Fact]
    public void OffersRespectWhatTheAdventurerIsHolding()
    {
        var physicalSkill = new SkillMasterData { id = "sp", family = "fp", level = 1 };
        var magicSkill = new SkillMasterData { id = "sm", family = "fm", level = 1 };
        var physical = Trait("t_phys", physicalSkill, (ExpeditionRecordType.Kills, 1));
        physical.builds.Add(TraitLens.Physical);
        var magic = Trait("t_magic", magicSkill, (ExpeditionRecordType.Kills, 1));
        magic.builds.Add(TraitLens.Magic);
        var both = new[] { physical, magic };

        var caster = new AdventurerData(Master());
        caster.records.Add(ExpeditionRecordType.Kills, 10);
        caster.SetEquipped(EquipSlot.RightHand, new EquipmentMasterData
        {
            id = "staff", displayName = "杖", attackKind = AttackKind.Magic, isTwoHanded = true,
        });

        var offer = Assert.Single(TraitSystem.BuildOffers(new[] { caster }, both));
        Assert.Equal(TraitLens.Magic, TraitSystem.LensOf(caster));
        Assert.Same(magic, Assert.Single(offer.Candidates));
    }

    [Fact]
    public void EveryBuildHasSomethingToEarn()
    {
        // どの型にも特性が用意されていること。術者や回復役が育成から取り残されないための下限。
        var db = Load();
        foreach (var lens in TraitAnalysis.AllLenses)
        {
            var forLens = db.traits.Values.Where(t => t.Builds.Contains(lens)).ToList();
            Assert.True(forLens.Count >= 3,
                $"{TraitAnalysis.LensName(lens)}型の特性が{forLens.Count}件しかありません");
            Assert.Contains(forLens, t => t.IsPureUpgrade && t.RequiresRisk);
        }
    }

    // ---- 開花の判定 ----

    [Fact]
    public void ATraitStaysHiddenUntilTheRecordReachesItsThreshold()
    {
        var skill = new SkillMasterData { id = "s", family = "f", level = 1 };
        var trait = Trait("t", skill, (ExpeditionRecordType.CritKills, 5));
        var adv = new AdventurerData(Master());

        adv.records.Add(ExpeditionRecordType.CritKills, 4);
        Assert.Empty(TraitSystem.BuildOffers(new[] { adv }, new[] { trait }));

        adv.records.Add(ExpeditionRecordType.CritKills, 1);
        var offers = TraitSystem.BuildOffers(new[] { adv }, new[] { trait });
        Assert.Single(offers);
        Assert.Contains(trait, offers[0].Candidates);
    }

    [Fact]
    public void ThreeBossFinishesOfferThreeDistinctTraits()
    {
        var db = Load();
        var adventurer = new AdventurerData(Master());
        adventurer.records.Add(ExpeditionRecordType.BossKills, 2);

        Assert.Empty(TraitSystem.BuildOffers(new[] { adventurer }, db.traits.Values));

        adventurer.records.Add(ExpeditionRecordType.BossKills);
        var offer = Assert.Single(TraitSystem.BuildOffers(
            new[] { adventurer }, db.traits.Values));
        var candidates = offer.Candidates.Select(trait => trait.id).ToHashSet();

        Assert.Equal(3, candidates.Count);
        Assert.True(candidates.SetEquals(
            new[]
            {
                "trait_renown",
                "trait_boss_footwork",
                "trait_trophy_eye",
            }));
        Assert.All(offer.Candidates, trait =>
        {
            Assert.Contains(trait.requirements, requirement =>
                requirement.record == ExpeditionRecordType.BossKills
                && requirement.atLeast == 3);
            Assert.All(TraitAnalysis.AllLenses, lens => Assert.Contains(lens, trait.Builds));
        });
    }

    [Fact]
    public void EveryRequirementMustBeMetNotJustOne()
    {
        var skill = new SkillMasterData { id = "s", family = "f", level = 1 };
        var trait = Trait("t", skill,
            (ExpeditionRecordType.ProtectedAlly, 5),
            (ExpeditionRecordType.NearDeathRounds, 5));
        var adv = new AdventurerData(Master());

        adv.records.Add(ExpeditionRecordType.ProtectedAlly, 20);
        Assert.Empty(TraitSystem.BuildOffers(new[] { adv }, new[] { trait }));

        adv.records.Add(ExpeditionRecordType.NearDeathRounds, 5);
        Assert.Single(TraitSystem.BuildOffers(new[] { adv }, new[] { trait }));
    }

    [Fact]
    public void AcceptingATraitGrantsItsSkillPermanently()
    {
        var skill = new SkillMasterData
        {
            id = "s", skillName = "不屈", family = "f", level = 1,
            add = new StatBlock { emergencyHeal = 8 },
        };
        var trait = Trait("t", skill, (ExpeditionRecordType.TimesDowned, 2));
        var adv = new AdventurerData(Master());
        adv.records.Add(ExpeditionRecordType.TimesDowned, 2);

        var offer = Assert.Single(TraitSystem.BuildOffers(new[] { adv }, new[] { trait }));
        TraitSystem.Accept(offer, trait);

        Assert.Contains(skill, adv.AllLearnedSkills);

        // スキル補正が乗るのは UnitCalculator を通したとき。実際に戦闘値へ届くことまで見る。
        var stats = UnitCalculator
            .CalcPerMember(new IUnitMember?[] { adv }, isAllySide: true)
            .Single().stats;
        Assert.Equal(8, stats.emergencyHeal);
    }

    [Fact]
    public void ATraitIsOnlyEverOfferedOnceWhetherTakenOrDeclined()
    {
        var taken = new SkillMasterData { id = "s1", family = "f1", level = 1 };
        var passed = new SkillMasterData { id = "s2", family = "f2", level = 1 };
        var traits = new[]
        {
            Trait("t1", taken, (ExpeditionRecordType.Kills, 1)),
            Trait("t2", passed, (ExpeditionRecordType.Kills, 1)),
        };

        var adv = new AdventurerData(Master());
        adv.records.Add(ExpeditionRecordType.Kills, 10);

        var offer = Assert.Single(TraitSystem.BuildOffers(new[] { adv }, traits));
        Assert.Equal(2, offer.Candidates.Count);
        TraitSystem.Accept(offer, traits[0]);

        // 選ばなかった候補も提示済みになる。同じ問いを毎クエスト繰り返さないため。
        Assert.Empty(TraitSystem.BuildOffers(new[] { adv }, traits));
        Assert.DoesNotContain(passed, adv.AllLearnedSkills);
    }

    [Fact]
    public void DecliningForfeitsTheWholeOffer()
    {
        var skill = new SkillMasterData { id = "s", family = "f", level = 1 };
        var trait = Trait("t", skill, (ExpeditionRecordType.LowHpRounds, 3));
        var adv = new AdventurerData(Master());
        adv.records.Add(ExpeditionRecordType.LowHpRounds, 3);

        var offer = Assert.Single(TraitSystem.BuildOffers(new[] { adv }, new[] { trait }));
        TraitSystem.Decline(offer);

        Assert.DoesNotContain(skill, adv.AllLearnedSkills);
        Assert.Empty(TraitSystem.BuildOffers(new[] { adv }, new[] { trait }));
    }

    [Fact]
    public void RiskEarnedTraitsAreShownFirst()
    {
        var ordinary = new SkillMasterData { id = "s1", family = "f1", level = 1 };
        var earned = new SkillMasterData { id = "s2", family = "f2", level = 1 };
        var traits = new[]
        {
            Trait("t_ordinary", ordinary, (ExpeditionRecordType.Kills, 1)),
            Trait("t_earned", earned, (ExpeditionRecordType.NearDeathRounds, 1)),
        };

        var adv = new AdventurerData(Master());
        adv.records.Add(ExpeditionRecordType.Kills, 50);
        adv.records.Add(ExpeditionRecordType.NearDeathRounds, 1);

        var offer = Assert.Single(TraitSystem.BuildOffers(new[] { adv }, traits));
        Assert.Equal("t_earned", offer.Candidates[0].id);
    }

    [Fact]
    public void TheDeadAreOfferedNothing()
    {
        var skill = new SkillMasterData { id = "s", family = "f", level = 1 };
        var trait = Trait("t", skill, (ExpeditionRecordType.Kills, 1));
        var adv = new AdventurerData(Master()) { isAlive = false };
        adv.records.Add(ExpeditionRecordType.Kills, 10);

        Assert.Empty(TraitSystem.BuildOffers(new[] { adv }, new[] { trait }));
    }

    // ---- 戦闘からの記録 ----

    [Fact]
    public void StandingAtLowHpIsRecordedEveryRound()
    {
        var adv = new AdventurerData(Master());
        adv.CombatHpMax = 100;
        adv.CombatHp = 20; // 4分の1以下＝瀕死かつ手傷

        var enemy = new EnemyData(new EnemyMasterData
        {
            id = "e", baseName = "案山子", threat = 1,
            vitality = 1, mental = 1, strength = 1, agility = 1,
            intelligence = 1, constitution = 1,
        });
        enemy.CombatHpMax = 1;
        enemy.CombatHp = 1;

        var recorder = new ExpeditionRecorder();
        BattleResolver.Resolve(
            new IUnitMember?[] { adv }, new IUnitMember?[] { enemy },
            new List<string>(), turn: 1, phase: 1, morale: new MoraleState(100),
            recorder: recorder);

        var record = recorder.For(adv.id);
        Assert.True(record[ExpeditionRecordType.LowHpRounds] >= 1);
        Assert.True(record[ExpeditionRecordType.NearDeathRounds] >= 1);
    }

    [Fact]
    public void HealthyMembersAccrueNoWoundedRounds()
    {
        var adv = new AdventurerData(Master());
        adv.CombatHpMax = 100;
        adv.CombatHp = 100;

        var enemy = new EnemyData(new EnemyMasterData
        {
            id = "e", baseName = "案山子", threat = 1,
            vitality = 1, mental = 1, strength = 1, agility = 1,
            intelligence = 1, constitution = 1,
        });
        enemy.CombatHpMax = 1;
        enemy.CombatHp = 1;

        var recorder = new ExpeditionRecorder();
        BattleResolver.Resolve(
            new IUnitMember?[] { adv }, new IUnitMember?[] { enemy },
            new List<string>(), turn: 1, phase: 1, morale: new MoraleState(100),
            recorder: recorder);

        Assert.Equal(0, recorder.For(adv.id)[ExpeditionRecordType.NearDeathRounds]);
    }

    [Fact]
    public void BattlesWithoutARecorderTouchNothing()
    {
        // 戦闘シミュレーターと Balance Lab は記録を渡さない。
        // 実在の冒険者の記録を汚さずに同じ戦闘ロジックを回せることを固定する。
        var adv = new AdventurerData(Master());
        adv.CombatHpMax = 100;
        adv.CombatHp = 10;

        var enemy = new EnemyData(new EnemyMasterData
        {
            id = "e", baseName = "案山子", threat = 1,
            vitality = 1, mental = 1, strength = 1, agility = 1,
            intelligence = 1, constitution = 1,
        });
        enemy.CombatHpMax = 1;
        enemy.CombatHp = 1;

        BattleResolver.Resolve(
            new IUnitMember?[] { adv }, new IUnitMember?[] { enemy },
            new List<string>(), turn: 1, phase: 1, morale: new MoraleState(100));

        Assert.True(adv.records.IsEmpty);
    }

    // ---- 依頼の結末そのものを数える ----

    /// <summary>
    /// 戦闘フックが数えるのはラウンド単位の出来事だが、こちらは「1本の依頼がどう終わったか」。
    /// 記録の粒度が違うだけで行き先は同じなので、特性の条件からは区別なく引ける。
    /// </summary>
    public class Outcomes
    {
        static QuestRun Run(int memberCount, out List<AdventurerData> members)
        {
            var quest = new QuestMasterData { id = "q", totalPhases = 5, phasesPerTurn = 1 };
            var run = new QuestRun(quest, startedTurn: 1);
            members = new List<AdventurerData>();
            for (int i = 0; i < memberCount; i++)
            {
                var adv = new AdventurerData(Master($"a{i}", $"隊員{i}"));
                members.Add(adv);
                run.formation[i] = adv;
            }
            return run;
        }

        [Fact]
        public void SoloClearsCountOnlyWhenOneMemberFinishesTheJob()
        {
            var solo = Run(1, out var alone);
            solo.completed = true;
            ExpeditionOutcomeRecorder.Record(solo);
            Assert.Equal(1, solo.recorder.Count(alone[0].id, ExpeditionRecordType.SoloClears));

            var pair = Run(2, out var two);
            pair.completed = true;
            ExpeditionOutcomeRecorder.Record(pair);
            Assert.Equal(0, pair.recorder.Count(two[0].id, ExpeditionRecordType.SoloClears));
        }

        [Fact]
        public void FailingAQuestIsRecordedForEveryoneWhoWasThere()
        {
            var run = Run(3, out var members);
            run.failed = true;
            ExpeditionOutcomeRecorder.Record(run);

            foreach (var member in members)
                Assert.Equal(1, run.recorder.Count(member.id, ExpeditionRecordType.QuestsFailed));
        }

        [Fact]
        public void LostComradesAreCountedForTheSurvivorsNotForTheDead()
        {
            var run = Run(3, out var members);
            members[1].isAlive = false;
            members[2].isAlive = false;
            run.completed = true;
            ExpeditionOutcomeRecorder.Record(run);

            Assert.Equal(2, run.recorder.Count(members[0].id, ExpeditionRecordType.ComradesLost));
            Assert.Equal(0, run.recorder.Count(members[1].id, ExpeditionRecordType.ComradesLost));
        }

        [Fact]
        public void SoleSurvivorNeedsComradesToHaveBeenLost()
        {
            var wiped = Run(3, out var members);
            members[1].isAlive = false;
            members[2].isAlive = false;
            ExpeditionOutcomeRecorder.Record(wiped);
            Assert.Equal(1, wiped.recorder.Count(members[0].id, ExpeditionRecordType.SoleSurvivor));

            // 最初から一人で出た遠征は「唯一の生還者」ではない。失う仲間がいない。
            var solo = Run(1, out var alone);
            solo.completed = true;
            ExpeditionOutcomeRecorder.Record(solo);
            Assert.Equal(0, solo.recorder.Count(alone[0].id, ExpeditionRecordType.SoleSurvivor));
        }

        [Fact]
        public void FlawlessClearsRequireThatNobodyWentDownAllExpedition()
        {
            var clean = Run(2, out var untouched);
            clean.completed = true;
            ExpeditionOutcomeRecorder.Record(clean);
            Assert.Equal(1, clean.recorder.Count(untouched[0].id, ExpeditionRecordType.FlawlessClears));

            // 帰還時には戦闘不能が負傷へ解決済みなので、道中で倒れたかは戦闘記録から見る。
            var bloodied = Run(2, out var hurt);
            bloodied.completed = true;
            bloodied.recorder.For(hurt[1].id).Add(ExpeditionRecordType.TimesDowned);
            ExpeditionOutcomeRecorder.Record(bloodied);
            Assert.Equal(0, bloodied.recorder.Count(hurt[0].id, ExpeditionRecordType.FlawlessClears));
        }

        [Fact]
        public void BossKillCountsOnlyForTheSurvivingFinisher()
        {
            var boss = Run(2, out var slayers);
            boss.completed = true;
            boss.bossDefeated = true;
            boss.bossFinisherAdventurerId = slayers[1].id;
            ExpeditionOutcomeRecorder.Record(boss);
            Assert.Equal(0, boss.recorder.Count(slayers[0].id, ExpeditionRecordType.BossKills));
            Assert.Equal(1, boss.recorder.Count(slayers[1].id, ExpeditionRecordType.BossKills));

            var pulled = Run(2, out var cautious);
            pulled.retreated = true;
            ExpeditionOutcomeRecorder.Record(pulled);
            Assert.Equal(1, pulled.recorder.Count(cautious[0].id, ExpeditionRecordType.Retreats));
            Assert.Equal(0, pulled.recorder.Count(cautious[0].id, ExpeditionRecordType.BossKills));
        }

        [Fact]
        public void TheDeadEarnNoClearCredit()
        {
            var run = Run(2, out var members);
            members[1].isAlive = false;
            run.completed = true;
            run.bossDefeated = true;
            run.bossFinisherAdventurerId = members[1].id;
            ExpeditionOutcomeRecorder.Record(run);

            Assert.Equal(0, run.recorder.Count(members[1].id, ExpeditionRecordType.BossKills));
            Assert.Equal(0, run.recorder.Count(members[0].id, ExpeditionRecordType.BossKills));
        }
    }

    // ---- ヘルプへの掲載 ----

    /// <summary>
    /// ヘルプの「特性と解禁条件」。職業スキルの解禁習熟度と同じように、
    /// 特性も何をどれだけ積めば開くのかをプレイヤーが読めなければならない。
    /// </summary>
    [Collection("Console presentation")]
    public class HelpCatalog
    {
        [Fact]
        public async Task HelpListsEveryTraitWithItsUnlockThresholds()
        {
            string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
            var db = MasterLoader.Load(dataDir);

            var originalIn = Console.In;
            var originalOut = Console.Out;
            // 「9=冒険者の節」→ PauseAsync を抜ける → 「0=戻る」
            using var input = new StringReader("9\n\n0\n");
            using var output = new StringWriter();
            string text;
            try
            {
                Console.SetIn(input);
                Console.SetOut(output);
                Ui.Use(new ConsoleGameIo());
                await HelpScreen.ShowAsync(db);
                text = output.ToString();
            }
            finally
            {
                Console.SetIn(originalIn);
                Console.SetOut(originalOut);
            }

            Assert.Contains("特性と解禁条件", text);

            foreach (var trait in db.traits.Values)
            {
                Assert.Contains(trait.traitName, text);
                foreach (var requirement in trait.requirements)
                    Assert.Contains(
                        $"{ExpeditionRecordTypes.DisplayName(requirement.record)} {requirement.atLeast}",
                        text);
            }

            // 担い手の型ごとに並べることで、「誰に何が生えるか」を読ませている。
            foreach (var lens in TraitAnalysis.AllLenses)
                Assert.Contains($"{TraitAnalysis.LensName(lens)}型が身につけられる特性", text);
        }
    }

    // ---- 遠征記録から生涯記録への合流 ----

    [Collection("Guild static state")]
    public class Merging
    {
        [Fact]
        public void FinishingAQuestFoldsTheExpeditionIntoTheCareerRecord()
        {
            var guild = new GuildManager(startGold: 500);
            var adv = new AdventurerData(Master("a", "アルファ"));
            guild.AddAdventurer(adv);

            var quest = new QuestMasterData { id = "q", totalPhases = 1, phasesPerTurn = 1 };
            var qm = new QuestManager(guild);
            var formation = new AdventurerData?[6];
            formation[0] = adv;
            Assert.True(qm.TryStartQuest(quest, formation, 1, out var error), error);

            var run = qm.activeQuests.Single();
            run.recorder.For(adv.id).Add(ExpeditionRecordType.CritKills, 7);
            qm.FinalizeQuest(run);

            Assert.Equal(7, adv.records[ExpeditionRecordType.CritKills]);
        }

        [Fact]
        public void RetreatingStillCountsHowTheyFought()
        {
            // 依頼を果たせたかどうかと、どう戦ったかは別の話。撤退でも記録は残る。
            var guild = new GuildManager(startGold: 500);
            var adv = new AdventurerData(Master("a", "アルファ"));
            guild.AddAdventurer(adv);

            var quest = new QuestMasterData { id = "q", totalPhases = 5, phasesPerTurn = 1 };
            var qm = new QuestManager(guild);
            var formation = new AdventurerData?[6];
            formation[0] = adv;
            Assert.True(qm.TryStartQuest(quest, formation, 1, out var error), error);

            var run = qm.activeQuests.Single();
            run.recorder.For(adv.id).Add(ExpeditionRecordType.NearDeathRounds, 9);
            run.retreated = true;
            qm.FinalizeQuest(run);

            Assert.Equal(9, adv.records[ExpeditionRecordType.NearDeathRounds]);
        }

        [Fact]
        public void OffersAppearOnTheQuestThatCrossesTheThreshold()
        {
            var guild = new GuildManager(startGold: 500);
            var adv = new AdventurerData(Master("a", "アルファ"));
            guild.AddAdventurer(adv);

            var skill = new SkillMasterData
            {
                id = "s", skillName = "不屈", family = "trait_test", level = 1,
                add = new StatBlock { emergencyHeal = 8 },
            };
            var trait = Trait("t", skill, (ExpeditionRecordType.NearDeathRounds, 8));

            var quest = new QuestMasterData { id = "q", totalPhases = 1, phasesPerTurn = 1 };
            var qm = new QuestManager(guild) { traitCatalog = new[] { trait } };
            var formation = new AdventurerData?[6];
            formation[0] = adv;
            Assert.True(qm.TryStartQuest(quest, formation, 1, out var error), error);

            var run = qm.activeQuests.Single();
            run.recorder.For(adv.id).Add(ExpeditionRecordType.NearDeathRounds, 8);
            qm.FinalizeQuest(run);

            var offer = Assert.Single(run.pendingTraitOffers);
            Assert.Same(adv, offer.Adventurer);
            Assert.Contains(trait, offer.Candidates);
        }

        [Fact]
        public void RealQuestsAccumulateRecordsFromActualFighting()
        {
            // 実マスタで実際にクエストを走らせ、戦闘フックから記録が積まれて
            // 生涯記録まで届くことを端から端まで確かめる。
            using var seed = GameRandom.UseSeed(20260809);

            string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
            var db = MasterLoader.Load(dataDir);

            var guild = new GuildManager(startGold: 1000, startRank: 1);
            var qm = new QuestManager(guild) { traitCatalog = db.traits.Values.ToList() };

            var formation = new AdventurerData?[6];
            var party = db.allAdventurers
                .Where(m => m.recruitGuildRank <= 1)
                .Take(4)
                .Select(m => new AdventurerData(m))
                .ToList();
            for (int i = 0; i < party.Count; i++)
            {
                guild.AddAdventurer(party[i]);
                formation[i] = party[i];
            }

            // 戦闘が確実に起きるクエストを選ぶ（採取専用だと剣を抜かないまま終わりうる）。
            var quest = db.allQuests.First(q =>
                q.rank <= 1 && !q.IsGatherQuest && q.Dungeon != null);
            Assert.True(qm.TryStartQuest(quest, formation, 1, out var error), error);

            var run = qm.activeQuests.Single();
            for (int turn = 2; turn <= 40 && qm.activeQuests.Contains(run); turn++)
            {
                run.pendingChoice = null;      // 選択イベントは自動で流す
                run.gatherDecisionPending = false;
                qm.AdvanceAll(turn);
                if (run.CanComplete || run.failed) break;
            }
            qm.FinalizeQuest(run);

            int totalRecorded = party.Sum(a =>
                ExpeditionRecordTypes.All.Sum(type => a.records[type]));
            Assert.True(totalRecorded > 0,
                "実クエストを1本走らせても記録が1件も積まれていない（戦闘フックが繋がっていない）");
        }

        [Fact]
        public void RecordsAndOfferHistorySurviveASaveRoundTrip()
        {
            string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
            var db = MasterLoader.Load(dataDir);

            var guild = new GuildManager(startGold: 200, startRank: 1);
            var qm = new QuestManager(guild) { traitCatalog = db.traits.Values.ToList() };
            var advMaster = db.allAdventurers.First(a => a.recruitGuildRank <= 1);
            var adv = new AdventurerData(advMaster);
            guild.AddAdventurer(adv);

            adv.records.Add(ExpeditionRecordType.NearDeathRounds, 11);
            adv.records.Add(ExpeditionRecordType.CritKills, 3);
            adv.offeredTraitIds.Add("trait_unyielding");

            // 進行中クエストの記録も、途中セーブで失われてはいけない。
            var quest = db.allQuests.First(q => q.rank <= 1);
            var formation = new AdventurerData?[6];
            formation[0] = adv;
            Assert.True(qm.TryStartQuest(quest, formation, 1, out var error), error);
            qm.activeQuests.Single().recorder.For(adv.id).Add(ExpeditionRecordType.ShieldBlocks, 4);

            string tmpPath = Path.Combine(Path.GetTempPath(), $"airpg_traits_{Guid.NewGuid():N}.json");
            try
            {
                SaveManager.Save(tmpPath, guild, qm, currentTurn: 3, new List<AdventurerMasterData>());
                var loaded = SaveManager.Load(tmpPath, db);

                var loadedAdv = Assert.Single(loaded.Guild.adventurers);
                Assert.Equal(11, loadedAdv.records[ExpeditionRecordType.NearDeathRounds]);
                Assert.Equal(3, loadedAdv.records[ExpeditionRecordType.CritKills]);
                Assert.Contains("trait_unyielding", loadedAdv.offeredTraitIds);

                var loadedRun = loaded.QuestManager.activeQuests.Single();
                Assert.Equal(4, loadedRun.recorder.For(adv.id)[ExpeditionRecordType.ShieldBlocks]);
            }
            finally
            {
                if (File.Exists(tmpPath)) File.Delete(tmpPath);
            }
        }

        [Fact]
        public void NoTraitCatalogMeansNoOffers()
        {
            var guild = new GuildManager(startGold: 500);
            var adv = new AdventurerData(Master("a", "アルファ"));
            guild.AddAdventurer(adv);

            var quest = new QuestMasterData { id = "q", totalPhases = 1, phasesPerTurn = 1 };
            var qm = new QuestManager(guild);
            var formation = new AdventurerData?[6];
            formation[0] = adv;
            Assert.True(qm.TryStartQuest(quest, formation, 1, out var error), error);

            var run = qm.activeQuests.Single();
            run.recorder.For(adv.id).Add(ExpeditionRecordType.NearDeathRounds, 100);
            qm.FinalizeQuest(run);

            Assert.Empty(run.pendingTraitOffers);
        }
    }
}
