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
        Console.WriteLine($"    ユニット加算 : HP+{add.hp} SAN+{add.san} pAtk+{add.pAtk} pDef+{add.pDef} mAtk+{add.mAtk} mDef+{add.mDef}");
        Console.WriteLine($"    ユニット倍率 : HP x{mul.hp:0.##} pAtk x{mul.pAtk:0.##} mAtk x{mul.mAtk:0.##} pDef x{mul.pDef:0.##} mDef x{mul.mDef:0.##}");
        Console.WriteLine($"    報酬Gold     : x{RelicSystem.GetGoldRewardMultiplier():0.##}");
        Console.WriteLine($"    維持費       : x{RelicSystem.GetUpkeepMultiplier():0.##}");
        Console.WriteLine($"    休息回復     : x{RelicSystem.GetRestHealMultiplier():0.##}");

        ConsoleHelper.PressAnyKey();
    }
}
