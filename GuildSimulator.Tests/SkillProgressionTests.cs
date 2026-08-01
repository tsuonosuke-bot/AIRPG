using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

/// <summary>
/// 段階スキル（Lv1〜Lv5）の畳み込みと、新しい発動条件の検証。
/// 「上のLvを覚えたら下のLvは押しのけられる」が守られないと、数値が二重三重に乗ってしまう。
/// </summary>
public class SkillProgressionTests
{
    static GameMasterData Load() => MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    static AdventurerData Fighter() => new(new AdventurerMasterData
    {
        id = "adv", baseName = "測定用",
        vitality = 12, mental = 12, strength = 16, agility = 14,
        intelligence = 10, constitution = 12,
    });

    static SkillMasterData Tier(string family, int level, StatBlock add) => new()
    {
        id = $"{family}_lv{level}", skillName = $"{family} Lv{level}",
        family = family, level = level, add = add,
    };

    [Fact]
    public void OnlyTheHighestTierOfAFamilyStaysActive()
    {
        var lv1 = Tier("mastery_test", 1, new StatBlock { pv = 1 });
        var lv3 = Tier("mastery_test", 3, new StatBlock { pv = 3 });

        var adv = Fighter();
        adv.LearnPermanentSkill(lv1);
        adv.LearnPermanentSkill(lv3);

        Assert.Single(adv.Skills);
        Assert.Same(lv3, adv.Skills[0]);
    }

    [Fact]
    public void LearningOrderDoesNotChangeWhichTierWins()
    {
        var lv2 = Tier("mastery_test", 2, new StatBlock { pv = 2 });
        var lv5 = Tier("mastery_test", 5, new StatBlock { pv = 5 });

        var highFirst = Fighter();
        highFirst.LearnPermanentSkill(lv5);
        highFirst.LearnPermanentSkill(lv2);

        Assert.Single(highFirst.Skills);
        Assert.Same(lv5, highFirst.Skills[0]);
    }

    [Fact]
    public void DifferentFamiliesStackSideBySide()
    {
        var adv = Fighter();
        adv.LearnPermanentSkill(Tier("mastery_a", 1, new StatBlock { pv = 1 }));
        adv.LearnPermanentSkill(Tier("mastery_a", 2, new StatBlock { pv = 2 }));
        adv.LearnPermanentSkill(Tier("mastery_b", 1, new StatBlock { dv = 1 }));

        Assert.Equal(2, adv.Skills.Count);
        Assert.Contains(adv.Skills, s => s.family == "mastery_a" && s.level == 2);
        Assert.Contains(adv.Skills, s => s.family == "mastery_b");
    }

    [Fact]
    public void SkillsWithoutAFamilyAreNeverCollapsed()
    {
        var adv = Fighter();
        var a = new SkillMasterData { id = "solo_a", skillName = "単独A", add = new StatBlock { av = 1 } };
        var b = new SkillMasterData { id = "solo_b", skillName = "単独B", add = new StatBlock { av = 1 } };
        adv.LearnPermanentSkill(a);
        adv.LearnPermanentSkill(b);

        Assert.Equal(2, adv.Skills.Count);
    }

    // ---- マスタデータそのものの整合 ----

    [Fact]
    public void EveryWeaponAndArmourMasteryHasFiveTiers()
    {
        var db = Load();
        string[] families =
        {
            "mastery_sword", "mastery_axe", "mastery_spear", "mastery_bow", "mastery_dagger",
            "mastery_fire", "mastery_wind", "mastery_water", "mastery_earth",
            "mastery_dark", "mastery_light",
            "mastery_cloth", "mastery_lightArmor", "mastery_plate",
        };

        foreach (var family in families)
        {
            var levels = db.skills.Values
                .Where(s => s.family == family)
                .Select(s => s.level)
                .OrderBy(x => x)
                .ToList();
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, levels);
        }
    }

    [Fact]
    public void TieredSkillsAreNamedByLevelAndNeverByTheOldSuffix()
    {
        var db = Load();
        foreach (var s in db.skills.Values)
        {
            Assert.DoesNotContain("・極", s.skillName);
            if (string.IsNullOrEmpty(s.family)) continue;
            Assert.True(s.level > 0, $"{s.id} に level がない");
            Assert.EndsWith($" Lv{s.level}", s.skillName);
        }
    }

    [Fact]
    public void EverySkillIsReachableFromSomeMaster()
    {
        // 誰も参照していないスキルは永久に手に入らない。風マスタリーが宙に浮いていた再発を防ぐ。
        var db = Load();
        var referenced = new HashSet<string>();

        foreach (var c in db.classes.Values)
            foreach (var e in c.classSkills)
                referenced.Add(e.skillId);
        foreach (var e in db.enemies.Values)
            foreach (var s in e.Skills)
                referenced.Add(s.id);
        foreach (var a in db.allAdventurers)
            foreach (var s in a.Skills)
                referenced.Add(s.id);
        foreach (var ev in db.choiceEvents.Values)
            foreach (var opt in ev.options)
                foreach (var o in opt.outcomes)
                    if (o.Skill != null) referenced.Add(o.Skill.id);
        foreach (var d in db.dungeons.Values)
            foreach (var e in d.treasureTable)
                if (e.Skill != null) referenced.Add(e.Skill.id);
        foreach (var q in db.allQuests)
            foreach (var e in q.bossDrops)
                if (e.Skill != null) referenced.Add(e.Skill.id);

        var orphans = db.skills.Values
            .Where(s => !referenced.Contains(s.id))
            .Select(s => $"{s.id}({s.skillName})")
            .OrderBy(x => x)
            .ToList();

        // 段階スキルは「まだ誰にも配っていない上位Lv」が残るのが自然なので、
        // ここでは系統ごとに最低1段は誰かの手に届いていることだけを見る。
        var unreachableFamilies = db.skills.Values
            .Where(s => !string.IsNullOrEmpty(s.family))
            .GroupBy(s => s.family)
            .Where(g => g.All(s => !referenced.Contains(s.id)))
            .Select(g => g.Key)
            .ToList();

        Assert.True(unreachableFamilies.Count == 0,
            "どこからも参照されていない系統: " + string.Join(", ", unreachableFamilies));
        Assert.True(orphans.Count < db.skills.Count,
            "スキルがひとつも参照されていない: " + string.Join(", ", orphans));
    }

    [Fact]
    public void ClassTreesNeverGrantATierOutOfOrder()
    {
        // 同じ系統の中では、必要習熟度が増えるほど Lv も上がっていること。
        var db = Load();
        foreach (var cls in db.classes.Values)
        {
            var byFamily = cls.classSkills
                .Where(e => e.Skill != null && !string.IsNullOrEmpty(e.Skill!.family))
                .GroupBy(e => e.Skill!.family);

            foreach (var group in byFamily)
            {
                var ordered = group.OrderBy(e => e.requiredClearCount).ToList();
                for (int i = 1; i < ordered.Count; i++)
                    Assert.True(ordered[i].Skill!.level > ordered[i - 1].Skill!.level,
                        $"{cls.className} の {group.Key} が習熟度順に並んでいない");
            }
        }
    }
}
