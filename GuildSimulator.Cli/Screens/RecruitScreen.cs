using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Systems.Guild;

namespace GuildSimulator.Cli.Screens;

public static class RecruitScreen
{
    public static void Show(List<AdventurerMasterData> candidates, GuildManager guild, int currentTurn)
    {
        while (true)
        {
            ConsoleHelper.Header($"冒険者雇入れ  （Turn {currentTurn} の候補）");
            Console.WriteLine($"  所持Gold: {guild.Gold}G   在籍冒険者: {guild.adventurers.Count}人");
            Console.WriteLine($"  ※候補は次のターンで入れ替わります");
            Console.WriteLine();

            if (candidates.Count == 0)
            {
                ConsoleHelper.Dim("  現在雇入れ可能な候補者はいません");
                ConsoleHelper.PressAnyKey();
                return;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                var m = candidates[i];
                bool alreadyHired = guild.adventurers.Any(a => a.master == m);
                int hireCost = CalcHireCost(m);
                string tag = alreadyHired ? " [雇用済]" : $"  雇用費: {hireCost}G";
                Console.WriteLine($"  {i + 1}. {m.baseName}  Lv{m.defaultLevel} Rank{m.defaultRank}  {m.DefaultClass?.className ?? "？"}/{m.Race?.raceName ?? "？"}  維持費{m.upkeepGold}G/T{tag}");
                Console.WriteLine($"       VIT:{m.vitality} MEN:{m.mental} STR:{m.strength} AGI:{m.agility} INT:{m.intelligence} CON:{m.constitution}");
                Console.WriteLine($"       武器:{m.DefaultWeapon?.displayName ?? "なし"}  防具:{m.DefaultArmor?.displayName ?? "なし"}");
            }

            Console.WriteLine("  0. 戻る");
            Console.Write("\n雇う候補の番号: ");
            if (!int.TryParse(Console.ReadLine(), out int sel) || sel == 0) return;
            if (sel < 1 || sel > candidates.Count) { ConsoleHelper.Error("無効な番号です"); continue; }

            var chosen = candidates[sel - 1];
            if (guild.adventurers.Any(a => a.master == chosen))
            {
                ConsoleHelper.Warn("すでに雇用済みです");
                ConsoleHelper.PressAnyKey();
                continue;
            }

            int cost = CalcHireCost(chosen);
            if (guild.Gold < cost)
            {
                ConsoleHelper.Error($"Goldが不足しています（必要: {cost}G  所持: {guild.Gold}G）");
                ConsoleHelper.PressAnyKey();
                continue;
            }

            Console.WriteLine($"\n  {chosen.baseName} を {cost}G で雇いますか？");
            if (!ConsoleHelper.Confirm("確認")) continue;

            guild.SpendGold(cost, $"雇用費: {chosen.baseName}");
            var adv = new AdventurerData(chosen);
            guild.AddAdventurer(adv);
            candidates.Remove(chosen);
            ConsoleHelper.Info($"{chosen.baseName} を雇いました！");
            ConsoleHelper.PressAnyKey();
        }
    }

    public static int CalcHireCost(AdventurerMasterData m)
        => Math.Max(10, m.upkeepGold * 5 + m.defaultLevel * 5);
}
