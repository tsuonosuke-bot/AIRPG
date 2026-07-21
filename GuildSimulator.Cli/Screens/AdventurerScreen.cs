using GuildSimulator.Core.GameData;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;

namespace GuildSimulator.Cli.Screens;

public static class AdventurerScreen
{
    public static void Show(GuildManager guild, QuestManager? questManager = null)
    {
        while (true)
        {
            ConsoleHelper.Header("冒険者一覧");
            var advs = guild.adventurers;
            for (int i = 0; i < advs.Count; i++)
            {
                var a = advs[i];
                string busy = questManager?.IsAdventurerBusy(a.id) == true ? "[出発中]" : "";
                string alive = a.isAlive ? "" : "[死亡]";
                Console.WriteLine($"  {i + 1}. {a.name} Lv{a.level} Rank{a.rank} {a.ClassAndRace} {busy}{alive}");
            }
            Console.WriteLine("  0. 戻る");
            Console.Write("番号を選択: ");
            var line = Console.ReadLine();
            if (!int.TryParse(line, out int sel) || sel == 0) return;
            if (sel < 1 || sel > advs.Count) { ConsoleHelper.Error("無効"); continue; }
            ShowDetail(advs[sel - 1], guild);
        }
    }

    static void ShowDetail(AdventurerData a, GuildManager guild)
    {
        ConsoleHelper.Header($"冒険者詳細: {a.name}");
        Console.WriteLine($"  クラス/種族 : {a.ClassAndRace}");
        Console.WriteLine($"  レベル      : {a.level}  (EXP {a.experience}/{a.RequiredExpForNextLevel})");
        Console.WriteLine($"  冒険者ランク: {a.rank}  (RP {a.rankPoint}/{a.RequiredRankPointForNextRank})");
        Console.WriteLine($"  状態        : {(a.isAlive ? "生存" : "死亡")}");
        Console.WriteLine();
        Console.WriteLine($"  VIT:{a.vitality} MEN:{a.mental} STR:{a.strength} AGI:{a.agility} INT:{a.intelligence} CON:{a.constitution}");
        var s = a.GetFinalCombatStats();
        int hpMax = a.CombatHpMax > 0 ? a.CombatHpMax : s.hp;
        int hpCur = a.CombatHpMax > 0 ? a.CombatHp : s.hp;
        Console.WriteLine($"  HP:{hpCur}/{hpMax}  pAtk:{s.pAtk} pDef:{s.pDef} mAtk:{s.mAtk} mDef:{s.mDef} hit:{s.hit} evd:{s.evade} heal:{s.heal}");
        Console.WriteLine();
        Console.WriteLine($"  武器: {a.weapon?.displayName ?? "なし"}");
        Console.WriteLine($"  防具: {a.armor?.displayName ?? "なし"}");
        Console.WriteLine();
        Console.Write("  スキル: ");
        var skills = a.Skills;
        Console.WriteLine(skills.Count == 0 ? "なし" : string.Join(", ", skills.Select(x => x.skillName)));
        ConsoleHelper.PressAnyKey();
    }
}
