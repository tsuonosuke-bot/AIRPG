using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.GameData;

/// <summary>
/// 道中で手に入れた未開封の宝箱。中身は帰還してから抽選するので、
/// 拾った時点では何が入っているか分からない。
/// </summary>
public class TreasureChest
{
    public TreasureChestKind kind;

    /// <summary>見つけたエリア。報告の並び順を保つためだけに持つ。</summary>
    public int foundPhase;

    public bool IsBossChest => kind == TreasureChestKind.Boss;

    public string Label => IsBossChest ? "ボスの宝箱" : "宝箱";
}
