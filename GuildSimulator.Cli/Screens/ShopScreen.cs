using GuildSimulator.Cli.Data;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;

namespace GuildSimulator.Cli.Screens;

public static class ShopScreen
{
    public static void Show(GameMasterData db, GuildManager guild, int currentTurn)
    {
        bool refreshed = ShopService.RefreshIfNeeded(
            guild, currentTurn, db.equipment.Values, db.consumables.Values);
        while (true)
        {
            ConsoleHelper.Header("商店");
            Console.WriteLine($"  所持金: {guild.Gold}G");
            Console.WriteLine($"  品ぞろえ更新: Turn {guild.LastShopRefreshTurn + ShopService.RefreshIntervalTurns}"
                + (refreshed ? "  [入荷しました]" : ""));
            Console.WriteLine();
            Console.WriteLine("  1. 装備を購入する");
            Console.WriteLine("  2. 消費アイテムを購入する");
            Console.WriteLine("  3. 倉庫の装備を売却する");
            Console.WriteLine("  0. 戻る");
            Console.Write("選択: ");
            var line = Console.ReadLine()?.Trim();
            if (line == "1") Buy(db, guild);
            else if (line == "2") BuyConsumables(db, guild);
            else if (line == "3") Sell(guild);
            else return;
        }
    }

    static void Buy(GameMasterData db, GuildManager guild)
    {
        // 武器→防具の順、価格昇順で並べる。
        var items = guild.shopEquipmentStock
            .Where(kv => kv.Value > 0 && db.equipment.ContainsKey(kv.Key))
            .Select(kv => db.equipment[kv.Key])
            .OrderBy(e => e.type)
            .ThenBy(e => e.price)
            .ToList();

        while (true)
        {
            ConsoleHelper.Header("装備を購入");
            Console.WriteLine($"  所持金: {guild.Gold}G");
            Console.WriteLine();

            for (int i = 0; i < items.Count; i++)
            {
                var e = items[i];
                string kind = e.type == EquipmentType.Weapon ? "武器" : "防具";
                int owned = guild.GetCount(e);
                string ownedTag = owned > 0 ? $"  (所持x{owned})" : "";
                bool affordable = guild.Gold >= e.price;
                string price = affordable ? $"{e.price}G" : $"{e.price}G[不足]";
                int shopCount = guild.shopEquipmentStock[e.id];
                Console.Write($"  {i + 1}. [{kind}] ");
                ConsoleHelper.WriteRarityName(e.displayName, e.rarity);
                Console.WriteLine($"  {price} 在庫x{shopCount}{ownedTag}");
                ConsoleHelper.Dim($"       {DescribeEquip(e)}");
            }
            Console.WriteLine("  0. 戻る");
            Console.Write($"購入する装備 [0-{items.Count}]: ");
            if (!int.TryParse(Console.ReadLine(), out int sel) || sel <= 0 || sel > items.Count) return;

            var item = items[sel - 1];
            if (guild.Gold < item.price)
            {
                ConsoleHelper.Error($"資金が不足しています（必要: {item.price}G  所持: {guild.Gold}G）");
                ConsoleHelper.PressAnyKey();
                continue;
            }
            if (!ConsoleHelper.Confirm($"{item.displayName} を {item.price}G で購入しますか？")) continue;
            if (guild.TryBuyEquipment(item))
            {
                guild.shopEquipmentStock[item.id]--;
                ConsoleHelper.Info($"{item.displayName} を購入しました（倉庫へ）");
            }
            else
                ConsoleHelper.Error("購入に失敗しました");
            ConsoleHelper.PressAnyKey();
        }
    }

    static void BuyConsumables(GameMasterData db, GuildManager guild)
    {
        while (true)
        {
            var items = guild.shopConsumableStock
                .Where(kv => kv.Value > 0 && db.consumables.ContainsKey(kv.Key))
                .Select(kv => db.consumables[kv.Key])
                .OrderBy(c => c.price)
                .ToList();
            ConsoleHelper.Header("消費アイテムを購入");
            Console.WriteLine($"  所持金: {guild.Gold}G");
            if (items.Count == 0)
            {
                ConsoleHelper.Dim("  今期の在庫は売り切れです");
                ConsoleHelper.PressAnyKey();
                return;
            }
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                Console.Write($"  {i + 1}. ");
                ConsoleHelper.WriteRarityName(item.displayName, item.rarity);
                Console.WriteLine($"  {item.price}G 在庫x{guild.shopConsumableStock[item.id]}"
                    + $" (所持x{guild.GetConsumableCount(item)})");
                ConsoleHelper.Dim($"       {item.description}");
            }
            Console.WriteLine("  0. 戻る");
            Console.Write($"購入するアイテム [0-{items.Count}]: ");
            if (!int.TryParse(Console.ReadLine(), out int selected) || selected <= 0 || selected > items.Count) return;
            var chosen = items[selected - 1];
            if (guild.Gold < chosen.price)
            {
                ConsoleHelper.Error("資金が不足しています");
                ConsoleHelper.PressAnyKey();
                continue;
            }
            if (!ConsoleHelper.Confirm($"{chosen.displayName} を {chosen.price}G で購入しますか？")) continue;
            guild.SpendGold(chosen.price, $"購入: {chosen.displayName}");
            guild.AddConsumable(chosen);
            guild.shopConsumableStock[chosen.id]--;
            ConsoleHelper.Info($"{chosen.displayName} を購入しました");
            ConsoleHelper.PressAnyKey();
        }
    }

    static void Sell(GuildManager guild)
    {
        while (true)
        {
            ConsoleHelper.Header("装備を売却");
            Console.WriteLine($"  所持金: {guild.Gold}G  （売値は買値の半額）");
            Console.WriteLine();

            var stock = guild.GetInventoryView().Where(s => s.count > 0).ToList();
            if (stock.Count == 0)
            {
                ConsoleHelper.Dim("  倉庫に売却できる装備はありません");
                ConsoleHelper.Dim("  （冒険者が装備中の品は倉庫にないため、先に外してください）");
                ConsoleHelper.PressAnyKey();
                return;
            }

            for (int i = 0; i < stock.Count; i++)
            {
                var st = stock[i];
                string kind = st.item.type == EquipmentType.Weapon ? "武器" : "防具";
                Console.Write($"  {i + 1}. [{kind}] ");
                ConsoleHelper.WriteRarityName(st.item.displayName, st.item.rarity);
                Console.WriteLine($"  x{st.count}  売値{GuildManager.SellPrice(st.item)}G/個");
            }
            Console.WriteLine("  0. 戻る");
            Console.Write($"売却する装備 [0-{stock.Count}]: ");
            if (!int.TryParse(Console.ReadLine(), out int sel) || sel <= 0 || sel > stock.Count) return;

            var chosen = stock[sel - 1];
            int max = chosen.count;
            int qty = max == 1 ? 1 : ConsoleHelper.PromptInt("売却個数", 1, max);
            int refund = GuildManager.SellPrice(chosen.item) * qty;
            if (!ConsoleHelper.Confirm($"{chosen.item.displayName} x{qty} を {refund}G で売却しますか？")) continue;
            if (guild.TrySellEquipment(chosen.item, qty))
                ConsoleHelper.Info($"{chosen.item.displayName} x{qty} を売却しました（+{refund}G）");
            else
                ConsoleHelper.Error("売却に失敗しました");
            ConsoleHelper.PressAnyKey();
        }
    }

    static string DescribeEquip(EquipmentMasterData item)
    {
        var parts = new List<string>();
        if (item.type == EquipmentType.Weapon)
        {
            if (item.physicalCoeff > 0f && item.physicalCoeff != 1f) parts.Add($"物理威力x{item.physicalCoeff:0.##}");
            if (item.magicCoeff > 0f) parts.Add($"魔法威力x{item.magicCoeff:0.##}");
            if (item.healCoeff > 0f) parts.Add($"回復効果x{item.healCoeff:0.##}");
        }
        parts.AddRange(BonusParts(item.bonus));
        parts.Add($"重量{item.weight}");
        return string.Join(" ", parts);
    }

    static List<string> BonusParts(StatBlock b)
    {
        var parts = new List<string>();
        void Add(string name, int v) { if (v != 0) parts.Add($"{name}{(v > 0 ? "+" : "")}{v}"); }
        Add("HP", b.hp);
        Add("物理攻撃", b.pAtk);
        Add("物理防御", b.pDef);
        Add("魔法攻撃", b.mAtk);
        Add("魔法防御", b.mDef);
        Add("命中", b.hit);
        Add("回避", b.evade);
        Add("回復力", b.heal);
        return parts;
    }
}
