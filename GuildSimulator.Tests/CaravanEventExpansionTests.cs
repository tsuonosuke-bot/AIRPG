using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

[Collection("Guild static state")]
public class CaravanEventExpansionTests
{
    static readonly string[] CaravanEventIds =
    {
        "event_caravan_supply_wagon",
        "event_caravan_specialist_merchant",
        "event_caravan_rare_broker",
        "event_caravan_stuck_wagon",
        "event_caravan_pack_beast_panic",
        "event_caravan_ambush",
    };

    static GameMasterData Load() =>
        MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void CaravanEventsAddCommerceAidAndDangerWithoutRaisingEventFrequency()
    {
        var db = Load();

        Assert.All(CaravanEventIds, id =>
        {
            Assert.True(db.choiceEvents.TryGetValue(id, out var choiceEvent), id);
            Assert.InRange(choiceEvent.weight, 1, 3);
            Assert.True(choiceEvent.options.Count >= 3, id);
        });

        var highway = db.dungeons["dungeon_highway"];
        Assert.All(CaravanEventIds, id =>
            Assert.Contains(highway.turnEndEvents, choiceEvent => choiceEvent.id == id));
        Assert.Equal(0.35f, highway.turnEndEventChance);

        Assert.Contains(db.dungeons["dungeon_meadow"].turnEndEvents,
            choiceEvent => choiceEvent.id == "event_caravan_supply_wagon");
        Assert.Contains(db.dungeons["dungeon_mine"].turnEndEvents,
            choiceEvent => choiceEvent.id == "event_caravan_specialist_merchant");
        Assert.Contains(db.dungeons["dungeon_old_city"].turnEndEvents,
            choiceEvent => choiceEvent.id == "event_caravan_rare_broker");
        Assert.DoesNotContain(db.dungeons["dungeon_crypt"].turnEndEvents,
            choiceEvent => choiceEvent.id.StartsWith("event_caravan_", StringComparison.Ordinal));

        Assert.Empty(MasterValidator.Validate(db));
    }

    [Fact]
    public void PurchaseOptionsResolveBothConsumablesAndEquipmentAndHaveAFreeAlternative()
    {
        var db = Load();

        var supply = db.choiceEvents["event_caravan_supply_wagon"];
        var trapKit = supply.options.Single(option => option.targetId == "item_trap_kit");
        Assert.Equal(QuestChoiceEffectType.Purchase, trapKit.effectType);
        Assert.NotNull(trapKit.Consumable);
        Assert.Null(trapKit.Equipment);

        var dealer = db.choiceEvents["event_bandit_deserter_dealer"];
        var chainMail = dealer.options.Single(option => option.targetId == "eq_chain_01");
        Assert.Equal(QuestChoiceEffectType.Purchase, chainMail.effectType);
        Assert.NotNull(chainMail.Equipment);
        Assert.Null(chainMail.Consumable);

        foreach (var choiceEvent in db.choiceEvents.Values.Where(choiceEvent =>
                     choiceEvent.options.Any(option => option.effectType == QuestChoiceEffectType.Purchase)))
        {
            Assert.Contains(choiceEvent.options,
                option => option.Outcomes.All(outcome => outcome.effectType != QuestChoiceEffectType.Purchase));
            Assert.All(choiceEvent.options.Where(option => option.effectType == QuestChoiceEffectType.Purchase),
                option => Assert.Empty(option.outcomes));
        }
    }

    [Fact]
    public void PurchaseSpendsGoldImmediatelyAndQueuesTheItemForReturn()
    {
        var db = Load();
        var choiceEvent = db.choiceEvents["event_caravan_supply_wagon"];
        var guild = new GuildManager(startGold: 100);
        var manager = new QuestManager(guild);
        var run = Pending(choiceEvent);

        Assert.True(manager.ResolveChoice(run, 0, out var result), result);

        Assert.Equal(70, guild.Gold);
        Assert.Null(run.pendingChoice);
        var loot = Assert.Single(run.pendingLoot);
        Assert.Equal(RewardType.Consumable, loot.type);
        Assert.Equal("item_trap_kit", loot.consumableId);
        Assert.Contains("30G", result);
    }

    [Fact]
    public void HaggleSkillAlsoReducesCaravanPurchasePrices()
    {
        var db = Load();
        var choiceEvent = db.choiceEvents["event_caravan_supply_wagon"];
        var guild = new GuildManager(startGold: 100);
        var manager = new QuestManager(guild);
        var run = Pending(choiceEvent);
        var negotiator = Adventurer("negotiator");
        negotiator.LearnPermanentSkill(new SkillMasterData
        {
            id = "test_haggle",
            skillName = "交渉術",
            expedition = new SkillExpeditionEffect { goldPercent = 20 },
        });
        run.formation[0] = negotiator;

        Assert.Equal(20, QuestManager.PurchaseNegotiationPercent(run));
        Assert.Equal(24, QuestManager.CalculatePurchasePrice(run, 30));
        Assert.True(manager.ResolveChoice(run, 0, out var result), result);

        Assert.Equal(76, guild.Gold);
        Assert.Contains("交渉で 20%引き", result);
    }

    [Theory]
    [InlineData(0, 90)]
    [InlineData(6, 85)]
    [InlineData(12, 80)]
    [InlineData(20, 72)]
    [InlineData(40, 68)]
    [InlineData(-15, 104)]
    [InlineData(-40, 113)]
    public void PurchasePriceUsesHaggleAndSpendthriftWithAQuarterCap(
        int goldPercent,
        int expected)
    {
        var run = Pending(new QuestChoiceEventMasterData { id = "price_test" });
        if (goldPercent != 0)
        {
            var member = Adventurer("price_tester");
            member.LearnPermanentSkill(new SkillMasterData
            {
                id = $"price_modifier_{goldPercent}",
                skillName = "価格補正",
                expedition = new SkillExpeditionEffect { goldPercent = goldPercent },
            });
            run.formation[0] = member;
        }

        Assert.Equal(Math.Clamp(goldPercent, -25, 25),
            QuestManager.PurchaseNegotiationPercent(run));
        Assert.Equal(expected, QuestManager.CalculatePurchasePrice(run, 90));
    }

    [Fact]
    public void InsufficientFundsKeepTheChoiceOpenSoAnotherOptionCanBeSelected()
    {
        var db = Load();
        var choiceEvent = db.choiceEvents["event_caravan_supply_wagon"];
        var guild = new GuildManager(startGold: 0);
        var manager = new QuestManager(guild);
        var run = Pending(choiceEvent);
        run.formation[0] = Adventurer("learner");

        Assert.False(manager.ResolveChoice(run, 0, out var error));
        Assert.Contains("資金が不足", error);
        Assert.NotNull(run.pendingChoice);
        Assert.Empty(run.pendingLoot);
        Assert.Equal(0, guild.Gold);

        Assert.True(manager.ResolveChoice(run, 3, out var result), result);
        Assert.Null(run.pendingChoice);
    }

    [Fact]
    public void PurchaseKeepsOneGoldToAvoidImmediateBankruptcy()
    {
        var db = Load();
        var choiceEvent = db.choiceEvents["event_caravan_supply_wagon"];
        var guild = new GuildManager(startGold: 30);
        var manager = new QuestManager(guild);
        var run = Pending(choiceEvent);

        Assert.False(manager.ResolveChoice(run, 0, out var bankruptcyWarning));
        Assert.Contains("購入後に0G", bankruptcyWarning);
        Assert.Equal(30, guild.Gold);
        Assert.NotNull(run.pendingChoice);

        guild.AddGold(1, "境界テスト");
        Assert.True(manager.ResolveChoice(run, 0, out var result), result);
        Assert.Equal(1, guild.Gold);
        Assert.Null(run.pendingChoice);
    }

    static QuestRun Pending(QuestChoiceEventMasterData choiceEvent) => new(
        new QuestMasterData { id = "test_caravan_quest", rank = Rank.Min },
        startedTurn: 1)
    {
        pendingChoice = new PendingQuestChoice { Event = choiceEvent, createdTurn = 2 },
    };

    static AdventurerData Adventurer(string id) => new(new AdventurerMasterData
    {
        id = id,
        baseName = id,
        vitality = 10,
        mental = 10,
        strength = 10,
        agility = 10,
        intelligence = 10,
        constitution = 10,
    });

    [Fact]
    public void PurchaseEffectWasAppendedWithoutRenumberingSavedChoiceEffects()
    {
        Assert.Equal(12, (int)QuestChoiceEffectType.AdventurerDamage);
        Assert.Equal(13, (int)QuestChoiceEffectType.Purchase);
    }
}
