using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.Systems.Guild;

public static class ShopService
{
    public const int RefreshIntervalTurns = 5;

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
        if (!guild.ShopNeedsRefresh(currentTurn)) return false;

        int shopLevel = BaseShopLevel + FacilitySystem.GetShopLevelBonus();
        var equipmentPool = equipment
            .Where(e => !e.id.StartsWith(DropOnlyIdPrefix, StringComparison.Ordinal) && e.shopTier <= shopLevel)
            .ToList();

        // 最低ランクの防具（shopTierが最小の防具）は品揃えの運に左右されず常に並べる。
        var armorTiers = equipmentPool
            .Where(e => e.type == EquipmentType.Armor)
            .Select(e => e.shopTier);
        var guaranteedArmor = armorTiers.Any()
            ? equipmentPool.Where(e => e.type == EquipmentType.Armor && e.shopTier == armorTiers.Min()).ToList()
            : new List<EquipmentMasterData>();
        var remainingPool = equipmentPool.Except(guaranteedArmor).ToList();

        var equipmentStock = guaranteedArmor
            .Concat(DrawDistinct(remainingPool, Math.Max(0, 8 - guaranteedArmor.Count)))
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
