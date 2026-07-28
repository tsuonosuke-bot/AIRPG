using GuildSimulator.Core.GameData;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Game.Presentation;

namespace GuildSimulator.Game.Screens;

public static class RelicScreen
{
    public static async Task ShowAsync(GuildManager guild)
    {
        Ui.BeginScreen();
        Ui.Header("所持遺物");
        var relics = guild.relics;

        if (relics.Count == 0)
        {
            Ui.Dim("  まだ遺物を所持していません");
            Ui.Dim("  遺物はクエストの選択報酬や道中の宝箱から入手できます");
            await Ui.PauseAsync();
            return;
        }

        Ui.WriteLine($"  所持数: {relics.Count}個  （効果は常時、全ユニットに適用されます）");
        Ui.WriteLine();
        foreach (var r in relics)
        {
            Ui.WriteLine($"  ◆ {r.relicName}");
            Ui.Dim($"      {RewardDescription.DescribeRelic(r)}");
        }

        // 現在の合計倍率をまとめて表示すると、遺物を重ねたときの効きが分かる。
        Ui.WriteLine();
        Ui.WriteLine("  ── 現在の合計効果 ──");
        RelicSystem.GetUnitModifiers(out var add, out var mul);
        Ui.WriteLine($"    能力加算 : HP+{add.hp} 士気+{add.san} AV+{add.av} mAV+{add.mav} PV+{add.pv} mPV+{add.mpv} DV+{add.dv} 命中+{add.toHit}");
        Ui.WriteLine($"    能力倍率 : HP x{mul.hp:0.##} 士気 x{mul.san:0.##} 回復力 x{mul.heal:0.##}");
        Ui.WriteLine($"    報酬資金 : x{RelicSystem.GetGoldRewardMultiplier():0.##}");
        Ui.WriteLine($"    維持費       : x{RelicSystem.GetUpkeepMultiplier():0.##}");
        Ui.WriteLine($"    休息回復     : x{RelicSystem.GetRestHealMultiplier():0.##}");

        await Ui.PauseAsync();
    }
}
