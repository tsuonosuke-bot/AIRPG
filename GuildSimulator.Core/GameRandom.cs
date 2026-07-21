namespace GuildSimulator.Core;

public static class GameRandom
{
    static readonly Random _rng = new();

    public static int Range(int min, int max) => _rng.Next(min, max);
    public static float NextFloat() => (float)_rng.NextDouble();
    public static float Range(float min, float max) => min + (float)_rng.NextDouble() * (max - min);
}
