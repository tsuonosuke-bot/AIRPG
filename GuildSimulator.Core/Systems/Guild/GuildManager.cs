using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;

namespace GuildSimulator.Core.Systems.Guild;

public class GuildManager
{
    public List<AdventurerData> adventurers = new();
    public int Gold { get; private set; }
    public int GuildRank { get; private set; }
    public int GuildPoints { get; private set; }
    public List<RelicMasterData> relics = new();
    public List<string> economyLogs = new();
    readonly List<EquipmentStack> inventory = new();

    public GuildManager(int startGold = 50, int startRank = 1)
    {
        Gold = startGold;
        GuildRank = startRank;
        RelicSystem.SetRelics(relics);
        economyLogs.Add($"初期資金: Gold {Gold}");
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
        economyLogs.Add($"{reason}: +{amount}GP（{GuildPoints}GP）");
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
        economyLogs.Add($"雇用: {adv.name}（維持費 {adv.master.upkeepGold}G/Turn）");
    }

    public int PayUpkeepForAll(int currentTurn)
    {
        int total = adventurers.Where(a => a != null && a.isAlive).Sum(a => a.master.upkeepGold);
        if (total > 0)
            SpendGold((int)Math.Floor(total * RelicSystem.GetUpkeepMultiplier()),
                $"[Turn {currentTurn}] 賃金支払い（{adventurers.Count}人）");
        return total;
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

    public IReadOnlyList<EquipmentStack> GetInventoryView() => inventory;
}
