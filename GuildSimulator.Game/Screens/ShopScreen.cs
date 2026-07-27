using GuildSimulator.Game.Data;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Game.Presentation;

namespace GuildSimulator.Game.Screens;

public static class ShopScreen
{
    public static async Task ShowAsync(GameMasterData db, GuildManager guild, int currentTurn)
    {
        bool refreshed = ShopService.RefreshIfNeeded(
            guild, currentTurn, db.equipment.Values, db.consumables.Values);
        while (true)
        {
            Ui.BeginScreen();
            Ui.Header("商店");
            Ui.WriteLine($"  所持金: {guild.Gold}G");
            Ui.WriteLine($"  品ぞろえ更新: Turn {guild.LastShopRefreshTurn + ShopService.RefreshIntervalTurns}"
                + (refreshed ? "  [入荷しました]" : ""));
            Ui.WriteLine();

            string line = await Ui.SelectAsync("選択", new[]
            {
                new MenuOption("1", "装備を購入する"),
                new MenuOption("2", "消費アイテムを購入する"),
                new MenuOption("3", "倉庫の装備を売却する"),
                new MenuOption("0", "戻る", Style: TextStyle.Dim),
            });
            if (line == "1") await BuyAsync(db, guild);
            else if (line == "2") await BuyConsumablesAsync(db, guild);
            else if (line == "3") await SellAsync(guild);
            else return;
        }
    }

    static async Task BuyAsync(GameMasterData db, GuildManager guild)
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
            Ui.BeginScreen();
            Ui.Header("装備を購入");
            Ui.WriteLine($"  所持金: {guild.Gold}G");
            Ui.WriteLine();

            var options = new List<MenuOption>();
            for (int i = 0; i < items.Count; i++)
            {
                var e = items[i];
                string kind = e.type switch { EquipmentType.Weapon => "武器", EquipmentType.Accessory => "装飾", _ => "防具" };
                int owned = guild.GetCount(e);
                string ownedTag = owned > 0 ? $"  (所持x{owned})" : "";
                bool affordable = guild.Gold >= e.price;
                string price = affordable ? $"{e.price}G" : $"{e.price}G[不足]";
                int shopCount = guild.shopEquipmentStock[e.id];
                Ui.Write($"  {i + 1}. [{kind}] ");
                Ui.WriteRarityName(e.displayName, e.rarity);
                Ui.WriteLine($"  {price} 在庫x{shopCount}{ownedTag}");
                Ui.Dim($"       {DescribeEquip(e)}");

                options.Add(new MenuOption(
                    (i + 1).ToString(),
                    $"[{kind}] {e.displayName}  {price} 在庫x{shopCount}{ownedTag}",
                    DescribeEquip(e),
                    affordable ? Ui.RarityStyle(e.rarity) : TextStyle.Dim));
            }

            int? sel = await Ui.SelectIndexAsync("購入する装備", options);
            if (sel == null) return;

            var item = items[sel.Value - 1];
            if (guild.Gold < item.price)
            {
                Ui.Error($"資金が不足しています（必要: {item.price}G  所持: {guild.Gold}G）");
                await Ui.PauseAsync();
                continue;
            }
            if (!await Ui.ConfirmAsync($"{item.displayName} を {item.price}G で購入しますか？")) continue;
            if (guild.TryBuyEquipment(item))
            {
                guild.shopEquipmentStock[item.id]--;
                Ui.Info($"{item.displayName} を購入しました（倉庫へ）");
            }
            else
                Ui.Error("購入に失敗しました");
            await Ui.PauseAsync();
        }
    }

    static async Task BuyConsumablesAsync(GameMasterData db, GuildManager guild)
    {
        while (true)
        {
            var items = guild.shopConsumableStock
                .Where(kv => kv.Value > 0 && db.consumables.ContainsKey(kv.Key))
                .Select(kv => db.consumables[kv.Key])
                .OrderBy(c => c.price)
                .ToList();
            Ui.BeginScreen();
            Ui.Header("消費アイテムを購入");
            Ui.WriteLine($"  所持金: {guild.Gold}G");
            if (items.Count == 0)
            {
                Ui.Dim("  今期の在庫は売り切れです");
                await Ui.PauseAsync();
                return;
            }

            var options = new List<MenuOption>();
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                Ui.Write($"  {i + 1}. ");
                Ui.WriteRarityName(item.displayName, item.rarity);
                Ui.WriteLine($"  {item.price}G 在庫x{guild.shopConsumableStock[item.id]}"
                    + $" (所持x{guild.GetConsumableCount(item)})");
                Ui.Dim($"       {item.description}");

                options.Add(new MenuOption(
                    (i + 1).ToString(),
                    $"{item.displayName}  {item.price}G 在庫x{guild.shopConsumableStock[item.id]}"
                        + $" (所持x{guild.GetConsumableCount(item)})",
                    item.description,
                    guild.Gold >= item.price ? Ui.RarityStyle(item.rarity) : TextStyle.Dim));
            }

            int? selected = await Ui.SelectIndexAsync("購入するアイテム", options);
            if (selected == null) return;
            var chosen = items[selected.Value - 1];
            if (guild.Gold < chosen.price)
            {
                Ui.Error("資金が不足しています");
                await Ui.PauseAsync();
                continue;
            }
            if (!await Ui.ConfirmAsync($"{chosen.displayName} を {chosen.price}G で購入しますか？")) continue;
            guild.SpendGold(chosen.price, $"購入: {chosen.displayName}");
            guild.AddConsumable(chosen);
            guild.shopConsumableStock[chosen.id]--;
            Ui.Info($"{chosen.displayName} を購入しました");
            await Ui.PauseAsync();
        }
    }

    static async Task SellAsync(GuildManager guild)
    {
        while (true)
        {
            Ui.BeginScreen();
            Ui.Header("装備を売却");
            Ui.WriteLine($"  所持金: {guild.Gold}G  （売値は買値の半額）");
            Ui.WriteLine();

            var stock = guild.GetInventoryView().Where(s => s.count > 0).ToList();
            if (stock.Count == 0)
            {
                Ui.Dim("  倉庫に売却できる装備はありません");
                Ui.Dim("  （冒険者が装備中の品は倉庫にないため、先に外してください）");
                await Ui.PauseAsync();
                return;
            }

            var options = new List<MenuOption>();
            for (int i = 0; i < stock.Count; i++)
            {
                var st = stock[i];
                string kind = st.item.type switch { EquipmentType.Weapon => "武器", EquipmentType.Accessory => "装飾", _ => "防具" };
                Ui.Write($"  {i + 1}. [{kind}] ");
                Ui.WriteRarityName(st.item.displayName, st.item.rarity);
                Ui.WriteLine($"  x{st.count}  売値{GuildManager.SellPrice(st.item)}G/個");

                options.Add(new MenuOption(
                    (i + 1).ToString(),
                    $"[{kind}] {st.item.displayName}  x{st.count}",
                    $"売値{GuildManager.SellPrice(st.item)}G/個",
                    Ui.RarityStyle(st.item.rarity)));
            }

            int? sel = await Ui.SelectIndexAsync("売却する装備", options);
            if (sel == null) return;

            var chosen = stock[sel.Value - 1];
            int max = chosen.count;
            int qty = max == 1 ? 1 : await Ui.SelectIntAsync("売却個数", 1, max);
            int refund = GuildManager.SellPrice(chosen.item) * qty;
            if (!await Ui.ConfirmAsync($"{chosen.item.displayName} x{qty} を {refund}G で売却しますか？")) continue;
            if (guild.TrySellEquipment(chosen.item, qty))
                Ui.Info($"{chosen.item.displayName} x{qty} を売却しました（+{refund}G）");
            else
                Ui.Error("売却に失敗しました");
            await Ui.PauseAsync();
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
