using GuildSimulator.Core;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Game.Data;
using Xunit;
using Xunit.Abstractions;

namespace GuildSimulator.Tests;

/// <summary>
/// スキルで入った新しい仕掛けの検証。
/// ヘイト・完全防御・貫通の自動成功・会心強化・応急処置・素手・積載、
/// それに戦闘の外に効く遠征効果。
/// </summary>
public class SkillEffectTests
{
    readonly ITestOutputHelper output;

    public SkillEffectTests(ITestOutputHelper output) => this.output = output;

    static GameMasterData Load() => MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    static AdventurerData Adventurer(string name = "測定用", int hp = 400) =>
        new(new AdventurerMasterData
        {
            id = name, baseName = name,
            vitality = 12, mental = 12, strength = 16, agility = 14,
            intelligence = 10, constitution = 12,
        })
        { CombatHpMax = hp, CombatHp = hp };

    static EnemyData Dummy(int naturalAv, int hp, string dice = "1d1-1") =>
        new(new EnemyMasterData
        {
            id = "dummy", baseName = "案山子",
            vitality = 10, mental = 10, strength = 1, agility = 1,
            intelligence = 1, constitution = 8,
            naturalAv = naturalAv - QudCombat.Modifier(8),
            naturalDamageDice = dice, naturalPv = 0,
        })
        { CombatHpMax = hp, CombatHp = hp };

    /// <summary>装甲を確実に抜いてくる殴り手。深手を負う場面を作るのに使う。</summary>
    static EnemyData Puncher(int hp) =>
        new(new EnemyMasterData
        {
            id = "puncher", baseName = "殴り手",
            vitality = 10, mental = 10, strength = 8, agility = 8,
            intelligence = 1, constitution = 8,
            naturalAv = -QudCombat.Modifier(8),
            naturalDamageDice = "1d4", naturalPv = 10,
        })
        { CombatHpMax = hp, CombatHp = hp };

    /// <summary>ログ末尾の "HP=現在/最大" を割合にする。</summary>
    static float ParseHpRate(string line)
    {
        var tail = line[(line.LastIndexOf(" HP=", StringComparison.Ordinal) + 4)..];
        var parts = tail.Split('/');
        return float.Parse(parts[0]) / float.Parse(parts[1]);
    }

    static List<string> Fight(IUnitMember?[] allies, IUnitMember?[] enemies)
    {
        var logs = new List<string>();
        BattleResolver.Resolve(allies, enemies, logs, turn: 1, phase: 1, new MoraleState(1_000_000));
        return logs;
    }

    static IUnitMember?[] Side(params IUnitMember?[] members)
    {
        var side = new IUnitMember?[6];
        for (int i = 0; i < members.Length && i < 6; i++) side[i] = members[i];
        return side;
    }

    // ---- ヘイト（threatWeight） ----

    [Fact]
    public void TauntPullsAttacksAndStealthPushesThemAway()
    {
        var db = Load();
        var taunt = db.skills["skill_taunt_lv3"];
        var stealth = db.skills["skill_stealth_lv3"];
        Assert.True(taunt.add.threatWeight > 0, "挑発がヘイトを上げていない");
        Assert.True(stealth.add.threatWeight < 0, "隠形がヘイトを下げていない");

        int taunted = 0, hidden = 0, plain = 0;
        for (int i = 0; i < 30; i++)
        {
            var loud = Adventurer("囮", 100_000);
            loud.LearnPermanentSkill(taunt);
            var quiet = Adventurer("影", 100_000);
            quiet.LearnPermanentSkill(stealth);
            var normal = Adventurer("並", 100_000);

            // 素手の案山子は殴るだけ。誰が狙われたかをログの行数で数える。
            foreach (var line in Fight(Side(loud, quiet, normal), Side(Dummy(0, 30_000, "1d4"))))
            {
                if (!line.Contains("案山子→")) continue;
                if (line.Contains("→囮")) taunted++;
                else if (line.Contains("→影")) hidden++;
                else if (line.Contains("→並")) plain++;
            }
        }
        output.WriteLine($"狙われた回数: 囮{taunted} / 並{plain} / 影{hidden}");

        Assert.True(taunted > plain, "挑発持ちが引きつけられていない");
        Assert.True(plain > hidden, "隠形持ちが後回しにされていない");
    }

    // ---- 盾の完全防御（blockNegate） ----

    [Fact]
    public void TheTopShieldMasteryCanNullifyABlockedHitEntirely()
    {
        var db = Load();
        var mastery = db.skills["skill_shieldMastery_lv5"];
        Assert.True(mastery.add.blockNegate > 0, "盾術Lv5に完全防御がない");
        Assert.True(mastery.requireShield, "盾術が盾を要求していない");

        var defender = Adventurer("守り手", 100_000);
        defender.SetEquipped(EquipSlot.RightHand, db.equipment["eq_sword_01"]);
        defender.SetEquipped(EquipSlot.LeftHand, db.equipment["eq_towershield_02"]);
        defender.LearnPermanentSkill(mastery);

        var logs = Fight(Side(defender), Side(Dummy(0, 100_000, "1d6")));
        Assert.Contains(logs, l => l.Contains("完全に受けきった"));
    }

    [Fact]
    public void BlockNegationNeedsAShieldInHand()
    {
        var db = Load();
        var mastery = db.skills["skill_shieldMastery_lv5"];

        var bare = Adventurer("盾なし", 100_000);
        bare.SetEquipped(EquipSlot.RightHand, db.equipment["eq_sword_01"]);
        bare.LearnPermanentSkill(mastery);

        Assert.DoesNotContain(bare.Skills,
            s => UnitCalculator.MeetsGearRequirements(s, bare) && s.add.blockNegate > 0);
    }

    // ---- 貫通の自動成功と会心強化 ----

    [Fact]
    public void AutoPenetrationRescuesAttacksThatTheArmourWouldHaveStopped()
    {
        // PV0 で AV30 は素の判定ではまず抜けない（貫通ダイスは上振れが青天井なので、
        // ごく稀に自力で抜ける。だから「絶対に0」ではなく頻度で見る）。
        int without = 0, with = 0, flagged = 0;
        for (int i = 0; i < 2000; i++)
        {
            if (QudCombat.ResolveAttack(0, 30, "1d4", critical: false).penetrations > 0) without++;

            var forced = QudCombat.ResolveAttack(0, 30, "1d4", critical: false, autoPenetrate: 50);
            if (forced.penetrations > 0) with++;
            if (forced.autoPenetrated)
            {
                flagged++;
                Assert.True(forced.penetrations > 0, "自動成功したのに貫通していない");
            }
        }
        output.WriteLine($"貫通できた回数: 素{without} / 自動成功50%{with}（うち自動成功{flagged}）");

        Assert.True(with > without * 5, "貫通の自動成功が効いていない");
        Assert.True(flagged > 0, "自動成功の印が一度も立っていない");
    }

    [Fact]
    public void CritPvMakesCriticalHitsBiteDeeper()
    {
        long plain = 0, sharpened = 0;
        for (int i = 0; i < 3000; i++)
        {
            plain += QudCombat.ResolveAttack(4, 10, "1d4", critical: true).penetrations;
            sharpened += QudCombat.ResolveAttack(4, 10, "1d4", critical: true, critPv: 6).penetrations;
        }
        output.WriteLine($"会心の貫通回数合計: 素{plain} / 会心PV+6 {sharpened}");
        Assert.True(sharpened > plain, "会心効果の増加が乗っていない");
    }

    [Fact]
    public void CritPvChangesNothingOnANonCriticalHit()
    {
        // 会心でないときは上乗せしない。常時PVの水増しにはならない。
        var a = QudCombat.ResolveAttack(5, 5, "1d1", critical: false, critPv: 50);
        Assert.Equal(5, a.pv);
    }

    // ---- 応急処置 ----

    [Fact]
    public void FirstAidFiresOnceWhenHealthDropsBelowHalf()
    {
        var db = Load();
        var firstAid = db.skills["skill_firstAid_lv2"];
        Assert.True(firstAid.add.emergencyHeal > 0);

        // 戦闘のばらつきに寄りかからず、ログから読み取れる事実だけで判定する。
        //   ・半分を切って生き延びた戦闘 → 必ず1度だけ発動している
        //   ・半分を切らなかった戦闘     → 発動していない
        const int battles = 40;
        int wounded = 0;
        for (int i = 0; i < battles; i++)
        {
            var adv = Adventurer("負傷者", 400);
            adv.LearnPermanentSkill(firstAid);

            var logs = Fight(Side(adv), Side(Puncher(100_000)));
            int fired = logs.Count(l => l.Contains("応急処置"));
            Assert.InRange(fired, 0, 1);

            bool died = logs.Any(l => l.Contains("負傷者 撃破！"));
            bool droppedBelowHalf = logs
                .Where(l => l.Contains("→負傷者") && l.Contains(" HP="))
                .Select(ParseHpRate)
                .Any(r => r < BattleResolver.EMERGENCY_HEAL_HP_RATE);

            if (died) continue;
            if (droppedBelowHalf)
            {
                wounded++;
                Assert.True(fired == 1, "半分を切ったのに応急処置が働いていない");
            }
            else
            {
                Assert.True(fired == 0, "半分を切っていないのに応急処置が働いた");
            }
        }
        output.WriteLine($"{battles}戦のうち、半分を切って生還したのは{wounded}戦");
        Assert.True(wounded > 0, "深手を負う戦闘が一度も起きていない（検証になっていない）");
    }

    // ---- 素手 ----

    [Fact]
    public void MartialArtsNeedsEmptyHandsAndUpgradesTheFist()
    {
        var db = Load();
        var lv1 = db.skills["skill_martialArts_lv1"];
        var lv5 = db.skills["skill_martialArts_lv5"];
        Assert.True(lv1.requireUnarmed && lv5.requireUnarmed);

        var monk = Adventurer("拳士");
        Assert.Equal(AdventurerData.UNARMED_DAMAGE_DICE, monk.DamageDice);

        monk.LearnPermanentSkill(lv1);
        monk.LearnPermanentSkill(lv5);
        // 段階は重ねがけにならないので、拳のダイスも最上位の1本だけ。
        Assert.Equal(lv5.unarmedDamageDice, monk.DamageDice);
        Assert.True(Dice.Parse(monk.DamageDice).Max
                    > Dice.Parse(AdventurerData.UNARMED_DAMAGE_DICE).Max);

        // 得物を握った瞬間に格闘術は消え、武器のダイスに戻る。
        var sword = db.equipment["eq_sword_01"];
        monk.SetEquipped(EquipSlot.RightHand, sword);
        Assert.Equal(sword.damageDice, monk.DamageDice);
        Assert.False(UnitCalculator.MeetsGearRequirements(lv5, monk));
    }

    // ---- 積載 ----

    [Fact]
    public void PlateMasteryCancelsTheWeightPenaltyOfHeavyArmour()
    {
        var db = Load();
        var mastery = db.skills["skill_plateMastery_lv5"];
        Assert.True(mastery.add.carry > 0);
        Assert.True(mastery.requireArmorType && mastery.requiredArmorType == ArmorType.Plate);

        int DvLoss(AdventurerData a) =>
            a.GetBaseCombatStats().dv + a.GetEquipmentBonus().dv - a.GetFinalCombatStats().dv;

        var knight = Adventurer("重装");
        knight.SetEquipped(EquipSlot.Body, db.equipment["eq_plate_02"]);
        knight.SetEquipped(EquipSlot.Head, db.equipment["eq_helm_01"]);
        knight.SetEquipped(EquipSlot.RightHand, db.equipment["eq_greataxe_03"]);
        int before = DvLoss(knight);
        Assert.True(before > 0, "重装で過積載になっていない");

        knight.LearnPermanentSkill(mastery);
        int after = DvLoss(knight);
        output.WriteLine($"過積載によるDV減: 重鎧マスタリー無し{before} / Lv5あり{after}");
        Assert.True(after < before, "重鎧マスタリーが積載を助けていない");
    }

    [Fact]
    public void CarryBonusOnlyCountsWhileTheRequiredArmourIsWorn()
    {
        var db = Load();
        var adv = Adventurer("軽装");
        adv.LearnPermanentSkill(db.skills["skill_plateMastery_lv5"]);
        Assert.Equal(0, adv.SkillCarryBonus);

        adv.SetEquipped(EquipSlot.Body, db.equipment["eq_plate_02"]);
        Assert.True(adv.SkillCarryBonus > 0);
    }

    // ---- 遠征に効く効果 ----

    [Fact]
    public void ExpeditionEffectsAddUpAcrossTheWholeParty()
    {
        var db = Load();
        var haggle = db.skills["skill_haggle_lv1"];

        var a = Adventurer("甲");
        var b = Adventurer("乙");
        a.LearnPermanentSkill(haggle);
        b.LearnPermanentSkill(haggle);

        var one = PartySkillEffects.Of(new[] { a });
        var two = PartySkillEffects.Of(new[] { a, b });

        Assert.Equal(haggle.expedition.goldPercent, one.goldPercent);
        Assert.Equal(haggle.expedition.goldPercent * 2, two.goldPercent);
        Assert.True(two.GoldMultiplier > one.GoldMultiplier);
    }

    [Fact]
    public void ExpeditionPenaltiesNeverDriveRewardsBelowZero()
    {
        var flaw = new SkillMasterData
        {
            id = "flaw", skillName = "大穴",
            expedition = new SkillExpeditionEffect { goldPercent = -80, expPercent = -80 },
        };
        var party = Enumerable.Range(0, 5).Select(i =>
        {
            var a = Adventurer($"欠{i}");
            a.LearnPermanentSkill(flaw);
            return a;
        }).ToArray();

        var effects = PartySkillEffects.Of(party);
        Assert.Equal(PartySkillEffects.MinRewardPercent, effects.goldPercent);
        Assert.Equal(0f, effects.GoldMultiplier);
        Assert.Equal(0f, effects.ExpMultiplier);
    }

    [Fact]
    public void TrapSenseLowersTheTrapWeightAndTreasureHuntingRaisesTheChestWeight()
    {
        var db = Load();
        var trapSense = db.skills["skill_trapSense_lv3"];
        var hunter = db.skills["skill_treasureHunter_lv3"];

        var scout = Adventurer("斥候");
        scout.LearnPermanentSkill(trapSense);
        scout.LearnPermanentSkill(hunter);

        var effects = PartySkillEffects.Of(new[] { scout });
        Assert.True(effects.ChanceMultiplierFor(DungeonEventType.Trap) < 1f);
        Assert.True(effects.ChanceMultiplierFor(DungeonEventType.Treasure) > 1f);
        // 関係のないイベントは素通し。
        Assert.Equal(1f, effects.ChanceMultiplierFor(DungeonEventType.EnemyEncounter));
    }

    [Fact]
    public void AnEmptyPartyProducesNoExpeditionEffects()
    {
        Assert.Equal(PartySkillEffects.None, PartySkillEffects.Of(null));
        Assert.Equal(PartySkillEffects.None, PartySkillEffects.Of(new AdventurerData?[6]));
    }
}
