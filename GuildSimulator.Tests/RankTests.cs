using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using Xunit;

namespace GuildSimulator.Tests;

/// <summary>
/// F〜Sの7段階ランクと、クラス習熟度が入る「適正ランク」の検証。
/// 冒険者・クエスト・ギルドの3つのランクは同じ物差しに乗っている。
/// </summary>
[Collection("Guild static state")]
public class RankTests
{
    [Theory]
    [InlineData(1, "F")]
    [InlineData(2, "E")]
    [InlineData(3, "D")]
    [InlineData(4, "C")]
    [InlineData(5, "B")]
    [InlineData(6, "A")]
    [InlineData(7, "S")]
    public void EachStoredNumberMapsToItsLetter(int stored, string label)
    {
        Assert.Equal(label, Rank.Label(stored));
    }

    [Fact]
    public void OutOfRangeValuesAreClampedToTheEnds()
    {
        // セーブデータやマスタに範囲外が入っていても、表示が壊れるより端に丸めたほうが安全。
        Assert.Equal("F", Rank.Label(0));
        Assert.Equal("F", Rank.Label(-5));
        Assert.Equal("S", Rank.Label(99));
        Assert.Equal(Rank.Min, Rank.Clamp(0));
        Assert.Equal(Rank.Max, Rank.Clamp(99));
    }

    [Theory]
    // 冒険者D(3) の適正帯は D〜B。格下Eは学ぶものがなく、格上すぎるAは連れ回されているだけ。
    [InlineData(3, 2, false)]
    [InlineData(3, 3, true)]
    [InlineData(3, 4, true)]
    [InlineData(3, 5, true)]
    [InlineData(3, 6, false)]
    public void SuitableRankIsTheBandFromYourOwnRankUpTwo(int adventurerRank, int questRank, bool suitable)
    {
        Assert.Equal(suitable, Rank.IsSuitable(questRank, adventurerRank));
    }

    [Fact]
    public void TheSuitableBandReadsTheSameFromBothSides()
    {
        // 冒険者から見た「受けるべきクエスト」と、クエストから見た「伸びる冒険者」は表裏。
        Assert.Equal("D〜B", Rank.SuitableRangeLabel(3));
        Assert.Equal("F〜D", Rank.SuitableAdventurerRangeLabel(3));

        // 端では潰れて1つになる。
        Assert.Equal("S", Rank.SuitableRangeLabel(Rank.Max));
        Assert.Equal("F", Rank.SuitableAdventurerRangeLabel(Rank.Min));
    }

    [Fact]
    public void ClassMasteryOnlyCountsClearsInsideTheSuitableBand()
    {
        var cls = new ClassMasterData { id = "cls", className = "テスト職" };
        var adventurer = new AdventurerData(new AdventurerMasterData
        {
            id = "adv", baseName = "測定用", defaultRank = 3, // D
        })
        {
            currentClass = cls,
        };

        adventurer.OnClearQuest(2); // E: 格下
        Assert.Equal(0, adventurer.CurrentClassMastery);

        adventurer.OnClearQuest(6); // A: 格上すぎる
        Assert.Equal(0, adventurer.CurrentClassMastery);

        adventurer.OnClearQuest(3); // D: 同ランク
        adventurer.OnClearQuest(5); // B: 適正帯の上端
        Assert.Equal(200, adventurer.CurrentClassMastery);

        // 死者は数えない。
        adventurer.isAlive = false;
        adventurer.OnClearQuest(3);
        Assert.Equal(200, adventurer.CurrentClassMastery);
    }

    [Fact]
    public void AdventurerRankStopsAtS()
    {
        // Sから始めて、何本クリアしてもSより上には行かず、カウンタも動かない。
        var adventurer = new AdventurerData(new AdventurerMasterData
        {
            id = "adv", baseName = "測定用", defaultRank = Rank.Max,
        });

        for (int i = 0; i < 100; i++) adventurer.RecordQuestClearForRank(Rank.Max);

        Assert.Equal(Rank.Max, adventurer.rank);
        Assert.True(adventurer.IsMaxRank);
        Assert.Equal("S", adventurer.RankLabel);
        // 上限に達したら回数も数えない。溜まり続けると昇格できるように見えてしまう。
        Assert.Equal(0, adventurer.higherRankClears);
        Assert.Equal(0, adventurer.suitableRankClearsTotal);
    }

    [Fact]
    public void AdventurerBecomesRankUpEligibleButDoesNotPromoteOnItsOwn()
    {
        // F→Eは 格上1 かつ 累積適正3。両方を満たすと CanRankUp が立つが、
        // ランクそのものは自動では上がらない（プレイヤー選択で昇格させる）。
        var adventurer = new AdventurerData(new AdventurerMasterData
        {
            id = "adv", baseName = "測定用", defaultRank = Rank.Min,
        });

        // 同ランク以下（格下含む）は格上に載らない。
        for (int i = 0; i < 5; i++) adventurer.RecordQuestClearForRank(Rank.Min - 1);
        Assert.Equal(0, adventurer.higherRankClears);
        Assert.Equal(0, adventurer.suitableRankClearsTotal);

        // 同ランクは適正帯なので累積には載る。ただし格上ではない。
        adventurer.RecordQuestClearForRank(Rank.Min);
        Assert.Equal(0, adventurer.higherRankClears);
        Assert.Equal(1, adventurer.suitableRankClearsTotal);
        Assert.False(adventurer.CanRankUp);

        // 格上を1本クリアしても、累積がまだ3に届かない。
        adventurer.RecordQuestClearForRank(Rank.Min + 1);
        Assert.Equal(1, adventurer.higherRankClears);
        Assert.Equal(2, adventurer.suitableRankClearsTotal);
        Assert.False(adventurer.CanRankUp);

        // もう1本適正帯を積むと累積3に達し、両方の条件を満たす。
        adventurer.RecordQuestClearForRank(Rank.Min);
        Assert.Equal(Rank.Min, adventurer.rank); // 自動昇格しない
        Assert.True(adventurer.CanRankUp);
    }

    [Fact]
    public void CumulativeSuitableClearsGateEvenWhenHigherRankIsSatisfied()
    {
        // E→Dは 格上2 かつ 累積10。格上だけ先に満たしても累積が足りなければ CanRankUp は立たない。
        var adventurer = new AdventurerData(new AdventurerMasterData
        {
            id = "adv", baseName = "測定用", defaultRank = Rank.Min + 1, // E
        });

        // 格上を2本先に取る（適正帯でもあるので累積にも2載る）。
        adventurer.RecordQuestClearForRank(Rank.Min + 2);
        adventurer.RecordQuestClearForRank(Rank.Min + 2);
        Assert.Equal(2, adventurer.higherRankClears);
        Assert.Equal(2, adventurer.suitableRankClearsTotal);
        Assert.False(adventurer.CanRankUp);

        // 累積10に届くまで同ランクを積む。
        for (int i = 0; i < 8; i++) adventurer.RecordQuestClearForRank(Rank.Min + 1);
        Assert.Equal(Rank.Min + 1, adventurer.rank);
        Assert.True(adventurer.CanRankUp);
    }

    [Fact]
    public void ManualRankUpGivesStatsMasteryAndUnlocksSkills()
    {
        // 昇格報酬: 全能力+2 と 現在職業に習熟度+1000。到達したスキルは同時に解禁される。
        var lateSkill = new SkillMasterData { id = "skill_promo_late", skillName = "昇格記念スキル" };
        var cls = new ClassMasterData { id = "cls_promo", className = "昇格試験職" };
        cls.classSkills.Add(new ClassSkillEntry
        {
            skillId = lateSkill.id, Skill = lateSkill,
            requiredClearCount = AdventurerData.RankUpMasteryGain, // ちょうど昇格報酬ぶんで開くしきい値
        });
        var adventurer = new AdventurerData(new AdventurerMasterData
        {
            id = "adv", baseName = "測定用",
            defaultRank = Rank.Min,
            vitality = 5, mental = 5, strength = 5,
            agility = 5, intelligence = 5, constitution = 5, appearance = 5,
            DefaultClass = cls, defaultClassId = cls.id,
        });

        // F→E の条件をひととおり満たす。
        adventurer.RecordQuestClearForRank(Rank.Min + 1);
        adventurer.RecordQuestClearForRank(Rank.Min);
        adventurer.RecordQuestClearForRank(Rank.Min);
        Assert.True(adventurer.CanRankUp);
        Assert.Equal(0, adventurer.CurrentClassMastery);

        Assert.True(adventurer.TryRankUp(out var result));
        Assert.Equal(Rank.Min, result.PreviousRank);
        Assert.Equal(Rank.Min + 1, result.NewRank);
        Assert.Equal(Rank.Min + 1, adventurer.rank);
        // 全能力+2（SIZ,APPも含む）
        int expectedStat = 5 + AdventurerData.RankUpStatGain;
        Assert.Equal(expectedStat, adventurer.vitality);
        Assert.Equal(expectedStat, adventurer.mental);
        Assert.Equal(expectedStat, adventurer.strength);
        Assert.Equal(expectedStat, adventurer.agility);
        Assert.Equal(expectedStat, adventurer.intelligence);
        Assert.Equal(expectedStat, adventurer.constitution);
        Assert.Equal(expectedStat, adventurer.appearance);
        // 習熟度+500 と、それによるスキル解禁。
        Assert.Equal(AdventurerData.RankUpMasteryGain, result.MasteryGained);
        Assert.Equal(AdventurerData.RankUpMasteryGain, adventurer.CurrentClassMastery);
        Assert.Contains(lateSkill, result.UnlockedSkills);
        Assert.Contains(lateSkill, adventurer.Skills);
        // 格上カウンタは0に戻り、累積は残る。
        Assert.Equal(0, adventurer.higherRankClears);
        Assert.Equal(3, adventurer.suitableRankClearsTotal);
        // 履歴に昇格ログが残る（スキル名を含む）。
        Assert.Contains(adventurer.adventureHistory, line =>
            line.Contains("F→E に昇格") && line.Contains("昇格記念スキル"));
        // 昇格後に再度呼んでも失敗する（条件が消えている）。
        Assert.False(adventurer.CanRankUp);
        Assert.False(adventurer.TryRankUp(out _));
    }

    [Fact]
    public void DeadAdventurersDoNotRankUp()
    {
        var adventurer = new AdventurerData(new AdventurerMasterData
        {
            id = "adv", baseName = "測定用", defaultRank = Rank.Min,
        });
        adventurer.isAlive = false;

        for (int i = 0; i < 10; i++) adventurer.RecordQuestClearForRank(Rank.Max);

        Assert.Equal(Rank.Min, adventurer.rank);
        Assert.Equal(0, adventurer.higherRankClears);
    }

    [Fact]
    public void GuildRankStopsAtSToo()
    {
        var guild = new GuildManager(startGold: 100, startRank: Rank.Min);
        for (int i = 0; i < 20; i++) guild.RankUp(1, "昇格試験");

        Assert.Equal(Rank.Max, guild.GuildRank);
        Assert.Equal("S", guild.GuildRankLabel);
        Assert.True(guild.IsMaxGuildRank);
    }

    [Fact]
    public void PromotionUsesPointsEarnedInTheCurrentRankAndResetsThemOnRankUp()
    {
        var guild = new GuildManager(startGold: 100, startRank: Rank.Min);
        guild.RestoreEconomy(100, Rank.Min, guildPoints: 5_000, guildPointsThisRank: 0);
        var manager = new QuestManager(guild);
        var promotion = new QuestMasterData
        {
            id = "promotion",
            rank = Rank.Min,
            isEmergencyQuest = true,
            rankUpOnClear = 1,
            requiredGuildPoints = 50,
        };

        manager.FillBoard(new[] { promotion }, currentTurn: 1);
        Assert.Empty(manager.questBoard);

        guild.AddGuildPoints(50, "現ランクでの依頼実績");
        manager.FillBoard(new[] { promotion }, currentTurn: 2);
        Assert.Contains(manager.questBoard, entry => entry.quest == promotion);

        guild.RankUp(1, "昇格試験");
        guild.AddGuildPoints(80, "昇格試験報酬", countTowardRankProgress: false);
        Assert.Equal(5_130, guild.GuildPoints);
        Assert.Equal(0, guild.GuildPointsThisRank);
    }

    [Fact]
    public void LoadingAnOldBoardRemovesPromotionPostedFromLifetimePointsAlone()
    {
        var guild = new GuildManager(startGold: 100, startRank: 2);
        guild.RestoreEconomy(100, 2, guildPoints: 5_000, guildPointsThisRank: 0);
        var promotion = new QuestMasterData
        {
            id = "stale-promotion",
            rank = 2,
            isEmergencyQuest = true,
            rankUpOnClear = 1,
            requiredGuildPoints = 200,
        };
        var manager = new QuestManager(guild);

        manager.RestoreState(
            new() { new QuestBoardEntry(promotion, postedTurn: 1) },
            new(),
            Array.Empty<string>());

        Assert.Empty(manager.questBoard);
    }

    [Fact]
    public void EnemyBattlePressureAddsTwentyFivePercentHpAccuracyAndPenetration()
    {
        var master = new EnemyMasterData
        {
            id = "pressure-test", baseName = "圧力試験", vitality = 4, constitution = 0, agility = 10,
        };
        var enemy = new EnemyData(master);
        IUnitMember?[] formation = { enemy, null, null, null, null, null };

        var raw = enemy.GetFinalCombatStats();
        var pressured = Assert.Single(UnitCalculator.CalcPerMember(formation, isAllySide: false)).stats;

        Assert.Equal((int)Math.Ceiling(raw.hp * UnitCalculator.EnemyHpMultiplier), pressured.hp);
        Assert.Equal(raw.toHit + UnitCalculator.EnemyToHitBonus, pressured.toHit);
        Assert.Equal(raw.pv + UnitCalculator.EnemyPvBonus, pressured.pv);
        Assert.Equal(raw.mpv + UnitCalculator.EnemyPvBonus, pressured.mpv);
    }

    [Fact]
    public void MasterDataRanksAllFitInsideTheSevenSteps()
    {
        // マスタが範囲外の数値を持っていると、表示は丸められて実挙動とずれる。
        var db = Game.Data.MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

        foreach (var quest in db.allQuests)
            Assert.InRange(quest.rank, Rank.Min, Rank.Max);
        foreach (var adventurer in db.allAdventurers)
        {
            Assert.InRange(adventurer.defaultRank, Rank.Min, Rank.Max);
            Assert.InRange(adventurer.defaultLevel, 1, Rank.LevelCap(adventurer.defaultRank));
            Assert.InRange(adventurer.recruitGuildRank, Rank.Min, Rank.Max);
        }
        foreach (var facility in db.facilities.Values)
            Assert.InRange(facility.requiredGuildRank, Rank.Min, Rank.Max);

        // 敵の脅威度も同じ物差しに乗る。
        foreach (var enemy in db.enemies.Values)
            Assert.InRange(enemy.threat, Rank.Min, Rank.Max);
    }

    [Fact]
    public void EnemyStrengthComesFromTheMasterDataNotFromAnyLevelMultiplier()
    {
        // レベル倍率をやめたので、マスタに書いた能力値がそのまま戦闘値になる。
        // 強弱は別々の個体で表す（はぐれゴブリン→ゴブリン兵士→ゴブリン重装歩兵）。
        var db = Game.Data.MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var master = db.enemies.Values.First(e => e.DefaultWeapon == null);
        var enemy = new EnemyData(master);

        var stats = enemy.GetBaseCombatStats();
        Assert.Equal((master.vitality * 10 + master.constitution * 5) / 2, stats.hp);
        Assert.Equal(master.mental * 10, stats.san);
        Assert.Equal(Rank.Clamp(master.threat), enemy.Threat);
    }

    [Fact]
    public void TheSameFamilyOfEnemiesClimbsTheThreatLadder()
    {
        // 「同系統で強さの違う個体を並べる」がレベル倍率の代わりになっていること。
        var db = Game.Data.MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var goblins = db.enemies.Values
            .Where(e => e.baseName.Contains("ゴブリン"))
            .ToList();
        Assert.True(goblins.Count >= 3, "同系統の敵が揃っていない");
        Assert.True(goblins.Select(e => e.threat).Distinct().Count() > 1,
            "同系統の敵に脅威度の段階差がない");
    }

    [Fact]
    public void MoraleShockIsMeasuredInRankStepsNotLevels()
    {
        // 尺度がレベル差（10以上開きうる）からランク差（最大6）に変わったので、
        // 1段あたりの重みを上げてある。上限に達するのは3段差から。
        var morale = new MoraleState(1000);
        Assert.Equal(0, morale.DrainThreatGap(0));
        Assert.Equal(0, morale.DrainThreatGap(-2));
        Assert.Equal(MoraleState.ThreatGapFlat, morale.DrainThreatGap(1));

        var capped = new MoraleState(1000);
        Assert.Equal(MoraleState.ThreatGapFlatCap, capped.DrainThreatGap(Rank.Max - Rank.Min));
    }
}
