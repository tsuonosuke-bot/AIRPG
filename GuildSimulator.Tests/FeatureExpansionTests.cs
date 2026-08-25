using GuildSimulator.Game.Data;
using GuildSimulator.Core;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using Xunit;

namespace GuildSimulator.Tests;

[Collection("Guild static state")]
public class FeatureExpansionTests
{
    [Fact]
    public void ShopAlwaysCarriesEveryUnlockedCommercialItem()
    {
        var guild = new GuildManager();
        var equipment = Enumerable.Range(1, 10).Select(i => new EquipmentMasterData
        {
            id = $"eq_{i}", displayName = $"装備{i}", price = 10,
        }).ToList();
        var items = Enumerable.Range(1, 5).Select(i => new ConsumableMasterData
        {
            id = $"item_{i}", displayName = $"道具{i}", price = 10,
        }).ToList();

        Assert.True(ShopService.RefreshIfNeeded(guild, 1, equipment, items));
        Assert.Equal(equipment.Select(e => e.id).Order(), guild.shopEquipmentStock.Keys.Order());
        Assert.Equal(items.Select(i => i.id).Order(), guild.shopConsumableStock.Keys.Order());
        Assert.False(ShopService.RefreshIfNeeded(guild, 5, equipment, items));
        Assert.Equal(5, guild.LastShopRefreshTurn);
    }

    [Fact]
    public void ShopStockAlwaysIncludesEveryUnlockedWeaponAndArmor()
    {
        var guild = new GuildManager();
        var lowestArmorIds = new[] { "eq_armor_low_1", "eq_armor_low_2" };
        var equipment = new List<EquipmentMasterData>
        {
            new() { id = lowestArmorIds[0], displayName = "低ランク防具1", type = EquipmentType.Armor, shopTier = 1, price = 10 },
            new() { id = lowestArmorIds[1], displayName = "低ランク防具2", type = EquipmentType.Armor, shopTier = 1, price = 10 },
        };
        equipment.AddRange(Enumerable.Range(1, 10).Select(i => new EquipmentMasterData
        {
            id = $"eq_other_{i}", displayName = $"装備{i}", type = EquipmentType.Weapon, shopTier = 1, price = 10,
        }));
        var items = Enumerable.Range(1, 5).Select(i => new ConsumableMasterData
        {
            id = $"item_{i}", displayName = $"道具{i}", price = 10,
        }).ToList();

        for (int turn = 1; turn <= 30; turn += 5)
        {
            ShopService.RefreshIfNeeded(guild, turn, equipment, items);
            foreach (var id in lowestArmorIds)
            {
                Assert.True(guild.shopEquipmentStock.ContainsKey(id), $"turn {turn} で {id} が売り出されていない");
            }
            Assert.All(equipment, e => Assert.True(guild.shopEquipmentStock.ContainsKey(e.id)));
        }
    }

    [Fact]
    public void CarriedConsumableIsSpentAndAppliesForTheQuest()
    {
        var guild = new GuildManager();
        var adventurer = new AdventurerData(BasicAdventurer());
        guild.AddAdventurer(adventurer);
        var tonic = new ConsumableMasterData
        {
            id = "tonic",
            displayName = "生命の霊薬",
            effectType = ConsumableEffectType.MaxHpPercent,
            effectValue = 20,
        };
        guild.AddConsumable(tonic);
        var manager = new QuestManager(guild);
        var formation = new AdventurerData?[6];
        formation[0] = adventurer;

        Assert.True(manager.TryStartQuest(
            new QuestMasterData { id = "q", totalPhases = 10 },
            formation, 1, out var error, new[] { tonic }), error);

        Assert.Equal(0, guild.GetConsumableCount(tonic));
        Assert.Contains("tonic", manager.activeQuests.Single().usedConsumableIds);
        Assert.True(adventurer.CombatHpMax > adventurer.GetFinalCombatStats().hp);
        Assert.Equal(adventurer.level, manager.activeQuests.Single().startingLevels[adventurer.id]);
        Assert.Equal(guild.EffectiveUpkeepPerTurn, manager.activeQuests.Single().guildUpkeepAtStart);
    }

    [Fact]
    public void TurnEndChoiceBlocksUntilResolved()
    {
        var guild = new GuildManager();
        var adventurer = new AdventurerData(BasicAdventurer());
        guild.AddAdventurer(adventurer);
        var choice = new QuestChoiceEventMasterData
        {
            id = "choice",
            title = "分かれ道",
            weight = 1,
            options =
            {
                new QuestChoiceOptionData
                {
                    text = "調べる", resultText = "資金を発見",
                    effectType = QuestChoiceEffectType.Gold, value = 25,
                },
                new QuestChoiceOptionData { text = "進む", resultText = "何もなし" },
            },
        };
        var dungeon = new DungeonMasterData { turnEndEventChance = 1f };
        dungeon.turnEndEvents.Add(choice);
        var quest = new QuestMasterData
        {
            id = "q", totalPhases = 10, phasesPerTurn = 1, Dungeon = dungeon,
        };
        var manager = new QuestManager(guild);
        var formation = new AdventurerData?[6];
        formation[0] = adventurer;
        Assert.True(manager.TryStartQuest(quest, formation, 1, out _));

        manager.AdvanceAll(2);

        var run = manager.activeQuests.Single();
        Assert.True(manager.HasPendingChoices);
        Assert.Equal(1, run.currentPhase);
        manager.AdvanceAll(3);
        Assert.Equal(1, run.currentPhase);

        Assert.True(manager.ResolveChoice(run, 0, out var result));
        Assert.StartsWith("資金を発見", result);
        Assert.Contains("ゴールド+25", result);
        Assert.False(manager.HasPendingChoices);
        Assert.Contains(run.pendingLoot, x => x.type == RewardType.Gold && x.gold == 25);
    }

    [Fact]
    public void LearnedClassSkillRemainsActiveAfterChangingClass()
    {
        var skill = new SkillMasterData { id = "skill", skillName = "達人技" };
        var firstClass = new ClassMasterData { id = "first", className = "第一職" };
        firstClass.classSkills.Add(new ClassSkillEntry
        {
            skillId = skill.id, Skill = skill, requiredClearCount = 0,
        });
        var secondClass = new ClassMasterData { id = "second", className = "第二職" };
        var master = BasicAdventurer();
        master.DefaultClass = firstClass;
        master.defaultClassId = firstClass.id;
        var adventurer = new AdventurerData(master);

        adventurer.ChangeClass(secondClass);

        Assert.Contains(skill, adventurer.Skills);
    }

    [Fact]
    public void MasterDataResolvesRareDropsAndRarities()
    {
        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        var db = MasterLoader.Load(dataDir);

        Assert.Contains(db.equipment.Values, e => e.rarity == Rarity.Legend);
        Assert.Contains(db.allAdventurers, a => a.rarity != Rarity.Common);
        Assert.Contains(db.enemies.Values.SelectMany(e => e.dropTable),
            d => d.Equipment != null || d.Consumable != null);
        Assert.Empty(MasterValidator.Validate(db));
    }

    [Fact]
    public void MasterDataLoadsWeaponDamageDiceAndAmplifyCaps()
    {
        // damageDice はダメージの基礎値そのもの。ここが読み込まれないと全武器が素手扱いになる。
        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        var db = MasterLoader.Load(dataDir);

        var weapons = db.equipment.Values.Where(e => e.type == EquipmentType.Weapon).ToList();
        Assert.NotEmpty(weapons);
        Assert.All(weapons, w => Assert.False(string.IsNullOrWhiteSpace(w.damageDice), $"{w.id} に damageDice が無い"));

        // ダイス表記が壊れていれば 1d4 にフォールバックしてしまうため、記法も検証する。
        Assert.All(weapons, w => Assert.Equal(w.damageDice, Dice.Parse(w.damageDice).ToString()));

        // 武器はどれもPVを持つ。PVが0だと能力値modifierだけが頼りになり、武器の等級が意味を失う。
        Assert.All(weapons, w => Assert.True(w.basePv > 0, $"{w.id} の basePv が0"));

        // 上限は武器クラスごとの固定値で、無制限の得物は存在しない。
        // 無制限を許すと主能力が伸びるほどクラス間の差が青天井に開き、
        // 「斧なら常に貫通、短剣は常に弾かれる」という壊れ方をする。
        var attackWeapons = weapons.Where(w => w.attackKind != AttackKind.Heal).ToList();
        Assert.NotEmpty(attackWeapons);
        Assert.All(attackWeapons, w => Assert.True(
            w.maxStatBonus < QudCombatDefaults.UnlimitedStatBonus,
            $"{w.id} の能力値上限が無制限になっている"));

        var caps = attackWeapons.Select(w => w.maxStatBonus).Distinct().OrderBy(x => x).ToList();
        Assert.True(caps.Count > 1, "武器クラスによる上限の差が無い");

        // 上限差は4点以内に収める。開きすぎると「斧なら常に貫通、短剣は常に弾かれる」という壊れ方をする。
        // 近接の片手武器だけなら5〜8の3点刻みで、両手武器（大剣・長槍・大斧）が
        // 左手を丸ごと失う代償として1段だけ上に乗るため、全体では4点になる。
        Assert.True(caps.Max() - caps.Min() <= 4,
            $"クラス間の上限差が開きすぎている（{caps.Min()}〜{caps.Max()}）");

        // 両手武器は必ず同じ武器種の片手版より上限が高い。そうでないと持ち替える理由がない。
        foreach (var twoHanded in attackWeapons.Where(w => w.isTwoHanded))
        {
            var oneHanded = attackWeapons.FirstOrDefault(
                w => !w.isTwoHanded && w.weaponType == twoHanded.weaponType);
            if (oneHanded != null)
                Assert.True(twoHanded.maxStatBonus > oneHanded.maxStatBonus,
                    $"{twoHanded.id} の上限が片手版{oneHanded.id}を上回っていない");
        }

        // 武器を持たない敵は自然攻撃ダイスで殴り、牙・爪そのもののPVを持つ。
        var unarmed = db.enemies.Values.Where(e => e.DefaultWeapon == null).ToList();
        Assert.NotEmpty(unarmed);
        Assert.All(unarmed, e => Assert.False(string.IsNullOrWhiteSpace(e.naturalDamageDice), $"{e.id} に naturalDamageDice が無い"));
        Assert.All(unarmed, e => Assert.True(e.naturalPv > 0, $"{e.id} の naturalPv が0"));
    }

    static AdventurerMasterData BasicAdventurer() => new()
    {
        id = "adv",
        baseName = "テスト",
        defaultLevel = 1,
        defaultRank = 1,
        vitality = 10,
        mental = 10,
        strength = 10,
        agility = 10,
        intelligence = 10,
        constitution = 10,
    };
}
