namespace GuildSimulator.Core;

public static class GameRandom
{
    static readonly AsyncLocal<Random?> ScopedRandom = new();

    static Random Current => ScopedRandom.Value ?? Random.Shared;

    public static int Range(int min, int max) => Current.Next(min, max);
    public static float NextFloat() => (float)Current.NextDouble();
    public static float Range(float min, float max) => min + (float)Current.NextDouble() * (max - min);

    /// <summary>
    /// この処理スコープだけ乱数列を固定する。Balance Labや回帰テストで、同じseedから
    /// 同じ結果を再現するために使う。破棄すると呼び出し前の乱数源へ戻る。
    /// </summary>
    public static IDisposable UseSeed(int seed)
    {
        var previous = ScopedRandom.Value;
        ScopedRandom.Value = new Random(seed);
        return new RandomScope(previous);
    }

    sealed class RandomScope(Random? previous) : IDisposable
    {
        bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ScopedRandom.Value = previous;
        }
    }
}
