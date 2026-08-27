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

    /// <summary>
    /// 採取クエストの <c>gatherChance</c> の下限。達成率そのものは
    /// <see cref="GatherSuccessPercent"/> で厳密に見るので、これは<b>手触りのための下限</b>。
    /// 判定回数が少ないと、同じ達成率でも「数回の当たり外れで決まった」感触になり、
    /// 少しずつ集まっていく手応えが消える。希少さは1回の採取量の幅で表すこと。
    /// </summary>
    public const float MinGatherChance = 0.65f;

    /// <summary>
    /// 予定エリア内に採取目標へ届く確率(%)の帯。ランクが上がるほど低くし、
    /// 「延長するか引き上げるか」の判断が奥へ行くほど頻繁に起きるようにする。
    /// 届かなくても撤退が確定するわけではなく、行程を延ばせば取り返せる
    /// （<c>QuestManager.ResolveGatherDecision</c>）ので、ここは失敗率ではなく
    /// <b>予定どおりに帰れる率</b>だと読む。
    /// </summary>
    static readonly Dictionary<int, Band> _gatherSuccess = new()
    {
        [1] = new(76, 84), [2] = new(72, 80), [3] = new(68, 76), [4] = new(65, 73),
        [5] = new(63, 71), [6] = new(61, 69), [7] = new(60, 68),
    };

    public static Band? GatherSuccessForRank(int rank) =>
        _gatherSuccess.TryGetValue(rank, out var b) ? b : (Band?)null;

    /// <summary>そのクエストで見込める採取量。ボスのエリアでは採取判定が起きない。</summary>
    public static float ExpectedGatherYield(int totalPhases, int bossPhase, bool hasBoss,
        float gatherChance, int minPerEvent, int maxPerEvent)
        => GatherPhases(totalPhases, bossPhase, hasBoss)
            * gatherChance * (minPerEvent + maxPerEvent) / 2f;

    /// <summary>
    /// 予定エリア内に <paramref name="targetCount"/> 個へ届く確率(%)。
    ///
    /// 期待量が目標の何倍か、という近似では足りない。同じ期待量でも
    /// 「毎エリア1〜2個」と「たまに大量」ではブレの幅がまるで違い、達成率が10ポイント以上ずれる。
    /// エリアごとの畳み込みで分布そのものを出す。目標に届いた時点で採取は止まるので、
    /// 到達済みの確率は吸収状態にまとめる。
    /// </summary>
    public static float GatherSuccessPercent(int totalPhases, int bossPhase, bool hasBoss,
        float gatherChance, int minPerEvent, int maxPerEvent, int targetCount)
    {
        if (targetCount <= 0) return 100f;

        int phases = GatherPhases(totalPhases, bossPhase, hasBoss);
        if (phases <= 0) return 0f;

        float hit = Math.Clamp(gatherChance, 0f, 1f);
        int low = Math.Max(0, minPerEvent);
        int high = Math.Max(low, maxPerEvent);
        double perValue = hit / (high - low + 1);

        // dist[n] = ここまでに n 個集まっている確率。dist[targetCount] は到達済み（吸収状態）。
        var dist = new double[targetCount + 1];
        dist[0] = 1;
        var next = new double[targetCount + 1];
        for (int phase = 0; phase < phases; phase++)
        {
            Array.Clear(next);
            next[targetCount] += dist[targetCount];
            for (int held = 0; held < targetCount; held++)
            {
                double weight = dist[held];
                if (weight <= 0) continue;
                next[held] += weight * (1 - hit);
                for (int got = low; got <= high; got++)
                    next[Math.Min(targetCount, held + got)] += weight * perValue;
            }
            (dist, next) = (next, dist);
        }
        return (float)(dist[targetCount] * 100);
    }

    static int GatherPhases(int totalPhases, int bossPhase, bool hasBoss) =>
        hasBoss && bossPhase >= 1 && bossPhase <= totalPhases ? totalPhases - 1 : totalPhases;

    public sealed record EnemyBand(Band Hp, Band Av, Band Dv, Band Pv, Band Exp);

    public sealed record QuestBand(Band RewardGold, Band RewardExp, Band GuildPoints, Band TotalPhases);

    public sealed record EquipmentBand(Band WeaponBasePv, Band ArmorAv, Band ArmorDv, Band BlockChance, Band BlockAv);

    /// <summary>脅威度(1〜7 = F〜S)ごとの敵の帯。</summary>
    static readonly Dictionary<int, EnemyBand> _enemies = new()
    {
        [1] = new(new(7, 30), new(0, 2), new(2, 9), new(-1, 3), new(3, 6)),
        [2] = new(new(20, 42), new(0, 5), new(4, 6), new(0, 4), new(7, 10)),
        [3] = new(new(25, 70), new(0, 8), new(3, 6), new(0, 6), new(11, 16)),
        [4] = new(new(32, 90), new(1, 12), new(0, 8), new(1, 7), new(16, 24)),
        [5] = new(new(50, 120), new(2, 14), new(3, 10), new(2, 9), new(28, 30)),
        [6] = new(new(80, 160), new(3, 16), new(4, 11), new(3, 11), new(34, 38)),
        [7] = new(new(120, 220), new(4, 18), new(5, 12), new(4, 13), new(45, 70)),
    };

    /// <summary>クエストランク(1〜7 = F〜S)ごとの報酬の帯。</summary>
    static readonly Dictionary<int, QuestBand> _quests = new()
    {
        [1] = new(new(80, 240), new(10, 45), new(8, 80), new(5, 14)),
        [2] = new(new(400, 650), new(40, 100), new(15, 120), new(12, 18)),
        [3] = new(new(750, 1300), new(100, 150), new(25, 150), new(16, 24)),
        [4] = new(new(1200, 2100), new(150, 220), new(40, 220), new(20, 30)),
        [5] = new(new(1900, 3200), new(220, 300), new(60, 300), new(25, 35)),
        [6] = new(new(2900, 4600), new(300, 400), new(80, 400), new(30, 40)),
        [7] = new(new(4300, 6500), new(400, 550), new(100, 500), new(35, 45)),
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
        [1] = new(1, Rank.LevelCap(1)),
        [2] = new(Rank.LevelCap(1) + 1, Rank.LevelCap(2)),
        [3] = new(Rank.LevelCap(2) + 1, Rank.LevelCap(3)),
        [4] = new(Rank.LevelCap(3) + 1, Rank.LevelCap(4)),
        [5] = new(Rank.LevelCap(4) + 1, Rank.LevelCap(5)),
        [6] = new(Rank.LevelCap(5) + 1, Rank.LevelCap(6)),
        [7] = new(Rank.LevelCap(6) + 1, Rank.LevelCap(7)),
    };

    public static EnemyBand? ForThreat(int threat) => _enemies.GetValueOrDefault(threat);

    public static QuestBand? ForQuestRank(int rank) => _quests.GetValueOrDefault(rank);

    public static EquipmentBand? ForShopTier(int tier) => _equipment.GetValueOrDefault(tier);

    public static Band? LevelsForRank(int rank) => _levels.TryGetValue(rank, out var b) ? b : (Band?)null;

    /// <summary>ランク表記つきの見出し。警告文をどの帯の話か分かるようにするために使う。</summary>
    public static string BandLabel(int rank) => $"{Rank.Label(rank)}帯";
}
