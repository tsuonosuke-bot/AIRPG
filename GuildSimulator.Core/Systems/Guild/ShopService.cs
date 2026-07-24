using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.Systems.Guild;

public static class ShopService
{
    public const int RefreshIntervalTurns = 5;

    public static bool RefreshIfNeeded(
        GuildManager guild,
        int currentTurn,
        IEnumerable<EquipmentMasterData> equipment,
        IEnumerable<ConsumableMasterData> consumables)
    {
        if (!guild.ShopNeedsRefresh(currentTurn)) return false;

        var equipmentPool = equipment.Where(e => !e.id.StartsWith("eq_drop_", StringComparison.Ordinal)).ToList();
        var equipmentStock = DrawDistinct(equipmentPool, 8)
            .ToDictionary(e => e.id, e => e.rarity >= Rarity.Rare ? 1 : GameRandom.Range(1, 4));
        var consumableStock = DrawDistinct(consumables.ToList(), 4)
            .ToDictionary(c => c.id, c => GameRandom.Range(1, 4));

        guild.ReplaceShopStock(currentTurn, equipmentStock, consumableStock);
        return true;
    }

    static List<T> DrawDistinct<T>(List<T> pool, int count)
    {
        var result = new List<T>();
        var available = new List<T>(pool);
        while (result.Count < count && available.Count > 0)
        {
            int index = GameRandom.Range(0, available.Count);
            result.Add(available[index]);
            available.RemoveAt(index);
        }
        return result;
    }
}
