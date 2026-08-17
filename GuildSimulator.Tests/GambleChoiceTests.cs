using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Game.Data;
using Xunit;
using Xunit.Abstractions;

namespace GuildSimulator.Tests;

/// <summary>
/// クエスト中の賭けイベント。プレイヤーは「誰に賭けるか」だけを決め、
/// 結果はそのあとで抽選される。恒久的な能力の増減がキャラクターの差を作る。
/// </summary>
[Collection("Guild static state")]
public class GambleChoiceTests
{
    readonly ITestOutputHelper output;

    public GambleChoiceTests(ITestOutputHelper output) => this.output = output;

    static AdventurerMasterData Master(string id, string name) => new()
    {
        id = id, baseName = name,
        vitality = 10, mental = 10, strength = 10, agility = 10,
        intelligence = 10, constitution = 10,
    };

    /// <summary>選択待ちの状態まで進めたクエストを1件用意する。</summary>
    static (QuestManager qm, QuestRun run, List<AdventurerData> party) Pending(
        QuestChoiceEventMasterData choice)
    {
        var guild = new GuildManager(startGold: 500);
        var party = new List<AdventurerData>
        {
            new(Master("a", "アルファ")),
            new(Master("b", "ベータ")),
        };
        foreach (var a in party) guild.AddAdventurer(a);

        var dungeon = new DungeonMasterData { turnEndEventChance = 1f };
        dungeon.turnEndEvents.Add(choice);
        var quest = new QuestMasterData
        {
            id = "q", totalPhases = 10, phasesPerTurn = 1, Dungeon = dungeon,
        };
        var qm = new QuestManager(guild);
        var formation = new AdventurerData?[6];
        formation[0] = party[0];
        formation[1] = party[1];
        Assert.True(qm.TryStartQuest(quest, formation, 1, out var error), error);

        qm.AdvanceAll(2);
        var run = qm.activeQuests.Single();
        Assert.True(qm.HasPendingChoices);
        return (qm, run, party);
    }

    static QuestChoiceEventMasterData SingleOption(QuestChoiceOptionData option) => new()
    {
        id = "ev", title = "試練", weight = 1,
        options = { option, new QuestChoiceOptionData { text = "何もしない", resultText = "見送った" } },
    };

    [Fact]
    public void OnlyTheChosenMemberIsAffected()
    {
        var (qm, run, party) = Pending(SingleOption(new QuestChoiceOptionData
        {
            text = "鍛える", resultText = "打ち込みを受けた",
            targetsOneMember = true,
            effectType = QuestChoiceEffectType.AdventurerStatUp,
            value = 1, targetId = "Strength",
        }));

        int otherBefore = party[1].strength;
        Assert.True(qm.ResolveChoice(run, 0, party[0], out var result), result);

        Assert.Equal(11, party[0].strength);
        Assert.Equal(otherBefore, party[1].strength);
        Assert.Contains("アルファ", result);
        Assert.Contains("筋力", result);
    }

    [Fact]
    public void ATargetedChoiceRefusesToResolveWithoutALivingTarget()
    {
        var (qm, run, party) = Pending(SingleOption(new QuestChoiceOptionData
        {
            text = "鍛える", resultText = "打ち込みを受けた",
            targetsOneMember = true,
            effectType = QuestChoiceEffectType.AdventurerStatUp, value = 1,
        }));

        // 対象なしでは解決できない。選択は保留のまま残る。
        Assert.False(qm.ResolveChoice(run, 0, out var noTarget));
        Assert.Contains("1人指定", noTarget);
        Assert.True(qm.HasPendingChoices);

        // 隊にいない冒険者も指定できない。
        var outsider = new AdventurerData(Master("x", "部外者"));
        Assert.False(qm.ResolveChoice(run, 0, outsider, out _));

        // 死亡者も対象外。
        party[0].isAlive = false;
        Assert.False(qm.ResolveChoice(run, 0, party[0], out _));
        Assert.True(qm.HasPendingChoices);
    }

    [Fact]
    public void StatsNeverFallBelowTheFloor()
    {
        // 能力値が0以下になると modifier が壊れるので、削られても1で止まる。
        var (qm, run, party) = Pending(SingleOption(new QuestChoiceOptionData
        {
            text = "捧げる", resultText = "力を吸われた",
            targetsOneMember = true,
            effectType = QuestChoiceEffectType.AdventurerStatDown,
            value = 99, targetId = "Mental",
        }));

        Assert.True(qm.ResolveChoice(run, 0, party[0], out _));
        Assert.Equal(AdventurerData.MinStatValue, party[0].mental);
    }

    [Fact]
    public void AnOutcomeTableIsRolledAfterTheTargetIsChosen()
    {
        // 「誰に賭けるか」を決めてから振る。同じ選択肢でも結果は毎回変わる。
        var results = new Dictionary<string, int>();
        const int trials = 400;
        for (int i = 0; i < trials; i++)
        {
            var option = new QuestChoiceOptionData
            {
                text = "触れる", resultText = "手を差し入れた", targetsOneMember = true,
                outcomes =
                {
                    new QuestChoiceOutcome
                    {
                        weight = 50, effectType = QuestChoiceEffectType.AdventurerStatUp,
                        value = 1, targetId = "Strength", resultText = "力が湧いた",
                    },
                    new QuestChoiceOutcome
                    {
                        weight = 50, effectType = QuestChoiceEffectType.AdventurerStatDown,
                        value = 1, targetId = "Strength", resultText = "力を奪われた",
                    },
                },
            };
            Assert.True(option.IsGamble);

            var (qm, run, party) = Pending(SingleOption(option));
            Assert.True(qm.ResolveChoice(run, 0, party[0], out _));

            string key = party[0].strength > 10 ? "up" : party[0].strength < 10 ? "down" : "none";
            results[key] = results.GetValueOrDefault(key) + 1;
        }

        output.WriteLine(string.Join(" / ", results.Select(kv => $"{kv.Key}:{kv.Value}")));
        Assert.False(results.ContainsKey("none"));
        Assert.InRange((double)results["up"] / trials, 0.35, 0.65);
    }

    [Fact]
    public void ASkillOutcomeTeachesTheChosenMember()
    {
        var skill = new SkillMasterData { id = "sk", skillName = "秘伝" };
        var (qm, run, party) = Pending(SingleOption(new QuestChoiceOptionData
        {
            text = "教えを乞う", resultText = "老兵は頷いた", targetsOneMember = true,
            effectType = QuestChoiceEffectType.AdventurerSkill, targetId = skill.id,
            outcomes =
            {
                new QuestChoiceOutcome
                {
                    weight = 1, effectType = QuestChoiceEffectType.AdventurerSkill,
                    targetId = skill.id, Skill = skill,
                },
            },
        }));

        Assert.True(qm.ResolveChoice(run, 0, party[1], out var result));
        Assert.Contains(skill, party[1].Skills);
        Assert.DoesNotContain(skill, party[0].Skills);
        Assert.Contains("秘伝", result);
    }

    [Fact]
    public void AChoiceWithoutAnOutcomeTableStillBehavesAsBefore()
    {
        // 既存のイベントは1選択肢=1効果のまま動く。
        var (qm, run, _) = Pending(SingleOption(new QuestChoiceOptionData
        {
            text = "探る", resultText = "資金を見つけた",
            effectType = QuestChoiceEffectType.Gold, value = 40,
        }));

        Assert.True(qm.ResolveChoice(run, 0, out var result));
        Assert.Contains("ゴールド+40", result);
        Assert.Contains(run.pendingLoot, x => x.type == RewardType.Gold && x.gold == 40);
    }

    [Fact]
    public void TheShippedGambleEventsAreReachableAndWellFormed()
    {
        var db = MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

        var gambles = db.choiceEvents.Values
            .Where(e => e.options.Any(o => o.IsGamble))
            .ToList();
        Assert.NotEmpty(gambles);

        foreach (var ev in gambles)
        {
            // どのダンジョンからも参照されていなければ、一度も発生しない。
            Assert.Contains(db.dungeons.Values, d => d.turnEndEvents.Contains(ev));

            foreach (var option in ev.options.Where(o => o.IsGamble))
            {
                Assert.True(option.outcomes.Sum(o => o.weight) > 0);
                if (option.targetsOneMember)
                {
                    // 個人の賭けは、恒久的な幸運と個人が負う危険の両方を持つ。
                    Assert.Contains(option.outcomes, o =>
                        o.effectType == QuestChoiceEffectType.AdventurerStatUp
                        || o.effectType == QuestChoiceEffectType.AdventurerSkill);
                    Assert.Contains(option.outcomes, o =>
                        o.effectType == QuestChoiceEffectType.AdventurerStatDown
                        || o.effectType == QuestChoiceEffectType.AdventurerDamage
                        || o.effectType == QuestChoiceEffectType.Morale && o.value < 0);
                }
                else
                {
                    // 積み荷の捜索など、パーティ全体で引き受けるリスクも同じ結果表で表せる。
                    Assert.DoesNotContain(option.outcomes, o => o.effectType is
                        QuestChoiceEffectType.AdventurerStatUp
                        or QuestChoiceEffectType.AdventurerStatDown
                        or QuestChoiceEffectType.AdventurerSkill
                        or QuestChoiceEffectType.AdventurerDamage);
                    Assert.Contains(option.outcomes, o => o.effectType is
                        QuestChoiceEffectType.Experience
                        or QuestChoiceEffectType.Equipment
                        or QuestChoiceEffectType.Consumable
                        or QuestChoiceEffectType.Treasure
                        || o.effectType == QuestChoiceEffectType.Gold && o.value > 0
                        || o.effectType == QuestChoiceEffectType.Morale && o.value > 0);
                    Assert.Contains(option.outcomes, o => o.effectType == QuestChoiceEffectType.DamagePercent
                        || o.effectType == QuestChoiceEffectType.Gold && o.value < 0
                        || o.effectType == QuestChoiceEffectType.Morale && o.value < 0);
                }
            }
        }
        output.WriteLine($"賭けイベント: {string.Join("、", gambles.Select(e => e.title))}");
    }

    [Theory]
    [InlineData("event_forest_lore")]
    [InlineData("event_ruin_tablets")]
    public void ShippedSkillChoiceEventsOfferThreeResolvedDeterministicSkills(string eventId)
    {
        var db = MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var choice = db.choiceEvents[eventId];

        Assert.Equal(3, choice.options.Count);
        Assert.Contains(db.dungeons.Values, dungeon => dungeon.turnEndEvents.Contains(choice));
        Assert.All(choice.options, option =>
        {
            Assert.True(option.targetsOneMember);
            Assert.False(option.IsGamble);
            Assert.Equal(QuestChoiceEffectType.AdventurerSkill, option.effectType);
            Assert.NotNull(option.Skill);
        });
        Assert.Equal(3, choice.options.Select(option => option.Skill!.id).Distinct().Count());

        var (qm, run, party) = Pending(choice);
        var offeredSkill = choice.options[0].Skill!;
        Assert.True(qm.ResolveChoice(run, 0, party[0], out var result), result);
        Assert.Contains(offeredSkill, party[0].AllLearnedSkills);
        Assert.DoesNotContain(offeredSkill, party[1].AllLearnedSkills);

        // 同じイベントに再遭遇しても、習得済みの隊員を選んだだけで機会を失わない。
        run.pendingChoice = new PendingQuestChoice { Event = choice, createdTurn = 3 };
        Assert.False(qm.ResolveChoice(run, 0, party[0], out var duplicate));
        Assert.Contains("別の隊員", duplicate);
        Assert.NotNull(run.pendingChoice);

        party[1].LearnPermanentSkill(offeredSkill);
        Assert.False(qm.ResolveChoice(run, 0, party[0], out var saturated));
        Assert.Contains("別のスキル", saturated);
        Assert.NotNull(run.pendingChoice);

        foreach (var skill in choice.options.Select(option => option.Skill!))
            foreach (var member in party)
                member.LearnPermanentSkill(skill);
        Assert.True(qm.ResolveChoice(run, 0, party[0], out var allKnown), allKnown);
        Assert.Contains("すでに", allKnown);
        Assert.Null(run.pendingChoice);
    }
}
