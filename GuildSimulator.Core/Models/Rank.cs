namespace GuildSimulator.Core.Models;

/// <summary>
/// 冒険者・クエスト・ギルドに共通する認定ランク。F〜Sの7段階。
///
/// 3種類のランクは互いに突き合わせるので、同じ数直線に乗っている。
///   ・クエストランク ＞ ギルドランク  → そのクエストは掲示されない
///   ・クエストランク が冒険者の適正帯から外れる → クラス習熟度が入らない
///
/// マスタJSONとセーブデータには 1〜7 の整数で入っている（1=F、7=S）。
/// プレイヤーに見せるときは必ず <see cref="Label"/> を通すこと。
/// </summary>
public static class Rank
{
    /// <summary>最低ランク(F)。すべてのランクはここから始まる。</summary>
    public const int Min = 1;

    /// <summary>最高ランク(S)。ここで打ち止めになる。</summary>
    public const int Max = 7;

    // 添字は rank - Min。並べ替えるとセーブデータの意味が変わるので触らないこと。
    static readonly string[] Labels = { "F", "E", "D", "C", "B", "A", "S" };

    /// <summary>プレイヤーに見せる表記。範囲外は端に丸める。</summary>
    public static string Label(int rank) => Labels[Clamp(rank) - Min];

    public static int Clamp(int rank) => Math.Clamp(rank, Min, Max);

    public static bool IsMax(int rank) => rank >= Max;

    /// <summary>
    /// 適正ランクの上限幅。自分のランクからこの数だけ上までが適正帯。
    /// 格上すぎるクエストに丸投げして育てるのを防ぐための蓋。
    /// </summary>
    public const int SuitableRangeAbove = 2;

    /// <summary>
    /// そのクエストが冒険者にとって適正ランクか。
    /// 格下は学ぶものがなく、格上すぎると連れ回されているだけなので、どちらも習熟にはならない。
    /// </summary>
    public static bool IsSuitable(int questRank, int adventurerRank)
        => questRank >= adventurerRank && questRank <= adventurerRank + SuitableRangeAbove;

    /// <summary>冒険者から見た適正帯の表記（例: "D〜B"）。ヘルプや冒険者詳細でそのまま出せる。</summary>
    public static string SuitableRangeLabel(int adventurerRank)
        => RangeLabel(adventurerRank, adventurerRank + SuitableRangeAbove);

    /// <summary>
    /// クエストから見た「習熟度が入る冒険者ランク」の表記（例: "F〜D"）。
    /// <see cref="IsSuitable"/> を冒険者ランクについて解くと [questRank - 幅, questRank] になる。
    /// </summary>
    public static string SuitableAdventurerRangeLabel(int questRank)
        => RangeLabel(questRank - SuitableRangeAbove, questRank);

    static string RangeLabel(int low, int high)
    {
        int lo = Clamp(low);
        int hi = Clamp(high);
        return lo == hi ? Label(lo) : $"{Label(lo)}〜{Label(hi)}";
    }
}
