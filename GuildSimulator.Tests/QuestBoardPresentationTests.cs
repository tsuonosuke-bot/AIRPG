using GuildSimulator.Cli;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Game.Data;
using GuildSimulator.Game.Presentation;
using GuildSimulator.Game.Screens;
using Xunit;

namespace GuildSimulator.Tests;

[Collection("Console presentation")]
public class QuestBoardPresentationTests
{
    [Fact]
    public async Task SummaryShowsGatherHuntAndTraversalObjectives()
    {
        var guild = new GuildManager(startGold: 100);
        var manager = new QuestManager(guild);
        manager.questBoard.Add(new QuestBoardEntry(new QuestMasterData
        {
            id = "gather",
            questName = "薬草集め",
            gatherItemName = "薬草",
            gatherTargetCount = 3,
        }, postedTurn: 1));
        manager.questBoard.Add(new QuestBoardEntry(HuntQuest(), postedTurn: 1));
        manager.questBoard.Add(new QuestBoardEntry(new QuestMasterData
        {
            id = "traversal",
            questName = "街道の踏破",
            totalPhases = 8,
            Dungeon = new DungeonMasterData { dungeonName = "北の街道" },
        }, postedTurn: 1));

        string text = await CaptureConsoleAsync(
            "0\n",
            () => QuestBoardScreen.ShowAsync(manager, guild, currentTurn: 1));

        Assert.Contains("【F】【採取】薬草集め", text);
        Assert.Contains("達成条件: 薬草×3", text);
        Assert.Contains("【F】【討伐】混成部隊の討伐", text);
        Assert.Contains("達成条件: ゴブリン兵士×1、ゴブリン×2（計3体）", text);
        Assert.Contains("【F】【踏破】街道の踏破", text);
        Assert.Contains("達成条件: 北の街道・8エリア", text);
        Assert.Contains("危険度目安:", text);
        Assert.Contains("5段階中", text);
        Assert.DoesNotContain("スコア", text);
    }

    [Fact]
    public async Task DetailNamesEveryRequiredMonsterAndSeparatesRankFromDanger()
    {
        var guild = new GuildManager(startGold: 100);
        var manager = new QuestManager(guild);
        var db = MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var quest = db.allQuests.Single(candidate => candidate.id == "quest_promotion_1");
        manager.questBoard.Add(new QuestBoardEntry(quest, postedTurn: 1));

        string text = await CaptureConsoleAsync(
            "1\nn\n0\n",
            () => QuestBoardScreen.ShowAsync(manager, guild, currentTurn: 1));

        Assert.Contains("種別: 討伐", text);
        Assert.Contains("達成条件: ゴブリン兵士×1、ゴブリン×2（合計3体）を討伐", text);
        Assert.Contains("依頼ランク: F", text);
        Assert.Contains("危険度目安: 標準（5段階中3）", text);
        Assert.Contains("討伐対象脅威:E（最終エリアで確定戦闘）", text);
        Assert.DoesNotContain("ボス:E/3体", text);
        Assert.DoesNotContain("スコア", text);
    }

    [Fact]
    public void PartyPreviewWarnsAboutAnEmptyFrontAndRearMeleePlacement()
    {
        var formation = new AdventurerData?[6];
        formation[3] = AdventurerWithWeapon("剣士", PhysicalWeapon());
        formation[4] = AdventurerWithWeapon("神官", HealingWeapon());

        string text = CaptureConsole(() =>
            QuestBoardScreen.ShowPartyPreview(formation, BossQuest()));

        Assert.Contains("配置役割: 前衛 0人 / 後衛 2人 / 回復役 1人", text);
        Assert.Contains("⚠ 前衛不在: 後衛への攻撃を遮る隊員がいません", text);
        Assert.Contains($"⚠ 後衛の近接役: 剣士（命中-{BattleResolver.REAR_MELEE_TO_HIT_PENALTY}）", text);
        Assert.DoesNotContain("回復役不在", text);
    }

    [Fact]
    public void PartyPreviewWarnsAboutMissingHealingForAFullBossParty()
    {
        var formation = new AdventurerData?[6];
        formation[0] = AdventurerWithWeapon("前衛1", PhysicalWeapon());
        formation[1] = AdventurerWithWeapon("前衛2", PhysicalWeapon());
        formation[2] = AdventurerWithWeapon("前衛3", PhysicalWeapon());

        string text = CaptureConsole(() =>
            QuestBoardScreen.ShowPartyPreview(formation, BossQuest()));

        Assert.Contains("配置役割: 前衛 3人 / 後衛 0人 / 回復役 0人", text);
        Assert.Contains("⚠ 回復役不在:", text);
        Assert.DoesNotContain("⚠ 前衛不在:", text);
    }

    [Fact]
    public void PartyPreviewDoesNotOverWarnAQuietSoloExpeditionWithoutHealing()
    {
        var formation = new AdventurerData?[6];
        formation[0] = AdventurerWithWeapon("斥候", PhysicalWeapon());
        var quest = new QuestMasterData
        {
            id = "quiet",
            questName = "安全な踏破",
            totalPhases = 3,
            Dungeon = new DungeonMasterData(),
        };

        string text = CaptureConsole(() =>
            QuestBoardScreen.ShowPartyPreview(formation, quest));

        Assert.Contains("配置役割: 前衛 1人 / 後衛 0人 / 回復役 0人", text);
        Assert.DoesNotContain("⚠ 回復役不在:", text);
    }

    static QuestMasterData HuntQuest()
    {
        var soldier = new EnemyMasterData
        {
            id = "goblin-soldier",
            baseName = "ゴブリン兵士",
        };
        var goblin = new EnemyMasterData
        {
            id = "goblin",
            baseName = "ゴブリン",
        };
        return new QuestMasterData
        {
            id = "hunt",
            questName = "混成部隊の討伐",
            totalPhases = 5,
            BossEnemy = new EnemyUnitTemplate
            {
                unitName = "ゴブリン混成部隊",
                Formation = new List<EnemyMasterData?>
                {
                    soldier,
                    goblin,
                    goblin,
                    null,
                    null,
                    null,
                },
            },
        };
    }

    static QuestMasterData BossQuest()
    {
        var boss = new EnemyMasterData
        {
            id = "boss",
            baseName = "大物",
            threat = 1,
        };
        return new QuestMasterData
        {
            id = "boss-quest",
            questName = "討伐任務",
            rank = 1,
            totalPhases = 1,
            bossPhase = 1,
            BossEnemy = new EnemyUnitTemplate
            {
                Formation = new List<EnemyMasterData?> { boss },
            },
        };
    }

    static AdventurerData AdventurerWithWeapon(string name, EquipmentMasterData weapon) =>
        new(new AdventurerMasterData
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

    static string CaptureConsole(Action action)
    {
        var originalOut = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Ui.Use(new ConsoleGameIo());
            action();
            return output.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
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
