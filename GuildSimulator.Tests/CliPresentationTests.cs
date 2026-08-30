using GuildSimulator.Cli;
using GuildSimulator.Game.Data;
using GuildSimulator.Game.Presentation;
using GuildSimulator.Game.Screens;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using Xunit;

namespace GuildSimulator.Tests;

[CollectionDefinition("Console presentation", DisableParallelization = true)]
public sealed class ConsolePresentationCollection
{
}

[Collection("Console presentation")]
public class CliPresentationTests
{
    [Fact]
    public async Task RetreatReportNamesActualCasualtiesAndShowsSettlementSummary()
    {
        var survivor = new AdventurerData(Master("survivor", "生還者"));
        var casualty = new AdventurerData(Master("casualty", "戦没者"))
        {
            isAlive = false,
        };
        var guild = new GuildManager(startGold: 100);
        guild.AddAdventurer(survivor);
        guild.AddAdventurer(casualty);
        var manager = new QuestManager(guild);
        var quest = new QuestMasterData
        {
            id = "retreat",
            questName = "撤退テスト",
            totalPhases = 10,
            rewardGold = 120,
        };
        var run = new QuestRun(quest, startedTurn: 1)
        {
            currentPhase = 5,
            retreated = true,
            retreatReason = ExpeditionRetreatReason.SurvivalPolicy,
            guildUpkeepAtStart = guild.EffectiveUpkeepPerTurn,
        };
        run.formation[0] = survivor;
        run.formation[1] = casualty;
        run.startingLevels[survivor.id] = survivor.level;
        run.startingLevels[casualty.id] = casualty.level;
        Assert.True(survivor.AddExperience(
            survivor.RequiredExpForNextLevel,
            out var levelUps,
            out var grownStats));
        Assert.Equal(1, levelUps);
        run.RecordLevelGrowth(survivor.id, grownStats);
        manager.RestoreState(new(), new() { run }, Array.Empty<string>());

        var originalIn = Console.In;
        var originalOut = Console.Out;
        using var input = new StringReader("\ny\n\n");
        using var output = new StringWriter();
        try
        {
            Console.SetIn(input);
            Console.SetOut(output);

            Ui.Use(new ConsoleGameIo());
            await ActiveQuestScreen.HandleQuestAsync(run, manager, guild);
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
        }

        string text = output.ToString();
        Assert.Contains("死亡者: 戦没者", text);
        Assert.DoesNotContain("死亡者はいません", text);
        Assert.Contains("クエスト終了サマリー", text);
        Assert.Contains("結果: 撤退", text);
        Assert.Contains("生還優先の方針", text);
        Assert.Contains("+0G", text);
        Assert.Contains("撤退のため基本報酬138Gは不支給", text);
        Assert.DoesNotContain("活躍手当 18G", text);
        Assert.DoesNotContain("士気が尽き", text);
        Assert.Contains(
            $"生還者: Lv1 → Lv2（{AdventurerData.FormatGrownStats(grownStats)}）",
            text);
        Assert.Empty(manager.activeQuests);
    }

    [Fact]
    public async Task CompletionRewardsAreGroupedAndHideInternalLogDetails()
    {
        var adventurer = new AdventurerData(Master("reward-reader", "報酬確認者"));
        var guild = new GuildManager(startGold: 100);
        guild.AddAdventurer(adventurer);
        var manager = new QuestManager(guild);
        var quest = new QuestMasterData
        {
            id = "readable-rewards",
            questName = "報酬表示テスト",
            totalPhases = 1,
            rewardGold = 80,
            rewardGuildPoints = 16,
            rewardExp = adventurer.RequiredExpForNextLevel,
        };
        var run = new QuestRun(quest, startedTurn: 1)
        {
            currentPhase = 1,
            guildUpkeepAtStart = guild.EffectiveUpkeepPerTurn,
        };
        run.formation[0] = adventurer;
        run.startingLevels[adventurer.id] = adventurer.level;
        run.chests.Add(new TreasureChest
        {
            kind = TreasureChestKind.Boss,
            foundPhase = 1,
        });
        manager.RestoreState(new(), new() { run }, Array.Empty<string>());

        string text = await CaptureConsoleAsync(
            "\n\n",
            () => ActiveQuestScreen.HandleQuestAsync(run, manager, guild));

        Assert.Contains("── 獲得内訳 ──", text);
        Assert.Contains("【基本報酬】", text);
        Assert.Contains("資金", text);
        Assert.Contains("+92G", text);
        Assert.Contains("（基本 80G + 活躍手当 12G）", text);
        Assert.Contains("ギルドポイント", text);
        Assert.Contains("【経験値】", text);
        Assert.Contains("報酬確認者", text);
        Assert.Contains($"+{(int)Math.Floor(quest.rewardExp * QuestRewardService.BaseExpRewardMultiplier)}", text);
        Assert.Contains("【宝箱・戦利品】", text);
        Assert.Contains("ボスの宝箱", text);
        Assert.Contains("空っぽ", text);
        Assert.Contains("報酬確認者: Lv1 → Lv2", text);
        Assert.Equal(1, CountOccurrences(text, "Lv1 → Lv2"));
        Assert.DoesNotContain("レベルアップ", text);
        Assert.DoesNotContain("[完了]", text);
        Assert.Empty(manager.activeQuests);
    }

    [Fact]
    public async Task RecruitScreenRendersEachCandidateOnlyOnce()
    {
        var candidate = Master("candidate", "重複しない候補");
        var guild = new GuildManager(startGold: 100);

        string text = await CaptureConsoleAsync(
            "0\n",
            () => RecruitScreen.ShowAsync(
                new List<AdventurerMasterData> { candidate },
                guild,
                currentTurn: 1,
                new[] { candidate },
                maxCandidateCount: 3));

        Assert.Equal(1, CountOccurrences(text, candidate.baseName));
        Assert.Contains("VIT:", text);
        Assert.Contains("SIZ:", text);
        Assert.DoesNotContain("CON:", text);
    }

    [Fact]
    public async Task QuestBoardRendersQuestNumberAndNameOnlyOnce()
    {
        var guild = new GuildManager(startGold: 100);
        var manager = new QuestManager(guild);
        var quest = new QuestMasterData
        {
            id = "single",
            questName = "重複しない依頼",
            rewardGold = 50,
            rewardExp = 10,
        };
        manager.questBoard.Add(new QuestBoardEntry(quest, postedTurn: 1));

        string text = await CaptureConsoleAsync(
            "0\n",
            () => QuestBoardScreen.ShowAsync(manager, guild, currentTurn: 1));

        Assert.Equal(1, CountOccurrences(text, quest.questName));
        Assert.Contains("1. 【F】", text);
        Assert.DoesNotContain("1. 1. 【F】", text);
        Assert.Contains("基本報酬（活躍手当込み） 資金:58G 経験値:11", text);
        Assert.DoesNotContain("資金:50G", text);
        Assert.DoesNotContain("予想収支", text);
        Assert.DoesNotContain("宝箱・敵ドロップ・選択イベントは上の概算に含みません", text);
    }

    [Fact]
    public async Task QuestBoardShowsDetailScreenBeforeAcceptingQuest()
    {
        var guild = new GuildManager(startGold: 100);
        var manager = new QuestManager(guild);
        var quest = new QuestMasterData
        {
            id = "detail",
            questName = "詳細確認クエスト",
            description = "掲示板には出ない詳しい依頼内容",
            rewardGold = 50,
        };
        manager.questBoard.Add(new QuestBoardEntry(quest, postedTurn: 1));

        string text = await CaptureConsoleAsync(
            "1\nn\n0\n",
            () => QuestBoardScreen.ShowAsync(manager, guild, currentTurn: 1));

        Assert.Contains("掲示板には出ない詳しい依頼内容", text);
        Assert.Contains("このクエストを受注しますか？", text);
    }

    [Fact]
    public async Task QuestDetailsKeepVerboseLogsCollapsedByDefault()
    {
        var guild = new GuildManager(startGold: 100);
        var manager = new QuestManager(guild);
        var quest = new QuestMasterData
        {
            id = "active",
            questName = "ログ折り畳みテスト",
            totalPhases = 10,
        };
        var run = new QuestRun(quest, startedTurn: 1)
        {
            currentPhase = 1,
        };
        run.logs.Add("既定では隠れる戦闘計算ログ");
        manager.RestoreState(new(), new() { run }, Array.Empty<string>());

        string text = await CaptureConsoleAsync(
            "\n",
            () => ActiveQuestScreen.HandleQuestAsync(run, manager, guild));

        Assert.Contains("詳細ログを見る（全1件）", text);
        Assert.Contains("エリア: 1/10", text);
        Assert.DoesNotContain("Phase", text);
        Assert.DoesNotContain("フェーズ", text);
        Assert.DoesNotContain("詳細ログ (", text);
        Assert.DoesNotContain("既定では隠れる戦闘計算ログ", text);
    }

    [Fact]
    public async Task RecruitMinimumFacilityShowsItsActualEffect()
    {
        var db = MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var facility = db.facilities["fac_recruitment_office_01"];
        var guild = new GuildManager(startGold: 200);

        string text = await CaptureConsoleAsync(
            "0\n",
            () => FacilityScreen.ShowAsync(db, guild));

        Assert.Equal(1, facility.recruitMinBonus);
        Assert.Contains("雇入れ候補の最低人数+1", text);
        Assert.DoesNotContain("効果なし", text);
    }

    [Fact]
    public async Task EquippingShieldShowsItsIntrinsicEffectInChangeSummary()
    {
        var adventurer = new AdventurerData(new AdventurerMasterData
        {
            id = "shield_user",
            baseName = "盾役",
            defaultLevel = 1,
            defaultRank = 1,
            vitality = 10,
            mental = 8,
            strength = 10,
            agility = 8,
            intelligence = 6,
            constitution = 10,
        });
        var shield = new EquipmentMasterData
        {
            id = "test_shield",
            displayName = "テスト小盾",
            type = EquipmentType.Shield,
            blockChance = 25,
            blockAv = 4,
            weight = 3,
        };
        var guild = new GuildManager(startGold: 100);
        guild.AddAdventurer(adventurer);
        guild.AddEquipment(shield, 1);

        string text = await CaptureConsoleAsync(
            "1\ne\n2\n1\n\n0\n0\n0\n",
            () => AdventurerScreen.ShowAsync(new GameMasterData(), guild));

        Assert.Contains("ステータス・装備変化", text);
        Assert.Contains("装備効果: なし → [盾] 受け25% 受け成功時AV+4 重量3", text);
        Assert.DoesNotContain("ステータス・装備変化:\r\n── Enterで続ける", text);
    }

    [Fact]
    public async Task SkillChoiceEventShowsThreeSkillsAndTheSelectedMembersLearningState()
    {
        var skills = new[]
        {
            new SkillMasterData { id = "skill_choice_1", skillName = "野営の知恵" },
            new SkillMasterData { id = "skill_choice_2", skillName = "危険察知" },
            new SkillMasterData { id = "skill_choice_3", skillName = "獣道歩き" },
        };
        var choice = new QuestChoiceEventMasterData
        {
            id = "event_choice_ui",
            title = "森渡りの教え",
            description = "三つの技から一つを選ぶ。",
            options = skills.Select(skill => new QuestChoiceOptionData
            {
                text = $"「{skill.skillName}」を学ぶ",
                resultText = "技を学んだ。",
                effectType = QuestChoiceEffectType.AdventurerSkill,
                targetId = skill.id,
                targetsOneMember = true,
                Skill = skill,
            }).ToList(),
        };
        var adventurer = new AdventurerData(Master("choice", "選択者"));
        var guild = new GuildManager(startGold: 100);
        guild.AddAdventurer(adventurer);
        var manager = new QuestManager(guild);
        var run = new QuestRun(new QuestMasterData { id = "choice_ui", questName = "選択試験" }, 1)
        {
            pendingChoice = new PendingQuestChoice { Event = choice, createdTurn = 2 },
        };
        run.formation[0] = adventurer;

        string text = await CaptureConsoleAsync(
            "2\n1\n\n",
            () => ActiveQuestScreen.HandleQuestAsync(run, manager, guild));

        Assert.Contains("1. 「野営の知恵」を学ぶ", text);
        Assert.Contains("2. 「危険察知」を学ぶ", text);
        Assert.Contains("3. 「獣道歩き」を学ぶ", text);
        Assert.Contains("選んだ隊員がスキル「危険察知」を習得", text);
        Assert.Contains("「危険察知」未習得", text);
        Assert.Contains(skills[1], adventurer.AllLearnedSkills);
        Assert.Null(run.pendingChoice);
    }

    [Fact]
    public async Task CommercialEquipmentRemainsAvailableAfterPurchase()
    {
        var db = new GameMasterData();
        var item = new EquipmentMasterData
        {
            id = "test_sword",
            displayName = "テストの剣",
            type = EquipmentType.Weapon,
            price = 10,
        };
        db.equipment[item.id] = item;
        var guild = new GuildManager(startGold: 1000);
        guild.ReplaceShopStock(1, new Dictionary<string, int> { [item.id] = 1 }, new Dictionary<string, int>());

        string text = await CaptureConsoleAsync(
            "1\n1\ny\n\n\n0\n",
            () => ShopScreen.ShowAsync(db, guild, currentTurn: 1));

        Assert.Contains("テストの剣 を購入しました", text);
        Assert.Contains("[常備]", text);
        Assert.Equal(1, guild.shopEquipmentStock[item.id]);
    }

    [Fact]
    public async Task ShopRendersEachEquipmentOnlyOncePerPurchaseMenu()
    {
        var db = new GameMasterData();
        var item = new EquipmentMasterData
        {
            id = "single_shop_sword",
            displayName = "一度だけ表示される剣",
            type = EquipmentType.Weapon,
            price = 10,
        };
        db.equipment[item.id] = item;
        var guild = new GuildManager(startGold: 100);
        guild.ReplaceShopStock(1, new Dictionary<string, int> { [item.id] = 1 }, new Dictionary<string, int>());

        string text = await CaptureConsoleAsync(
            "1\n0\n0\n",
            () => ShopScreen.ShowAsync(db, guild, currentTurn: 1));

        Assert.Equal(1, CountOccurrences(text, item.displayName));
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

    [Fact]
    public async Task RecruitScreenShowsTheRosterCapAndRefusesToHireWhenItIsFull()
    {
        var guild = new GuildManager(startGold: 1_000);
        for (int i = 0; i < GuildManager.BaseRosterCapacity; i++)
            guild.AddAdventurer(new AdventurerData(Master($"member-{i}", $"在籍者{i}")));
        var candidate = Master("candidate", "応募者");
        var candidates = new List<AdventurerMasterData> { candidate };
        int goldBefore = guild.Gold;

        string text = await RenderRecruitScreenAsync(candidates, guild, "1\n\n0\n");

        Assert.Contains($"在籍冒険者: {GuildManager.BaseRosterCapacity}/{GuildManager.BaseRosterCapacity}人", text);
        Assert.Contains("在籍上限に達しています", text);
        Assert.Contains("[在籍上限]", text);
        Assert.Equal(goldBefore, guild.Gold);
        Assert.Equal(GuildManager.BaseRosterCapacity, guild.adventurers.Count);
        Assert.Contains(candidate, candidates);
    }

    [Fact]
    public async Task RecruitScreenQuotesTheRaisedHireCost()
    {
        var guild = new GuildManager(startGold: 1_000);
        var candidates = new List<AdventurerMasterData> { Master("candidate", "応募者") };

        string text = await RenderRecruitScreenAsync(candidates, guild, "0\n");

        // Lv1のCommonは 55G の1.5倍。表示と実際の支払いは同じ計算を通す。
        Assert.Equal(83, RecruitScreen.CalcHireCost(candidates[0]));
        Assert.Contains("雇用費: 83G", text);
    }

    static async Task<string> RenderRecruitScreenAsync(
        List<AdventurerMasterData> candidates,
        GuildManager guild,
        string keystrokes)
    {
        var originalIn = Console.In;
        var originalOut = Console.Out;
        using var input = new StringReader(keystrokes);
        using var output = new StringWriter();
        try
        {
            Console.SetIn(input);
            Console.SetOut(output);

            Ui.Use(new ConsoleGameIo());
            await RecruitScreen.ShowAsync(
                candidates,
                guild,
                currentTurn: 1,
                candidates,
                maxCandidateCount: 3);
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
        }
        return output.ToString();
    }

    static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    static AdventurerMasterData Master(string id, string name) => new()
    {
        id = id,
        baseName = name,
        defaultLevel = 1,
        defaultRank = 1,
    };
}
