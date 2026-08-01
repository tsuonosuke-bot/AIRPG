using GuildSimulator.Core.Models;

namespace GuildSimulator.Game.Data;

/// <summary>数値がどのくらいの大きさになるはずかを表す範囲。両端を含む。</summary>
public readonly record struct Band(int Min, int Max)
{
    public bool Contains(int value) => value >= Min && value <= Max;
    public override string ToString() => $"{Min}〜{Max}";
}

/// <summary>
/// MASTER_DATA.md「ランク帯の物差し」の数値をコードに写したもの。
/// マスタを大量に足すときに、値が帯からはみ出していないかを機械的に確かめるために使う。
///
/// ここからの逸脱は<b>エラーではなく警告</b>として報告する。帯は「作りたいゲームの形」であって
/// 現時点のマスタの姿ではなく、C〜Sのデータを入れ終えるまでは既存データのほうが帯の外にいるため。
/// 警告が0になったときが、そのランク帯のデータが揃ったときになる。
/// </summary>
public static class RankBandTable
{
    /// <summary>商店に並ぶ上限。これより上のTierは宝箱とボスドロップ専用にする。</summary>
    public const int MaxShopTier = 3;

    /// <summary>装備Tierの上限。</summary>
    public const int MaxEquipmentTier = 5;

    public sealed record EnemyBand(Band Hp, Band Av, Band Dv, Band Pv, Band Exp);

    public sealed record QuestBand(Band RewardGold, Band RewardExp, Band GuildPoints, Band TotalPhases);

    public sealed record EquipmentBand(Band WeaponBasePv, Band ArmorAv, Band ArmorDv, Band BlockChance, Band BlockAv);

    /// <summary>脅威度(1〜7 = F〜S)ごとの敵の帯。</summary>
    static readonly Dictionary<int, EnemyBand> _enemies = new()
    {
        [1] = new(new(7, 30), new(0, 2), new(2, 9), new(-1, 3), new(12, 20)),
        [2] = new(new(20, 42), new(0, 5), new(4, 6), new(0, 4), new(16, 26)),
        [3] = new(new(25, 70), new(0, 8), new(3, 6), new(0, 6), new(20, 32)),
        [4] = new(new(32, 90), new(1, 12), new(0, 8), new(1, 7), new(28, 42)),
        [5] = new(new(50, 120), new(2, 14), new(3, 10), new(2, 9), new(30, 46)),
        [6] = new(new(80, 160), new(3, 16), new(4, 11), new(3, 11), new(32, 50)),
        [7] = new(new(120, 220), new(4, 18), new(5, 12), new(4, 13), new(38, 60)),
    };

    /// <summary>クエストランク(1〜7 = F〜S)ごとの報酬の帯。</summary>
    static readonly Dictionary<int, QuestBand> _quests = new()
    {
        [1] = new(new(100, 300), new(300, 600), new(8, 80), new(5, 12)),
        [2] = new(new(600, 1500), new(600, 1100), new(15, 120), new(12, 18)),
        [3] = new(new(1500, 3500), new(1100, 1800), new(25, 150), new(16, 24)),
        [4] = new(new(3000, 6500), new(2000, 3000), new(40, 220), new(20, 30)),
        [5] = new(new(5000, 11000), new(2600, 3700), new(60, 300), new(25, 35)),
        [6] = new(new(8000, 16000), new(3000, 4300), new(80, 400), new(30, 40)),
        [7] = new(new(12000, 22000), new(4200, 6000), new(100, 500), new(35, 45)),
    };

    /// <summary>
    /// shopTierごとの装備の帯。武器の <c>basePv</c> は剣を基準線に、
    /// 他の武器種の相対差（短剣 -2 / 槍 +1〜+2 / 斧 -1〜-2）と片手・両手の差を吸収した幅にしてある。
    /// </summary>
    static readonly Dictionary<int, EquipmentBand> _equipment = new()
    {
        [1] = new(new(1, 6), new(0, 3), new(-4, 1), new(25, 40), new(4, 7)),
        [2] = new(new(2, 7), new(1, 4), new(-4, 0), new(30, 45), new(6, 10)),
        [3] = new(new(3, 9), new(5, 6), new(-4, 0), new(35, 50), new(8, 12)),
        [4] = new(new(5, 11), new(7, 7), new(-5, 0), new(40, 55), new(10, 14)),
        [5] = new(new(6, 13), new(8, 9), new(-5, 0), new(45, 60), new(12, 16)),
    };

    /// <summary>その帯の冒険者が到達しているはずのレベル。</summary>
    static readonly Dictionary<int, Band> _levels = new()
    {
        [1] = new(1, 5), [2] = new(6, 10), [3] = new(11, 16), [4] = new(17, 24),
        [5] = new(25, 32), [6] = new(33, 40), [7] = new(41, 50),
    };

    public static EnemyBand? ForThreat(int threat) => _enemies.GetValueOrDefault(threat);

    public static QuestBand? ForQuestRank(int rank) => _quests.GetValueOrDefault(rank);

    public static EquipmentBand? ForShopTier(int tier) => _equipment.GetValueOrDefault(tier);

    public static Band? LevelsForRank(int rank) => _levels.TryGetValue(rank, out var b) ? b : (Band?)null;

    /// <summary>ランク表記つきの見出し。警告文をどの帯の話か分かるようにするために使う。</summary>
    public static string BandLabel(int rank) => $"{Rank.Label(rank)}帯";
}
