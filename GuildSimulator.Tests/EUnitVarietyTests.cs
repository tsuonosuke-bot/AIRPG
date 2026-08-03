using GuildSimulator.Core.MasterData;
using GuildSimulator.Game.Data;
using Xunit;

namespace GuildSimulator.Tests;

/// <summary>
/// E帯（と接する D帯）ユニットの<b>形の多様性</b>を固定するテスト。
/// 「前衛のみ3体」ばかりだと、E帯の駆け引きが痩せてしまうため、
/// 単独出現・少数精鋭・後衛のみといった別形状をここで縛る。
/// </summary>
public class EUnitVarietyTests
{
    static GameMasterData Load() => MasterLoader.Load(Path.Combine(AppContext.BaseDirectory, "Data"));

    static int MaxThreat(EnemyUnitTemplate unit) =>
        unit.Formation.Where(e => e != null).Select(e => e!.threat).DefaultIfEmpty(0).Max();

    // Formation は MasterLoader が埋める実体リスト（formationIds は生JSONの文字列のままで
    // 読み込み後に使われない。参照するとテストが常に空扱いになる）。
    static (int front, int back) Shape(EnemyUnitTemplate unit)
    {
        int front = unit.Formation.Take(3).Count(e => e != null);
        int back = unit.Formation.Skip(3).Count(e => e != null);
        return (front, back);
    }

    [Fact]
    public void GoblinArmyHasATrueMixedLineUnit()
    {
        var db = Load();
        var line = db.enemyUnits["unit_goblin_line"];
        var ids = line.Formation.Where(e => e != null).Select(e => e!.id).ToList();
        // ゴブリン正規軍：兵士2 + 射手 + 魔導士。ゴブリンの3職を1隊に揃えた顔役。
        Assert.Equal(2, ids.Count(id => id == "enemy_goblin_soldier"));
        Assert.Contains("enemy_goblin_archer", ids);
        Assert.Contains("enemy_goblin_mage", ids);
        var (front, back) = Shape(line);
        Assert.Equal((2, 2), (front, back));
    }

    /// <summary>E帯の単独出現がある（"ランエンカで格上に見える"演出）。</summary>
    [Fact]
    public void EBandHasLoneEncounterUnits()
    {
        var db = Load();
        var lone = db.enemyUnits.Values
            .Where(u => MaxThreat(u) == 2 && Shape(u) == (1, 0))
            .ToList();
        Assert.True(lone.Count >= 2, "E帯の単独出現ユニットが2本未満です");

        Assert.Contains(lone, u => u.id == "unit_mage_wolf_alone");
        Assert.Contains(lone, u => u.id == "unit_wight_alone");
    }

    /// <summary>後衛のみユニットがある。前列DV補正が付かない代わりに、こちらの前列が空振る形。</summary>
    [Fact]
    public void ThereIsABackRowOnlyUnit()
    {
        var db = Load();
        var backOnly = db.enemyUnits.Values
            .Where(u => Shape(u) is (0, > 0))
            .ToList();
        Assert.NotEmpty(backOnly);
        Assert.Contains(backOnly, u => u.id == "unit_bandit_back_line");
    }

    /// <summary>E帯の"前衛のみ"が全体の過半にならないこと。処理順の駆け引きが痩せる。</summary>
    [Fact]
    public void EBandUnitsAreNotDominatedByFrontOnlyShapes()
    {
        var db = Load();
        var eUnits = db.enemyUnits.Values.Where(u => MaxThreat(u) == 2).ToList();
        int frontOnly = eUnits.Count(u => Shape(u).back == 0);
        Assert.True(frontOnly * 2 <= eUnits.Count * 3,
            $"E帯 {eUnits.Count} 本のうち {frontOnly} 本が前衛のみで、多様性の目安（2/3以下）を超えています");
    }

    /// <summary>各追加ユニットが少なくとも1つのダンジョンから参照されている。</summary>
    [Fact]
    public void EveryVarietyUnitIsReachable()
    {
        var db = Load();
        var referenced = db.dungeons.Values.SelectMany(d => d.encounterTable)
            .Select(e => e.unitId)
            .Concat(db.allQuests.Where(q => q.BossEnemy != null).Select(q => q.BossEnemy!.id))
            .ToHashSet();

        string[] added = {
            "unit_goblin_line", "unit_goblin_pair",
            "unit_mage_wolf_alone", "unit_wight_alone",
            "unit_spider_and_bees", "unit_mage_wolf_hunt",
            "unit_wight_pair", "unit_undead_column",
            "unit_ranpos_hunt", "unit_acid_slime_swarm",
            "unit_bandit_back_line", "unit_highway_coalition",
        };
        foreach (var id in added)
            Assert.True(referenced.Contains(id), $"{id} がどのダンジョンからも参照されていません");
    }
}
