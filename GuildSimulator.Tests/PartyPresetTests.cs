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

/// <summary>
/// パーティ編成の保存と呼び出し。毎回1人ずつ選び直さずに済むことがこの機能の目的なので、
/// 「保存 → 呼び出し → そのまま出発」がひと続きで通ることまでを見る。
/// </summary>
[Collection("Console presentation")]
public class PartyPresetTests
{
    [Fact]
    public void SavingTheSameNameOverwritesInsteadOfEatingASlot()
    {
        var guild = new GuildManager(startGold: 100);
        var scout = AddAdventurer(guild, "斥候");
        var priest = AddAdventurer(guild, "神官");

        var first = new AdventurerData?[GuildManager.FormationSlotCount];
        first[0] = scout;
        Assert.True(guild.TrySavePartyPreset("主力", first, ExpeditionPolicy.SurvivalFirst, out _));

        var second = new AdventurerData?[GuildManager.FormationSlotCount];
        second[0] = scout;
        second[3] = priest;
        Assert.True(guild.TrySavePartyPreset("主力", second, ExpeditionPolicy.ObjectiveFirst, out _));

        var preset = Assert.Single(guild.partyPresets);
        Assert.Equal("主力", preset.name);
        Assert.Equal(2, preset.MemberCount);
        Assert.Equal(priest.id, preset.memberIds[3]);
        Assert.Equal(ExpeditionPolicy.ObjectiveFirst, preset.policy);
    }

    [Fact]
    public void SavingRefusesAnEmptyPartyAndStopsAtTheLimit()
    {
        var guild = new GuildManager(startGold: 100);
        var member = AddAdventurer(guild, "斥候");
        var formation = new AdventurerData?[GuildManager.FormationSlotCount];
        formation[0] = member;

        Assert.False(guild.TrySavePartyPreset(
            "空編成",
            new AdventurerData?[GuildManager.FormationSlotCount],
            ExpeditionPolicy.SurvivalFirst,
            out string emptyReason));
        Assert.Contains("空", emptyReason);

        for (int i = 0; i < GuildManager.PartyPresetLimit; i++)
            Assert.True(guild.TrySavePartyPreset(
                $"編成{i}", formation, ExpeditionPolicy.SurvivalFirst, out _));

        Assert.False(guild.TrySavePartyPreset(
            "あふれる編成", formation, ExpeditionPolicy.SurvivalFirst, out string fullReason));
        Assert.Contains($"{GuildManager.PartyPresetLimit}件", fullReason);

        Assert.True(guild.RemovePartyPreset(guild.partyPresets[0]));
        Assert.True(guild.TrySavePartyPreset(
            "あふれる編成", formation, ExpeditionPolicy.SurvivalFirst, out _));
    }

    [Fact]
    public void SaveThenLoadKeepsPresetsAndDropsMembersWhoLeftTheGuild()
    {
        var db = MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var guild = new GuildManager(startGold: 300, startRank: 1);
        var questManager = new QuestManager(guild);

        var masters = db.allAdventurers.Where(a => a.recruitGuildRank <= 1).Take(2).ToList();
        var stays = new AdventurerData(masters[0]);
        var leaves = new AdventurerData(masters[1]);
        guild.AddAdventurer(stays);
        guild.AddAdventurer(leaves);

        var formation = new AdventurerData?[GuildManager.FormationSlotCount];
        formation[0] = stays;
        formation[4] = leaves;
        Assert.True(guild.TrySavePartyPreset("主力", formation, ExpeditionPolicy.ObjectiveFirst, out _));
        guild.RecordLastParty(formation, ExpeditionPolicy.ObjectiveFirst);

        // 解雇された隊員は二度と戻らないので、保存済み編成からも落とす。
        Assert.True(guild.TryDismissAdventurer(leaves, out _));

        string json = SaveManager.Serialize(guild, questManager, currentTurn: 3, new List<AdventurerMasterData>());
        var loaded = SaveManager.Deserialize(json, db);

        var preset = Assert.Single(loaded.Guild.partyPresets);
        Assert.Equal("主力", preset.name);
        Assert.Equal(ExpeditionPolicy.ObjectiveFirst, preset.policy);
        Assert.Equal(stays.id, preset.memberIds[0]);
        Assert.Null(preset.memberIds[4]);

        Assert.NotNull(loaded.Guild.lastParty);
        Assert.Equal(stays.id, loaded.Guild.lastParty!.memberIds[0]);
    }

    [Fact]
    public async Task PickingAMemberPlacesThemByWeaponRangeAndTheNamedPartyIsStored()
    {
        var guild = new GuildManager(startGold: 100);
        var questManager = new QuestManager(guild);
        var priest = AddAdventurer(guild, "神官", HealingWeapon());
        var fighter = AddAdventurer(guild, "剣士", PhysicalWeapon());
        questManager.questBoard.Add(new QuestBoardEntry(SimpleQuest(), postedTurn: 1));

        // クエスト選択 → 受注確認 → 神官を追加 → 剣士を追加 → 保存（名前入力） → 受注をやめる → ボードを閉じる
        string text = await CaptureConsoleAsync(
            "1\ny\n1\n1\ns\n主力\n0\n0\n",
            () => QuestBoardScreen.ShowAsync(questManager, guild, currentTurn: 1));

        Assert.Contains("後衛1 : 神官", text);
        Assert.Contains("前衛1 : 剣士", text);
        Assert.Contains("編成「主力」を保存しました", text);

        var preset = Assert.Single(guild.partyPresets);
        Assert.Equal(fighter.id, preset.memberIds[0]);
        Assert.Equal(priest.id, preset.memberIds[3]);
    }

    [Fact]
    public async Task CallingUpASavedPartyStartsTheQuestWithoutPickingMembersAgain()
    {
        var guild = new GuildManager(startGold: 100);
        var questManager = new QuestManager(guild);
        var priest = AddAdventurer(guild, "神官", HealingWeapon());
        var fighter = AddAdventurer(guild, "剣士", PhysicalWeapon());
        questManager.questBoard.Add(new QuestBoardEntry(SimpleQuest(), postedTurn: 1));

        var saved = new AdventurerData?[GuildManager.FormationSlotCount];
        saved[0] = fighter;
        saved[3] = priest;
        Assert.True(guild.TrySavePartyPreset("主力", saved, ExpeditionPolicy.ObjectiveFirst, out _));

        // クエスト選択 → 受注確認 → 呼び出し → 「主力」 → この編成で進む → 受注確認 → 続ける → ボードを閉じる
        string text = await CaptureConsoleAsync(
            "1\ny\nl\n1\nd\ny\n\n0\n",
            () => QuestBoardScreen.ShowAsync(questManager, guild, currentTurn: 1));

        Assert.Contains("遠征方針: 依頼達成優先", text);
        Assert.Contains("受注しました", text);

        var run = Assert.Single(questManager.activeQuests);
        Assert.Equal(fighter.id, run.formation[0]?.id);
        Assert.Equal(priest.id, run.formation[3]?.id);
        Assert.Equal(ExpeditionPolicy.ObjectiveFirst, run.policy);

        // 保存し忘れても次回に呼び出せるよう、出発した編成は控えられている。
        Assert.NotNull(guild.lastParty);
        Assert.Equal(fighter.id, guild.lastParty!.memberIds[0]);
    }

    [Fact]
    public async Task CallingUpAPartyLeavesOutMembersWhoCannotGoAndSaysSo()
    {
        var guild = new GuildManager(startGold: 100);
        var questManager = new QuestManager(guild);
        var priest = AddAdventurer(guild, "神官", HealingWeapon());
        var fighter = AddAdventurer(guild, "剣士", PhysicalWeapon());
        questManager.questBoard.Add(new QuestBoardEntry(SimpleQuest(), postedTurn: 1));

        var saved = new AdventurerData?[GuildManager.FormationSlotCount];
        saved[0] = fighter;
        saved[3] = priest;
        Assert.True(guild.TrySavePartyPreset("主力", saved, ExpeditionPolicy.SurvivalFirst, out _));
        priest.isAlive = false;

        string text = await CaptureConsoleAsync(
            "1\ny\nl\n1\n0\n0\n",
            () => QuestBoardScreen.ShowAsync(questManager, guild, currentTurn: 1));

        Assert.Contains("外れたメンバー: 神官", text);
        Assert.Contains("前衛1 : 剣士", text);
    }

    static QuestMasterData SimpleQuest() => new()
    {
        id = "quest_preset_test",
        questName = "見回り",
        totalPhases = 3,
        Dungeon = new DungeonMasterData { dungeonName = "近郊の森" },
    };

    static AdventurerData AddAdventurer(
        GuildManager guild, string name, EquipmentMasterData? weapon = null)
    {
        var adv = new AdventurerData(new AdventurerMasterData
        {
            id = name,
            baseName = name,
            defaultLevel = 1,
            defaultRank = 1,
            vitality = 10,
            mental = 10,
            strength = 10,
            agility = 10,
            intelligence = 10,
            constitution = 10,
            appearance = 10,
            DefaultWeapon = weapon,
        });
        guild.AddAdventurer(adv);
        return adv;
    }

    static EquipmentMasterData PhysicalWeapon() => new()
    {
        id = "sword",
        displayName = "剣",
        type = EquipmentType.Weapon,
        weaponType = WeaponType.Sword,
        attackKind = AttackKind.Physical,
        damageDice = "1d6",
        bonus = new StatBlock(),
    };

    static EquipmentMasterData HealingWeapon() => new()
    {
        id = "staff",
        displayName = "回復杖",
        type = EquipmentType.Weapon,
        weaponType = WeaponType.Light,
        attackKind = AttackKind.Heal,
        healPower = 1f,
        bonus = new StatBlock(),
    };

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
