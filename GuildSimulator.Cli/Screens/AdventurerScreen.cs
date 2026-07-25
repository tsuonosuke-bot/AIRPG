using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
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
                Console.Write($"  {i + 1}. ");
                ConsoleHelper.WriteRarityName(a.name, a.master.rarity);
                Console.Write($" Lv{a.level} Rank{a.rank} {a.ClassAndRace} {busy}");
                if (!a.isAlive)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write("[死亡]");
                    Console.ResetColor();
                }
                Console.WriteLine();
            }
            Console.WriteLine("  0. 戻る");
            Console.Write("番号を選択: ");
            var line = Console.ReadLine();
            if (!int.TryParse(line, out int sel) || sel == 0) return;
            if (sel < 1 || sel > advs.Count) { ConsoleHelper.Error("無効"); continue; }
            ShowDetail(advs[sel - 1], guild, questManager);
        }
    }

    static void ShowDetail(AdventurerData a, GuildManager guild, QuestManager? questManager)
    {
        while (true)
        {
            ConsoleHelper.Header($"冒険者詳細: {a.name}");
            Console.WriteLine($"  クラス/種族 : {a.ClassAndRace}");
            Console.WriteLine($"  レベル      : {a.level}  (経験値 {a.experience}/{a.RequiredExpForNextLevel})");
            Console.WriteLine($"  冒険者ランク: {a.rank}  (RP {a.rankPoint}/{a.RequiredRankPointForNextRank})");
            Console.WriteLine($"  維持費      : {GuildManager.CalculateAdventurerUpkeep(a.level)}G/T（Lv×{GuildManager.UpkeepGoldPerLevel}G）");
            Console.WriteLine($"  状態        : {(a.isAlive ? "生存" : "死亡")}");
            Console.WriteLine();
            Console.WriteLine($"  VIT:{a.vitality} MEN:{a.mental} STR:{a.strength} AGI:{a.agility} INT:{a.intelligence} CON:{a.constitution}");
            var s = a.GetFinalCombatStats();
            int hpMax = a.CombatHpMax > 0 ? a.CombatHpMax : s.hp;
            int hpCur = a.CombatHpMax > 0 ? a.CombatHp : s.hp;
            Console.WriteLine($"  HP:{hpCur}/{hpMax}  物理攻撃:{s.pAtk} 物理防御:{s.pDef} 魔法攻撃:{s.mAtk} 魔法防御:{s.mDef}");
            Console.WriteLine($"  命中:{s.hit} 回避:{s.evade} 回復力:{s.heal}");
            Console.WriteLine();
            Console.Write("  武器: ");
            if (a.weapon != null) ConsoleHelper.WriteRarityName(a.weapon.displayName, a.weapon.rarity); else Console.Write("なし");
            Console.WriteLine(DescribeItem(a.weapon));
            Console.Write("  防具: ");
            if (a.armor != null) ConsoleHelper.WriteRarityName(a.armor.displayName, a.armor.rarity); else Console.Write("なし");
            Console.WriteLine(DescribeItem(a.armor));
            Console.WriteLine();
            Console.Write("  スキル: ");
            var skills = a.Skills;
            Console.WriteLine(skills.Count == 0 ? "なし" : string.Join(", ", skills.Select(x => x.skillName)));
            Console.WriteLine();
            ShowProfile(a);
            Console.WriteLine();
            Console.WriteLine($"  遠征記録: {a.expeditionCount}回"
                + $"（成功 {a.successfulExpeditionCount} / 撤退 {a.retreatCount}）");
            if (a.adventureHistory.Count == 0)
                ConsoleHelper.Dim("    まだ遠征記録はない");
            else
                foreach (var history in a.adventureHistory.TakeLast(5))
                    ConsoleHelper.Dim($"    ・{history}");
            Console.WriteLine();

            bool busy = questManager?.IsAdventurerBusy(a.id) == true;
            if (!a.isAlive)
                ConsoleHelper.Dim("  （死亡者は装備を変更できません）");
            else if (busy)
                ConsoleHelper.Dim("  （出発中は装備を変更できません。帰還後に変更してください）");
            else
                Console.WriteLine("  e. 装備を変更する");
            Console.WriteLine("  0. 戻る");
            Console.Write("選択: ");
            var input = Console.ReadLine()?.Trim().ToLower();
            if (input == "e" && a.isAlive && !busy) { ManageEquipment(a, guild); continue; }
            return;
        }
    }

    static void ShowProfile(AdventurerData a)
    {
        var m = a.master;
        Console.WriteLine("  人物記録:");
        if (!string.IsNullOrWhiteSpace(m.selfIntroduction))
            Console.WriteLine($"    「{m.selfIntroduction}」");
        ShowProfileLine("経歴", m.background);
        ShowProfileLine("性格", m.personality);
        ShowProfileLine("動機", m.motivation);
        ShowProfileLine("得意", m.specialty);
        ShowProfileLine("苦手・恐怖", m.fear);
        ShowProfileLine("信条", m.creed);
        if (string.IsNullOrWhiteSpace(m.background)
            && string.IsNullOrWhiteSpace(m.personality)
            && string.IsNullOrWhiteSpace(m.selfIntroduction))
            ConsoleHelper.Dim("    詳しい人物記録はまだない");
    }

    static void ShowProfileLine(string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            Console.WriteLine($"    {label}: {value}");
    }

    static string DescribeItem(EquipmentMasterData? item)
    {
        if (item == null) return "";
        var parts = BonusParts(item.bonus);
        return parts.Count == 0 ? "" : $"（{string.Join(" ", parts)}）";
    }

    static void ManageEquipment(AdventurerData a, GuildManager guild)
    {
        while (true)
        {
            ConsoleHelper.Header($"装備変更: {a.name}");
            Console.WriteLine($"  現在の武器: {a.weapon?.displayName ?? "なし"}");
            Console.WriteLine($"  現在の防具: {a.armor?.displayName ?? "なし"}");
            Console.WriteLine();
            Console.WriteLine("  1. 武器を変更");
            Console.WriteLine("  2. 防具を変更");
            Console.WriteLine("  0. 戻る");
            Console.Write("選択: ");
            var line = Console.ReadLine()?.Trim();
            if (line == "1") ChooseAndEquip(a, guild, EquipmentType.Weapon);
            else if (line == "2") ChooseAndEquip(a, guild, EquipmentType.Armor);
            else return;
        }
    }

    static void ChooseAndEquip(AdventurerData a, GuildManager guild, EquipmentType type)
    {
        string label = type == EquipmentType.Weapon ? "武器" : "防具";
        var current = type == EquipmentType.Weapon ? a.weapon : a.armor;

        // 在庫から該当タイプの装備を集計（同一品はまとめて表示）。
        var stock = guild.GetInventoryView()
            .Where(st => st.item.type == type && st.count > 0)
            .Select(st => st.item)
            .ToList();

        ConsoleHelper.Header($"{label}を選ぶ: {a.name}");
        Console.WriteLine($"  現在の{label}: {current?.displayName ?? "なし"}{DescribeItem(current)}");
        Console.WriteLine();

        if (stock.Count == 0 && current == null)
        {
            ConsoleHelper.Dim($"  ギルド倉庫に装備できる{label}がありません（商店で購入するか、クエスト報酬で入手できます）");
            ConsoleHelper.PressAnyKey();
            return;
        }

        var beforeStats = a.GetFinalCombatStats();

        int idx = 1;
        var pickable = new List<EquipmentMasterData?>();
        if (current != null)
        {
            Console.WriteLine($"  {idx}. 外して倉庫へ戻す");
            pickable.Add(null); // null = 外す
            idx++;
        }
        foreach (var item in stock)
        {
            int owned = guild.GetCount(item);
            string equipped = item == current ? " [装備中]" : "";
            Console.Write($"  {idx}. ");
            ConsoleHelper.WriteRarityName(item.displayName, item.rarity);
            Console.WriteLine($"  x{owned}{equipped}");
            ConsoleHelper.Dim($"       {DescribeEquipDetail(item)}");
            pickable.Add(item);
            idx++;
        }
        Console.WriteLine("  0. やめる");
        Console.Write($"選択 [0-{pickable.Count}]: ");
        if (!int.TryParse(Console.ReadLine(), out int sel) || sel <= 0 || sel > pickable.Count) return;

        var chosen = pickable[sel - 1];
        if (chosen == null)
        {
            if (type == EquipmentType.Weapon) EquipService.UnequipWeapon(a, guild);
            else EquipService.UnequipArmor(a, guild);
            ConsoleHelper.Info($"{label}を外しました");
        }
        else if (chosen == current)
        {
            ConsoleHelper.Dim("すでに装備しています");
            ConsoleHelper.PressAnyKey();
            return;
        }
        else if (EquipService.TryEquip(a, chosen, guild, out var reason))
        {
            ConsoleHelper.Info($"{chosen.displayName} を装備しました");
        }
        else
        {
            ConsoleHelper.Error($"装備できません: {reason}");
            ConsoleHelper.PressAnyKey();
            return;
        }

        // 装備前後のステータス差分を表示して、選択の良し悪しをその場で確認できるようにする。
        var afterStats = a.GetFinalCombatStats();
        Console.WriteLine();
        Console.WriteLine("  ステータス変化:");
        ShowStatDelta("物理攻撃", beforeStats.pAtk, afterStats.pAtk);
        ShowStatDelta("物理防御", beforeStats.pDef, afterStats.pDef);
        ShowStatDelta("魔法攻撃", beforeStats.mAtk, afterStats.mAtk);
        ShowStatDelta("魔法防御", beforeStats.mDef, afterStats.mDef);
        ShowStatDelta("命中", beforeStats.hit, afterStats.hit);
        ShowStatDelta("回避", beforeStats.evade, afterStats.evade);
        ShowStatDelta("回復力", beforeStats.heal, afterStats.heal);
        ConsoleHelper.PressAnyKey();
    }

    static void ShowStatDelta(string name, int before, int after)
    {
        if (before == after) return;
        int d = after - before;
        string arrow = d > 0 ? $"▲+{d}" : $"▼{d}";
        var color = d > 0 ? ConsoleColor.Green : ConsoleColor.Red;
        Console.ForegroundColor = color;
        Console.WriteLine($"    {name,-5}: {before} → {after}  {arrow}");
        Console.ResetColor();
    }

    static string DescribeEquipDetail(EquipmentMasterData item)
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
