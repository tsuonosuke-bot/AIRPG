using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;

namespace GuildSimulator.Core.GameData;

/// <summary>
/// 保存したパーティ編成。冒険者は実体ではなくIDで持つ。
/// 死亡・解雇・遠征中でも編成そのものは壊さず、呼び出すときに
/// 「いま連れて行ける人だけ」を並べ直す方が、毎回作り直すより手数が少ない。
/// </summary>
public sealed class PartyPreset
{
    public const int MaxNameLength = 20;

    public string name = "";

    /// <summary>前衛3＋後衛3の並び。空きスロットは null。</summary>
    public string?[] memberIds = new string?[GuildManager.FormationSlotCount];

    /// <summary>この編成で最後に選んだ遠征方針。呼び出したときの初期値になる。</summary>
    public ExpeditionPolicy policy = ExpeditionPolicy.SurvivalFirst;

    public int MemberCount => memberIds.Count(id => !string.IsNullOrEmpty(id));

    public static PartyPreset From(
        string name,
        IReadOnlyList<AdventurerData?> formation,
        ExpeditionPolicy policy)
    {
        var preset = new PartyPreset { name = name, policy = policy };
        for (int slot = 0; slot < preset.memberIds.Length && slot < formation.Count; slot++)
            preset.memberIds[slot] = formation[slot]?.id;
        return preset;
    }

    public PartyPreset Clone()
    {
        var copy = new PartyPreset { name = name, policy = policy };
        Array.Copy(memberIds, copy.memberIds, Math.Min(memberIds.Length, copy.memberIds.Length));
        return copy;
    }
}
