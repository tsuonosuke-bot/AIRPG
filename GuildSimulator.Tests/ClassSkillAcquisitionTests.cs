using GuildSimulator.Cli;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Game.Data;
using GuildSimulator.Game.Presentation;
using GuildSimulator.Game.Screens;
using Xunit;

namespace GuildSimulator.Tests;

public class ClassSkillAcquisitionTests
{
    [Fact]
    public void QuestClearAddsAVisibleReportWhenAClassSkillUnlocks()
    {
        var skill = new SkillMasterData { id = "skill_notice", skillName = "気づける新技" };
        var cls = new ClassMasterData { id = "class_notice", className = "通知職" };
        cls.classSkills.Add(new ClassSkillEntry
        {
            skillId = skill.id,
            Skill = skill,
            requiredClearCount = 1,
        });

        var adventurer = new AdventurerData(Master("notice", "習得者", cls));
        var guild = new GuildManager(startGold: 100);
        guild.AddAdventurer(adventurer);
        var manager = new QuestManager(guild);
        var quest = new QuestMasterData
        {
            id = "quest_notice",
            questName = "修練依頼",
            rank = Rank.Min,
            totalPhases = 1,
            phasesPerTurn = 1,
        };

        Assert.True(manager.TryStartQuest(
            quest,
            new AdventurerData?[] { adventurer, null, null, null, null, null },
            currentTurn: 1,
            out var error), error);

        manager.AdvanceAll(currentTurn: 2);
        var run = Assert.Single(manager.activeQuests);

        Assert.Contains(skill, adventurer.Skills);
        Assert.Contains(run.logs, line =>
            line.Contains("[スキル習得]") && line.Contains(adventurer.name) && line.Contains(skill.skillName));
        var report = Assert.Single(run.reportEvents, entry => entry.title == "スキル習得");
        Assert.True(report.important);
        Assert.Equal(adventurer.name, report.actorName);
        Assert.Contains("通知職習熟度 1", report.detail);

        manager.AdvanceAll(currentTurn: 3);
        Assert.Single(run.reportEvents, entry => entry.title == "スキル習得");
    }

    static AdventurerMasterData Master(string id, string name, ClassMasterData cls) => new()
    {
        id = id,
        baseName = name,
        defaultLevel = 1,
        defaultRank = Rank.Min,
        vitality = 10,
        mental = 10,
        strength = 10,
        agility = 10,
        intelligence = 10,
        constitution = 10,
        DefaultClass = cls,
        defaultClassId = cls.id,
    };
}

[Collection("Console presentation")]
public class ClassSkillPresentationTests
{
    [Fact]
    public async Task SkillDetailsShowEffectiveConditionalReplacedAndEventSkills()
    {
        var lv1 = new SkillMasterData
        {
            id = "skill_history_lv1",
            skillName = "剣技 Lv1",
            family = "history_sword",
            level = 1,
            add = new StatBlock { pv = 1 },
        };
        var lv2 = new SkillMasterData
        {
            id = "skill_history_lv2",
            skillName = "剣技 Lv2",
            family = "history_sword",
            level = 2,
            add = new StatBlock { pv = 2 },
        };
        var shieldSkill = new SkillMasterData
        {
            id = "skill_needs_shield",
            skillName = "盾の心得",
            requireShield = true,
            add = new StatBlock { av = 1 },
        };
        var eventSkill = new SkillMasterData
        {
            id = "skill_event",
            skillName = "迷宮のひらめき",
            add = new StatBlock { dv = 1 },
        };
        var cls = new ClassMasterData { id = "class_history", className = "履歴職" };
        cls.classSkills.Add(new ClassSkillEntry
        {
            skillId = lv1.id, Skill = lv1, requiredClearCount = 0,
        });
        cls.classSkills.Add(new ClassSkillEntry
        {
            skillId = lv2.id, Skill = lv2, requiredClearCount = 1,
        });
        cls.classSkills.Add(new ClassSkillEntry
        {
            skillId = shieldSkill.id, Skill = shieldSkill, requiredClearCount = 0,
        });

        var master = new AdventurerMasterData
        {
            id = "history",
            baseName = "技能者",
            defaultLevel = 1,
            defaultRank = Rank.Min,
            vitality = 10,
            mental = 10,
            strength = 10,
            agility = 10,
            intelligence = 10,
            constitution = 10,
            DefaultClass = cls,
            defaultClassId = cls.id,
        };
        var adventurer = new AdventurerData(master);
        adventurer.OnClearQuest(Rank.Min);
        adventurer.LearnPermanentSkill(eventSkill);
        var guild = new GuildManager(startGold: 100);
        guild.AddAdventurer(adventurer);

        string text = await CaptureConsoleAsync(
            "1\ns\n\n0\n0\n",
            () => AdventurerScreen.ShowAsync(new GameMasterData(), guild));

        Assert.Contains("習得スキル詳細: 技能者", text);
        Assert.Contains("○ 剣技 Lv2  [有効]  習得元: 履歴職", text);
        Assert.Contains("× 盾の心得  [装備条件未達]  習得元: 履歴職", text);
        Assert.Contains("▽ 剣技 Lv1  [上位Lvに置換]  習得元: 履歴職", text);
        Assert.Contains("○ 迷宮のひらめき  [有効]  習得元: 固有・イベント", text);
        Assert.Contains("条件: 盾", text);
    }

    static async Task<string> CaptureConsoleAsync(string inputText, Func<Task> action)
    {
        var originalIn = Console.In;
        var originalOut = Console.Out;
        using var input = new StringReader(inputText);
        using var output = new StringWriter();
        try
        {
            Console.SetIn(input);
            Console.SetOut(output);
            Ui.Use(new ConsoleGameIo());
            await action();
            return output.ToString();
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
        }
    }
}
