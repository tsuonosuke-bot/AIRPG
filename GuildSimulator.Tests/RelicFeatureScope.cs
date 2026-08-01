using GuildSimulator.Core;

namespace GuildSimulator.Tests;

/// <summary>
/// 凍結中の遺物システムを、テストの間だけ一時的に切り替える。
/// <see cref="GameFeatures.RelicsEnabled"/> は静的なので、
/// 直列実行される "Guild static state" コレクションの中でのみ使うこと。
/// </summary>
sealed class RelicFeatureScope : IDisposable
{
    readonly bool _previous;

    public RelicFeatureScope(bool enabled = true)
    {
        _previous = GameFeatures.RelicsEnabled;
        GameFeatures.RelicsEnabled = enabled;
    }

    public void Dispose() => GameFeatures.RelicsEnabled = _previous;
}
