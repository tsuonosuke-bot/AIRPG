using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.Systems.Guild;

public static class ShopService
{
    public const int RefreshIntervalTurns = 1;

    /// <summary>施設を1つも建てていないときの商店レベル。並ぶのは shopTier がこれ以下の装備だけ。</summary>
    public const int BaseShopLevel = 1;

    /// <summary>商店に並ばない、ドロップ専用装備のID接頭辞。</summary>
    public const string DropOnlyIdPrefix = "eq_drop_";

    public static bool RefreshIfNeeded(
        GuildManager guild,
        int currentTurn,
        IEnumerable<EquipmentMasterData> equipment,
        IEnumerable<ConsumableMasterData> consumables)
    {
        int shopLevel = BaseShopLevel + FacilitySystem.GetShopLevelBonus();
        var equipmentPool = equipment
            .Where(e => !e.id.StartsWith(DropOnlyIdPrefix, StringComparison.Ordinal) && e.shopTier <= shopLevel)
            .OrderBy(e => e.type)
            .ThenBy(e => e.price)
            .ThenBy(e => e.id, StringComparer.Ordinal)
            .ToList();

        // 市販品は抽選も在庫切れもなく常備する。値はセーブ互換のため1を入れるが、購入で減らさない。
        var equipmentStock = equipmentPool.ToDictionary(e => e.id, _ => 1);
        var consumableStock = consumables
            .OrderBy(c => c.price)
            .ThenBy(c => c.id, StringComparer.Ordinal)
            .ToDictionary(c => c.id, _ => 1);

        bool changed = !SameKeys(guild.shopEquipmentStock, equipmentStock)
            || !SameKeys(guild.shopConsumableStock, consumableStock);

        guild.ReplaceShopStock(currentTurn, equipmentStock, consumableStock);
        return changed;
    }

    static bool SameKeys<T>(IReadOnlyDictionary<string, int> current, IReadOnlyDictionary<string, T> expected) =>
        current.Count == expected.Count && current.Keys.All(expected.ContainsKey);
}
