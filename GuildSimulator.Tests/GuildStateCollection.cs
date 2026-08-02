using Xunit;

namespace GuildSimulator.Tests;

/// <summary>
/// <see cref="GuildSimulator.Core.Systems.Guild.FacilitySystem"/> と
/// <see cref="GuildSimulator.Core.Systems.Guild.RelicSystem"/> は静的な現在状態を持ち、
/// <c>new GuildManager(...)</c> のたびに上書きされる（1プロセス1セッションの前提で設計されている）。
///
/// そのため GuildManager を作るテストクラスを並列に走らせると、
/// 施設や遺物の効果が他のテストのぶんに差し替わって落ちる。
/// このコレクションに入れて直列化する。
/// </summary>
[CollectionDefinition("Guild static state", DisableParallelization = true)]
public sealed class GuildStateCollection
{
}
