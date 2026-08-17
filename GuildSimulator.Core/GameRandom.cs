namespace GuildSimulator.Core;

public static class GameRandom
{
    static readonly AsyncLocal<Random?> ScopedRandom = new();
    static readonly AsyncLocal<Random?> ScopedDropRandom = new();
    static readonly Random SharedDropRandom = new();
    static readonly object SharedDropLock = new();

    static Random Current => ScopedRandom.Value ?? Random.Shared;

    public static int Range(int min, int max) => Current.Next(min, max);
    public static float NextFloat() => (float)Current.NextDouble();
    public static float Range(float min, float max) => min + (float)Current.NextDouble() * (max - min);

    /// <summary>
    /// 敵固有ドロップ専用の乱数。戦利品を追加しても、その後の戦闘・成長・イベントの
    /// 乱数列がずれないよう、通常のゲーム乱数とは別の列を使う。
    /// </summary>
    public static float NextDropFloat()
    {
        var scoped = ScopedDropRandom.Value;
        if (scoped != null) return (float)scoped.NextDouble();
        lock (SharedDropLock) return (float)SharedDropRandom.NextDouble();
    }

    /// <summary>
    /// この処理スコープだけ乱数列を固定する。Balance Labや回帰テストで、同じseedから
    /// 同じ結果を再現するために使う。破棄すると呼び出し前の乱数源へ戻る。
    /// </summary>
    public static IDisposable UseSeed(int seed)
    {
        var previous = ScopedRandom.Value;
        var previousDrop = ScopedDropRandom.Value;
        ScopedRandom.Value = new Random(seed);
        ScopedDropRandom.Value = new Random(unchecked(seed ^ 0x5F37_59DF));
        return new RandomScope(previous, previousDrop);
    }

    sealed class RandomScope(Random? previous, Random? previousDrop) : IDisposable
    {
        bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ScopedRandom.Value = previous;
            ScopedDropRandom.Value = previousDrop;
        }
    }
}
