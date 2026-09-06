using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

/// <summary>
/// 遠征中の賭けで背負う「呪い」（`skill_curse_*`）を、配られ方ごと固定する。
///
/// 呪いは恒久的な不利益なので、うっかり良い効果が混ざったり、序盤のダンジョンで
/// 踏めるようになったりすると、賭けの意味そのものが壊れる。
/// </summary>
public class CurseSkillTests
{
    /// <summary>ランク1のクエストが並ぶ、引き返せない不利益を踏ませたくないダンジョン。</summary>
    static readonly string[] EarlyDungeonIds = { "dungeon_meadow", "dungeon_woods" };

    static GameMasterData Load() => MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    static IEnumerable<SkillMasterData> Curses(GameMasterData db) => db.skills.Values
        .Where(skill => skill.id.StartsWith("skill_curse_", StringComparison.Ordinal));

    static bool IsCurse(SkillMasterData skill) =>
        skill.id.StartsWith("skill_curse_", StringComparison.Ordinal);

    [Fact]
    public void EveryCurseSkillIsNothingButADrawback()
    {
        var db = Load();
        var curses = Curses(db).ToList();

        // 「相当数」あることそのものが賭けの手応えなので、数も一緒に固定する。
        Assert.True(curses.Count >= 12, $"呪いスキルが {curses.Count} 件しかない");

        foreach (var curse in curses)
        {
            var effect = TraitAnalysis.Evaluate(curse, null);
            Assert.True(effect.Benefits.Count == 0,
                $"{curse.id}: 呪いに利点が混ざっている（{string.Join("、", effect.Benefits)}）");
            Assert.True(effect.Drawbacks.Count > 0, $"{curse.id}: 不利益が何も無い");
        }
    }

    [Fact]
    public void CursesAreOnlyEverHandedOutByAGamble()
    {
        // 確定型の3択に呪いが並ぶと、プレイヤーは「損の少ない呪い」を選ばされるだけになる。
        var db = Load();

        foreach (var choiceEvent in db.choiceEvents.Values)
        {
            foreach (var option in choiceEvent.options.Where(o => !o.IsGamble))
            {
                Assert.DoesNotContain(option.Outcomes, outcome =>
                    outcome.effectType == QuestChoiceEffectType.AdventurerSkill
                    && outcome.Skill != null && IsCurse(outcome.Skill));
            }
        }
    }

    [Fact]
    public void EveryCurseCanActuallyBeDrawnSomewhere()
    {
        // どのダンジョンからも辿れない呪いは、マスタに居るだけで一度も踏まれない。
        var db = Load();

        var reachable = db.dungeons.Values
            .SelectMany(dungeon => dungeon.turnEndEvents)
            .SelectMany(choiceEvent => choiceEvent.options)
            .SelectMany(option => option.Outcomes)
            .Where(outcome => outcome.effectType == QuestChoiceEffectType.AdventurerSkill)
            .Select(outcome => outcome.Skill!.id)
            .ToHashSet();

        Assert.All(Curses(db), curse => Assert.Contains(curse.id, reachable));
    }

    [Fact]
    public void EarlyDungeonsNeverHandOutACurse()
    {
        // 恒久的な能力減少と同じ理由で、取り返しのつかない呪いも奥地にだけ置く。
        var db = Load();

        foreach (string dungeonId in EarlyDungeonIds)
        {
            var offered = db.dungeons[dungeonId].turnEndEvents
                .SelectMany(choiceEvent => choiceEvent.options)
                .SelectMany(option => option.Outcomes)
                .Where(outcome => outcome.effectType == QuestChoiceEffectType.AdventurerSkill)
                .Select(outcome => outcome.Skill!)
                .Where(IsCurse)
                .Select(skill => skill.id)
                .ToList();

            Assert.True(offered.Count == 0,
                $"{dungeonId}: 序盤のダンジョンで呪いを踏める（{string.Join("、", offered)}）");
        }
    }

    [Fact]
    public void ACurseGambleAlsoOffersSomethingWorthTheRisk()
    {
        // 当たりの無い賭けはただの罰なので、呪いを含む選択肢には必ず見返りを置く。
        var db = Load();

        var curseOptions = db.choiceEvents.Values
            .SelectMany(choiceEvent => choiceEvent.options.Select(option => (choiceEvent, option)))
            .Where(pair => pair.option.Outcomes.Any(outcome =>
                outcome.effectType == QuestChoiceEffectType.AdventurerSkill
                && outcome.Skill != null && IsCurse(outcome.Skill)))
            .ToList();

        Assert.NotEmpty(curseOptions);

        foreach (var (choiceEvent, option) in curseOptions)
        {
            Assert.True(option.Outcomes.Any(outcome =>
                    outcome.effectType == QuestChoiceEffectType.AdventurerStatUp
                    || outcome.effectType == QuestChoiceEffectType.AdventurerSkill
                    && outcome.Skill != null && !IsCurse(outcome.Skill)),
                $"{choiceEvent.id}: 選択肢「{option.text}」に見返りが無い");

            // 呪いだけで結果表を埋めない。踏み抜く確率が高すぎると誰も賭けなくなる。
            int curseWeight = option.Outcomes
                .Where(outcome => outcome.effectType == QuestChoiceEffectType.AdventurerSkill
                    && outcome.Skill != null && IsCurse(outcome.Skill))
                .Sum(outcome => Math.Max(0, outcome.weight));
            int totalWeight = option.Outcomes.Sum(outcome => Math.Max(0, outcome.weight));
            Assert.True(curseWeight * 2 <= totalWeight,
                $"{choiceEvent.id}: 選択肢「{option.text}」は呪いを引く確率が高すぎる"
                + $"（{curseWeight}/{totalWeight}）");
        }
    }

    [Fact]
    public void ACurseIsPermanentAndStaysWithTheOneWhoTookIt()
    {
        var db = Load();
        var curse = db.skills["skill_curse_frayedNerve"];
        var adventurer = new AdventurerData(new AdventurerMasterData
        {
            id = "adv_curse_probe", baseName = "試験体",
            vitality = 10, mental = 10, strength = 10, agility = 10,
            intelligence = 10, constitution = 10,
        });

        Assert.True(adventurer.LearnPermanentSkill(curse));
        Assert.Contains(curse, adventurer.AllLearnedSkills);

        // 二度目は積み増さない（同じ呪いを何度も引いても重ねがけにはならない）。
        Assert.False(adventurer.LearnPermanentSkill(curse));
        Assert.Single(adventurer.AllLearnedSkills, skill => skill.id == curse.id);
    }
}
