using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Game.Presentation;

namespace GuildSimulator.Game.Screens;

public static class InventoryScreen
{
    public static async Task ShowAsync(GuildManager guild)
    {
        Ui.BeginScreen();
        Ui.Header("倉庫");
        var stock = guild.GetInventoryView()
            .Where(s => s.count > 0)
            .OrderBy(s => s.item.type)
            .ThenBy(s => s.item.price)
            .ToList();

        var consumables = guild.GetConsumablesView().Where(s => s.count > 0).ToList();
        if (stock.Count == 0 && consumables.Count == 0)
        {
            Ui.Dim("  倉庫に装備はありません");
            Ui.Dim("  商店で購入するか、クエスト報酬で入手できます");
            await Ui.PauseAsync();
            return;
        }

        if (stock.Count > 0)
        {
            Ui.WriteLine($"  装備の種類: {stock.Count}種   合計: {stock.Sum(s => s.count)}個");
            Ui.WriteLine();
            for (int i = 0; i < stock.Count; i++)
            {
                var stack = stock[i];
                string kind = stack.item.type switch
                {
                    EquipmentType.Weapon => "武器",
                    EquipmentType.Armor => "防具",
                    EquipmentType.Accessory => "装飾",
                    _ => "その他",
                };
                int sellPrice = GuildManager.SellPrice(stack.item);
                Ui.Write($"  {i + 1}. [{kind}] ");
                Ui.WriteRarityName(stack.item.displayName, stack.item.rarity);
                Ui.WriteLine($"  x{stack.count}  売値{sellPrice}G/個");
                Ui.Dim($"       {DescribeEquipment(stack.item)}");
            }
        }

        if (consumables.Count > 0)
        {
            Ui.WriteLine();
            Ui.WriteLine($"  消費アイテム: {consumables.Sum(s => s.count)}個");
            foreach (var stack in consumables)
            {
                Ui.Write("    ・");
                Ui.WriteRarityName(stack.item.displayName, stack.item.rarity);
                Ui.WriteLine($" x{stack.count}");
                Ui.Dim($"       {stack.item.description}");
            }
        }

        await Ui.PauseAsync();
    }

    static string DescribeEquipment(EquipmentMasterData item)
    {
        var parts = new List<string>();
        if (item.type == EquipmentType.Weapon)
        {
            if (item.attackKind == AttackKind.Heal)
            {
                parts.Add($"回復効果x{item.healPower:0.##}");
            }
            else
            {
                parts.Add($"{(item.attackKind == AttackKind.Magic ? "魔法" : "物理")} PV{item.basePv}");
                if (!string.IsNullOrWhiteSpace(item.damageDice)) parts.Add($"{item.damageDice}/貫通");
                parts.Add(item.maxStatBonus >= QudCombatDefaults.UnlimitedStatBonus
                    ? "能力値上限なし" : $"能力値上限+{item.maxStatBonus}");
            }
        }
        parts.AddRange(BonusParts(item.bonus));
        parts.Add($"重量{item.weight}");
        parts.Add($"購入価格{item.price}G");
        return string.Join(" ", parts);
    }

    static List<string> BonusParts(StatBlock stats)
    {
        var parts = new List<string>();
        void Add(string name, int value)
        {
            if (value != 0) parts.Add($"{name}{(value > 0 ? "+" : "")}{value}");
        }

        Add("HP", stats.hp);
        Add("AV", stats.av);
        Add("mAV", stats.mav);
        Add("PV", stats.pv);
        Add("mPV", stats.mpv);
        Add("DV", stats.dv);
        Add("命中", stats.toHit);
        Add("回復力", stats.heal);
        return parts;
    }
}
