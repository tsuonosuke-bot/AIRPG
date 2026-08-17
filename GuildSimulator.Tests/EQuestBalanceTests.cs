using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

/// <summary>
/// E帯とD帯のクエスト設計を固定する。F帯を抜けた直後の遊びが「歯応え」ではなく
/// 「金にならない作業」に落ちないよう、報酬の帯とダンジョン割当てをここで縛る。
/// </summary>
public class EQuestBalanceTests
{
    static GameMasterData Load() => MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    [Fact]
    public void EandDQuestRewardsStayInsideTheBand()
    {
        var db = Load();
        foreach (int rank in new[] { 2, 3 })
        {
            var band = RankBandTable.ForQuestRank(rank)!;
            foreach (var quest in db.allQuests)
            {
                if (quest.rank != rank) continue;
                // 昇格試験は帯の外を許す（1本で次段へ進むぶんの見返りを積んである）。
                if (quest.isEmergencyQuest) continue;

                Assert.True(band.RewardGold.Contains(quest.rewardGold),
                    $"{quest.id}: gold {quest.rewardGold} が帯 {band.RewardGold} の外");
                Assert.True(band.RewardExp.Contains(quest.rewardExp),
                    $"{quest.id}: exp {quest.rewardExp} が帯 {band.RewardExp} の外");
                Assert.True(band.GuildPoints.Contains(quest.rewardGuildPoints),
                    $"{quest.id}: GP {quest.rewardGuildPoints} が帯 {band.GuildPoints} の外");
                Assert.True(band.TotalPhases.Contains(quest.totalPhases),
                    $"{quest.id}: totalPhases {quest.totalPhases} が帯 {band.TotalPhases} の外");
            }
        }
    }

    /// <summary>
    /// 廃坑と旧市街の通常クエストはD帯。E帯クエストとして残っているのは
    /// 昇格試験（quest_promotion_2）だけ。
    /// </summary>
    [Fact]
    public void MineAndOldCityLowRankQuestsAreDRankExceptPromotion()
    {
        var db = Load();
        foreach (var quest in db.allQuests)
        {
            if (quest.Dungeon?.id is not ("dungeon_mine" or "dungeon_old_city")) continue;
            if (quest.rank > 3) continue;
            int expected = quest.id == "quest_promotion_2" ? 2 : 3;
            Assert.Equal(expected, quest.rank);
        }
    }

    /// <summary>E帯とD帯の主力ボスクエストが、想定のユニットを呼び出しているか。</summary>
    [Fact]
    public void SignatureBossQuestsPointAtTheirUnits()
    {
        var db = Load();
        (string questId, string expectedUnitId)[] pairs =
        {
            ("quest_poison_spider_cull", "unit_poison_spider_lair"),
            ("quest_ranpos_cull",         "unit_ranpos_pack"),
            ("quest_bandit_raiders",      "unit_bandit_raiders"),
            ("quest_poison_fang",         "unit_poison_fang_pack"),
            ("quest_ranpos_alpha",        "unit_ranpos_alpha_pack"),
            ("quest_wyvern_scout",        "unit_wyvern_lesser"),
            ("quest_mine_brood",          "unit_rock_eater_swarm"),
        };
        foreach (var (questId, unitId) in pairs)
        {
            var quest = db.allQuests.Single(q => q.id == questId);
            Assert.Equal(unitId, quest.BossEnemy?.id);
            Assert.Equal(quest.totalPhases, quest.bossPhase);
        }
    }

    [Fact]
    public void BanditEncounterEventsAreOnTheHighway()
    {
        var db = Load();
        var highway = db.dungeons["dungeon_highway"];
        Assert.Contains("event_bandit_toll", highway.turnEndEvents.Select(e => e.id));
        Assert.Contains("event_bandit_deserter_dealer", highway.turnEndEvents.Select(e => e.id));
        Assert.Contains("event_bandit_lookout_spotted", highway.turnEndEvents.Select(e => e.id));

        // 通行料の交渉は結果が運次第（IsGamble = outcomes.Count > 1）で、
        // 戦闘を模した DamagePercent の分岐がある。
        var toll = db.choiceEvents["event_bandit_toll"];
        var negotiate = toll.options.Single(o => o.text.Contains("顔役"));
        Assert.True(negotiate.IsGamble);
        Assert.Contains(negotiate.outcomes,
            o => o.effectType == Core.Models.QuestChoiceEffectType.DamagePercent);
    }
}
