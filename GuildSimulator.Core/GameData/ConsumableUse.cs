using GuildSimulator.Core.MasterData;

namespace GuildSimulator.Core.GameData;

/// <summary>遠征へ持ち込む消費アイテム1個と、必要ならその効果対象。</summary>
public sealed class ConsumableUse
{
    public ConsumableMasterData item;
    public AdventurerData? target;

    public ConsumableUse(ConsumableMasterData item, AdventurerData? target = null)
    {
        this.item = item;
        this.target = target;
    }

    public string DisplayName => target == null
        ? item.displayName
        : $"{item.displayName} → {target.name}";
}
