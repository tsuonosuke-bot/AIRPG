using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;

namespace GuildSimulator.Core.Systems.Guild;

public class GuildManager
{
    public const int GuildBaseUpkeepGoldPerTurn = 10;
    public const int UpkeepGoldPerLevel = 5;

    public List<AdventurerData> adventurers = new();
    public int Gold { get; private set; }
    public int GuildRank { get; private set; }
    public int GuildPoints { get; private set; }
    public List<RelicMasterData> relics = new();
    public List<string> economyLogs = new();
    readonly List<EquipmentStack> inventory = new();
    readonly List<ConsumableStack> consumables = new();
    public Dictionary<string, int> shopEquipmentStock = new();
    public Dictionary<string, int> shopConsumableStock = new();
    public int LastShopRefreshTurn { get; private set; }

    public GuildManager(int startGold = 50, int startRank = 1)
    {
        Gold = startGold;
        GuildRank = startRank;
        RelicSystem.SetRelics(relics);
        economyLogs.Add($"初期資金: {Gold}G");
    }

    public void AddGold(int amount, string reason)
    {
        Gold += amount;
        economyLogs.Add($"{reason}: {(amount >= 0 ? "+" : "")}{amount}G（所持 {Gold}G）");
    }

    public void SpendGold(int amount, string reason) => AddGold(-amount, reason);

    public void AddGuildPoints(int amount, string reason)
    {
        GuildPoints += amount;
        economyLogs.Add($"{reason}: ギルドポイント +{amount}（合計 {GuildPoints}）");
    }

    public void RankUp(int amount, string reason)
    {
        if (amount <= 0) return;
        GuildRank = Math.Max(1, GuildRank + amount);
        economyLogs.Add($"{reason}: 認定ランク → {GuildRank}");
    }

    public void AddAdventurer(AdventurerData adv)
    {
        adventurers.Add(adv);
        economyLogs.Add($"雇用: {adv.name}（維持費 {CalculateAdventurerUpkeep(adv.level)}G/Turn）");
    }

    /// <summary>セーブデータからの復元専用。経済ログは追加しない。</summary>
    public void RestoreEconomy(int gold, int guildRank, int guildPoints)
    {
        Gold = gold;
        GuildRank = guildRank;
        GuildPoints = guildPoints;
    }

    public static int CalculateAdventurerUpkeep(int level) =>
        Math.Max(1, level) * UpkeepGoldPerLevel;

    public int AdventurerUpkeepPerTurn =>
        adventurers.Where(a => a != null && a.isAlive).Sum(a => CalculateAdventurerUpkeep(a.level));

    public int BaseUpkeepPerTurn =>
        GuildBaseUpkeepGoldPerTurn + AdventurerUpkeepPerTurn;

    public int EffectiveUpkeepPerTurn => CalculateEffectiveUpkeep(BaseUpkeepPerTurn);

    public static int CalculateEffectiveUpkeep(int baseUpkeep) =>
        Math.Max(0, (int)Math.Floor(Math.Max(0, baseUpkeep) * RelicSystem.GetUpkeepMultiplier()));

    /// <summary>
    /// 報酬などの収入がない前提で、破産せずに支払える維持費の回数。
    /// Gold が0以下になった支払いターンにゲームオーバーとなるため、残金1Gを安全ラインとする。
    /// </summary>
    public static int SafeUpkeepTurns(int gold, int effectiveUpkeep)
    {
        if (effectiveUpkeep <= 0) return int.MaxValue;
        return Math.Max(0, (gold - 1) / effectiveUpkeep);
    }

    /// <summary>
    /// 現在のギルド維持費が所要ターン中ずっと続く前提で、基本報酬から差し引いた予想収支。
    /// ランダム報酬・採取超過・レベルアップによる維持費変動は含めない。
    /// </summary>
    public int EstimateNetAfterUpkeep(int rewardGold, int turns) =>
        rewardGold - EffectiveUpkeepPerTurn * Math.Max(0, turns);

    public int PayUpkeepForAll(int currentTurn)
    {
        int baseTotal = BaseUpkeepPerTurn;
        int effectiveTotal = CalculateEffectiveUpkeep(baseTotal);
        if (effectiveTotal > 0)
            SpendGold(effectiveTotal,
                $"[Turn {currentTurn}] 維持費支払い（ギルド基本 {GuildBaseUpkeepGoldPerTurn}G + 冒険者 {adventurers.Count}人）");
        return effectiveTotal;
    }

    public void AddRelic(RelicMasterData relic, string reason = "")
    {
        if (relics.Contains(relic)) return;
        relics.Add(relic);
        RelicSystem.SetRelics(relics);
        economyLogs.Add($"遺物入手: {relic.relicName}{(string.IsNullOrEmpty(reason) ? "" : $"（{reason}）")}");
    }

    // ---- Inventory ----
    public int GetCount(EquipmentMasterData item)
    {
        var s = inventory.FirstOrDefault(x => x.item == item);
        return s?.count ?? 0;
    }
    public bool Has(EquipmentMasterData item, int amount = 1) => GetCount(item) >= amount;

    public void AddEquipment(EquipmentMasterData item, int amount = 1, string reason = "")
    {
        var s = inventory.FirstOrDefault(x => x.item == item);
        if (s == null) { s = new EquipmentStack(item, 0); inventory.Add(s); }
        s.count += amount;
    }

    public bool TryConsumeEquipment(EquipmentMasterData item, int amount = 1, string reason = "")
    {
        var s = inventory.FirstOrDefault(x => x.item == item);
        if (s == null || s.count < amount) return false;
        s.count -= amount;
        if (s.count <= 0) inventory.Remove(s);
        return true;
    }

    public bool TryBuyEquipment(EquipmentMasterData item, int amount = 1)
    {
        int cost = item.price * amount;
        if (Gold < cost) return false;
        AddGold(-cost, $"購入: {item.displayName} x{amount}");
        AddEquipment(item, amount);
        return true;
    }

    /// <summary>買値の半額で売却する。売値は最低1G。</summary>
    public static int SellPrice(EquipmentMasterData item) => Math.Max(1, item.price / 2);

    public bool TrySellEquipment(EquipmentMasterData item, int amount = 1)
    {
        if (!TryConsumeEquipment(item, amount)) return false;
        AddGold(SellPrice(item) * amount, $"売却: {item.displayName} x{amount}");
        return true;
    }

    public IReadOnlyList<EquipmentStack> GetInventoryView() => inventory;

    // ---- Consumables ----
    public int GetConsumableCount(ConsumableMasterData item) =>
        consumables.FirstOrDefault(x => x.item == item)?.count ?? 0;

    public void AddConsumable(ConsumableMasterData item, int amount = 1)
    {
        if (amount <= 0) return;
        var stack = consumables.FirstOrDefault(x => x.item == item);
        if (stack == null) { stack = new ConsumableStack(item, 0); consumables.Add(stack); }
        stack.count += amount;
    }

    public bool TryConsumeConsumable(ConsumableMasterData item, int amount = 1)
    {
        var stack = consumables.FirstOrDefault(x => x.item == item);
        if (stack == null || amount <= 0 || stack.count < amount) return false;
        stack.count -= amount;
        if (stack.count == 0) consumables.Remove(stack);
        return true;
    }

    public IReadOnlyList<ConsumableStack> GetConsumablesView() => consumables;

    // ---- Shop stock ----
    public bool ShopNeedsRefresh(int currentTurn) =>
        LastShopRefreshTurn <= 0 || currentTurn - LastShopRefreshTurn >= 5;

    public void ReplaceShopStock(
        int currentTurn,
        Dictionary<string, int> equipmentStock,
        Dictionary<string, int> consumableStock)
    {
        LastShopRefreshTurn = currentTurn;
        shopEquipmentStock = equipmentStock;
        shopConsumableStock = consumableStock;
    }

    public void RestoreShopStock(
        int lastRefreshTurn,
        Dictionary<string, int> equipmentStock,
        Dictionary<string, int> consumableStock) =>
        ReplaceShopStock(lastRefreshTurn, equipmentStock, consumableStock);
}
