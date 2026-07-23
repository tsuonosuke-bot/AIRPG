using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;

namespace GuildSimulator.Cli.Screens;

public static class InventoryScreen
{
    public static void Show(GuildManager guild)
    {
        ConsoleHelper.Header("倉庫インベントリ");
        var stock = guild.GetInventoryView()
            .Where(s => s.count > 0)
            .OrderBy(s => s.item.type)
            .ThenBy(s => s.item.price)
            .ToList();

        if (stock.Count == 0)
        {
            ConsoleHelper.Dim("  倉庫に装備はありません");
            ConsoleHelper.Dim("  商店で購入するか、クエスト報酬で入手できます");
            ConsoleHelper.PressAnyKey();
            return;
        }

        Console.WriteLine($"  装備の種類: {stock.Count}種   合計: {stock.Sum(s => s.count)}個");
        Console.WriteLine();
        for (int i = 0; i < stock.Count; i++)
        {
            var stack = stock[i];
            string kind = stack.item.type == EquipmentType.Weapon ? "武器" : "防具";
            int sellPrice = GuildManager.SellPrice(stack.item);
            Console.WriteLine($"  {i + 1}. [{kind}] {stack.item.displayName}  x{stack.count}  売値{sellPrice}G/個");
            ConsoleHelper.Dim($"       {DescribeEquipment(stack.item)}");
        }

        ConsoleHelper.PressAnyKey();
    }

    static string DescribeEquipment(EquipmentMasterData item)
    {
        var parts = new List<string>();
        if (item.type == EquipmentType.Weapon)
        {
            if (item.physicalCoeff > 0f && Math.Abs(item.physicalCoeff - 1f) > 0.001f)
                parts.Add($"物理威力x{item.physicalCoeff:0.##}");
            if (item.magicCoeff > 0f)
                parts.Add($"魔法威力x{item.magicCoeff:0.##}");
            if (item.healCoeff > 0f)
                parts.Add($"回復効果x{item.healCoeff:0.##}");
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
        Add("物理攻撃", stats.pAtk);
        Add("物理防御", stats.pDef);
        Add("魔法攻撃", stats.mAtk);
        Add("魔法防御", stats.mDef);
        Add("命中", stats.hit);
        Add("回避", stats.evade);
        Add("回復力", stats.heal);
        return parts;
    }
}
