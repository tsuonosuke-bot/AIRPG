namespace GuildSimulator.Core.Models;

/// <summary>
/// StatBlockに対する倍率。AV/DV/PVは1点の重みが大きい小さな整数なので、
/// 倍率で触ってよいのはHP・SAN・回復量のような「量」の側だけにしてある。
/// </summary>
public struct StatMultiplier
{
    public float hp;
    public float san;
    public float heal;

    public static StatMultiplier One => new() { hp = 1f, san = 1f, heal = 1f };
}
