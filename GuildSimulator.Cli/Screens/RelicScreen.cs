using GuildSimulator.Core.GameData;
using GuildSimulator.Core.Systems.Guild;

namespace GuildSimulator.Cli.Screens;

public static class RelicScreen
{
    public static void Show(GuildManager guild)
    {
        ConsoleHelper.Header("所持遺物");
        var relics = guild.relics;

        if (relics.Count == 0)
        {
            ConsoleHelper.Dim("  まだ遺物を所持していません");
            ConsoleHelper.Dim("  遺物はクエストの選択報酬や道中の宝箱から入手できます");
            ConsoleHelper.PressAnyKey();
            return;
        }

        Console.WriteLine($"  所持数: {relics.Count}個  （効果は常時、全ユニットに適用されます）");
        Console.WriteLine();
        foreach (var r in relics)
        {
            Console.WriteLine($"  ◆ {r.relicName}");
            ConsoleHelper.Dim($"      {RewardOption.DescribeRelic(r)}");
        }

        // 現在の合計倍率をまとめて表示すると、遺物を重ねたときの効きが分かる。
        Console.WriteLine();
        Console.WriteLine("  ── 現在の合計効果 ──");
        RelicSystem.GetUnitModifiers(out var add, out var mul);
        Console.WriteLine($"    能力加算 : HP+{add.hp} 士気+{add.san} 物理攻撃+{add.pAtk} 物理防御+{add.pDef} 魔法攻撃+{add.mAtk} 魔法防御+{add.mDef}");
        Console.WriteLine($"    能力倍率 : HP x{mul.hp:0.##} 物理攻撃 x{mul.pAtk:0.##} 魔法攻撃 x{mul.mAtk:0.##} 物理防御 x{mul.pDef:0.##} 魔法防御 x{mul.mDef:0.##}");
        Console.WriteLine($"    報酬資金 : x{RelicSystem.GetGoldRewardMultiplier():0.##}");
        Console.WriteLine($"    維持費       : x{RelicSystem.GetUpkeepMultiplier():0.##}");
        Console.WriteLine($"    休息回復     : x{RelicSystem.GetRestHealMultiplier():0.##}");

        ConsoleHelper.PressAnyKey();
    }
}
