using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Game.Presentation;

namespace GuildSimulator.Game.Screens;

public static class AdventurerScreen
{
    public static async Task ShowAsync(GuildManager guild, QuestManager? questManager = null)
    {
        while (true)
        {
            Ui.BeginScreen();
            Ui.Header("冒険者一覧");
            var advs = guild.adventurers;
            var entries = new List<MenuOption>();
            for (int i = 0; i < advs.Count; i++)
            {
                var a = advs[i];
                string busy = questManager?.IsAdventurerBusy(a.id) == true ? "[出発中]" : "";
                Ui.Write($"  {i + 1}. ");
                Ui.WriteRarityName(a.name, a.master.rarity);
                Ui.Write($" Lv{a.level} Rank{a.rank} {a.ClassAndRace} {busy}");
                if (!a.isAlive) Ui.Write("[死亡]", TextStyle.Error);
                Ui.WriteLine();

                entries.Add(new MenuOption(
                    (i + 1).ToString(),
                    $"{a.name} Lv{a.level}{(a.isAlive ? "" : "[死亡]")}",
                    $"Rank{a.rank} {a.ClassAndRace} {busy}",
                    a.isAlive ? Ui.RarityStyle(a.master.rarity) : TextStyle.Error));
            }

            int? sel = await Ui.SelectIndexAsync("冒険者を選択", entries);
            if (sel == null) return;
            await ShowDetailAsync(advs[sel.Value - 1], guild, questManager);
        }
    }

    static async Task ShowDetailAsync(AdventurerData a, GuildManager guild, QuestManager? questManager)
    {
        while (true)
        {
            Ui.BeginScreen();
            Ui.Header($"冒険者詳細: {a.name}");
            Ui.WriteLine($"  クラス/種族 : {a.ClassAndRace}");
            Ui.WriteLine($"  レベル      : {a.level}  (経験値 {a.experience}/{a.RequiredExpForNextLevel})");
            Ui.WriteLine($"  冒険者ランク: {a.rank}  (RP {a.rankPoint}/{a.RequiredRankPointForNextRank})");
            Ui.WriteLine($"  維持費      : {GuildManager.CalculateAdventurerUpkeep(a.level)}G/T（Lv×{GuildManager.UpkeepGoldPerLevel}G）");
            Ui.WriteLine($"  状態        : {(a.isAlive ? "生存" : "死亡")}");
            Ui.WriteLine();
            Ui.WriteLine($"  VIT:{a.vitality} MEN:{a.mental} STR:{a.strength} AGI:{a.agility} INT:{a.intelligence} CON:{a.constitution}");
            var s = a.GetFinalCombatStats();
            int hpMax = a.CombatHpMax > 0 ? a.CombatHpMax : s.hp;
            int hpCur = a.CombatHpMax > 0 ? a.CombatHp : s.hp;
            Ui.WriteLine($"  HP:{hpCur}/{hpMax}  物理攻撃:{s.pAtk} 物理防御:{s.pDef} 魔法攻撃:{s.mAtk} 魔法防御:{s.mDef}");
            Ui.WriteLine($"  命中:{s.hit} 回避:{s.evade} 回復力:{s.heal}");
            Ui.WriteLine();
            Ui.Write("  武器: ");
            if (a.weapon != null) Ui.WriteRarityName(a.weapon.displayName, a.weapon.rarity); else Ui.Write("なし");
            Ui.WriteLine(DescribeItem(a.weapon));
            Ui.Write("  防具: ");
            if (a.armor != null) Ui.WriteRarityName(a.armor.displayName, a.armor.rarity); else Ui.Write("なし");
            Ui.WriteLine(DescribeItem(a.armor));
            Ui.WriteLine();
            Ui.Write("  スキル: ");
            var skills = a.Skills;
            Ui.WriteLine(skills.Count == 0 ? "なし" : string.Join(", ", skills.Select(x => x.skillName)));
            Ui.WriteLine();
            ShowProfile(a);
            Ui.WriteLine();
            Ui.WriteLine($"  遠征記録: {a.expeditionCount}回"
                + $"（成功 {a.successfulExpeditionCount} / 撤退 {a.retreatCount}）");
            if (a.adventureHistory.Count == 0)
                Ui.Dim("    まだ遠征記録はない");
            else
                foreach (var history in a.adventureHistory.TakeLast(5))
                    Ui.Dim($"    ・{history}");
            Ui.WriteLine();

            bool busy = questManager?.IsAdventurerBusy(a.id) == true;
            var options = new List<MenuOption>();
            if (!a.isAlive)
                Ui.Dim("  （死亡者は装備を変更できません）");
            else if (busy)
                Ui.Dim("  （出発中は装備を変更できません。帰還後に変更してください）");
            else
                options.Add(new MenuOption("e", "装備を変更する"));
            if (!busy)
                options.Add(new MenuOption("d", "ギルドから除名する", Style: TextStyle.Error));
            options.Add(new MenuOption("0", "戻る", Style: TextStyle.Dim));

            string input = await Ui.SelectAsync("選択", options);
            if (input == "e" && a.isAlive && !busy) { await ManageEquipmentAsync(a, guild); continue; }
            if (input == "d" && !busy)
            {
                if (await DismissAdventurerAsync(a, guild)) return;
                continue;
            }
            return;
        }
    }

    static void ShowProfile(AdventurerData a)
    {
        var m = a.master;
        Ui.WriteLine("  人物記録:");
        if (!string.IsNullOrWhiteSpace(m.selfIntroduction))
            Ui.WriteLine($"    「{m.selfIntroduction}」");
        ShowProfileLine("経歴", m.background);
        ShowProfileLine("性格", m.personality);
        ShowProfileLine("動機", m.motivation);
        ShowProfileLine("得意", m.specialty);
        ShowProfileLine("苦手・恐怖", m.fear);
        ShowProfileLine("信条", m.creed);
        if (string.IsNullOrWhiteSpace(m.background)
            && string.IsNullOrWhiteSpace(m.personality)
            && string.IsNullOrWhiteSpace(m.selfIntroduction))
            Ui.Dim("    詳しい人物記録はまだない");
    }

    static void ShowProfileLine(string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            Ui.WriteLine($"    {label}: {value}");
    }

    static string DescribeItem(EquipmentMasterData? item)
    {
        if (item == null) return "";
        var parts = BonusParts(item.bonus);
        return parts.Count == 0 ? "" : $"（{string.Join(" ", parts)}）";
    }

    static async Task<bool> DismissAdventurerAsync(AdventurerData a, GuildManager guild)
    {
        string warning = a.isAlive
            ? $"{a.name}（Lv{a.level}）をギルドから除名します。装備は倉庫に戻りますが、この冒険者は二度と雇えません。"
            : $"{a.name}の記録をギルドから抹消します。装備は倉庫に戻ります。";
        if (!await Ui.ConfirmAsync(warning)) return false;

        if (a.weapon != null) { guild.AddEquipment(a.weapon, 1); a.weapon = null; }
        if (a.armor != null) { guild.AddEquipment(a.armor, 1); a.armor = null; }
        guild.RemoveAdventurer(a);
        Ui.Info($"{a.name} をギルドから除名しました");
        await Ui.PauseAsync();
        return true;
    }

    static async Task ManageEquipmentAsync(AdventurerData a, GuildManager guild)
    {
        while (true)
        {
            Ui.BeginScreen();
            Ui.Header($"装備変更: {a.name}");
            Ui.WriteLine($"  現在の武器: {a.weapon?.displayName ?? "なし"}");
            Ui.WriteLine($"  現在の防具: {a.armor?.displayName ?? "なし"}");
            Ui.WriteLine();

            string line = await Ui.SelectAsync("選択", new[]
            {
                new MenuOption("1", "武器を変更"),
                new MenuOption("2", "防具を変更"),
                new MenuOption("0", "戻る", Style: TextStyle.Dim),
            });
            if (line == "1") await ChooseAndEquipAsync(a, guild, EquipmentType.Weapon);
            else if (line == "2") await ChooseAndEquipAsync(a, guild, EquipmentType.Armor);
            else return;
        }
    }

    static async Task ChooseAndEquipAsync(AdventurerData a, GuildManager guild, EquipmentType type)
    {
        string label = type == EquipmentType.Weapon ? "武器" : "防具";
        var current = type == EquipmentType.Weapon ? a.weapon : a.armor;

        // 在庫から該当タイプの装備を集計（同一品はまとめて表示）。
        var stock = guild.GetInventoryView()
            .Where(st => st.item.type == type && st.count > 0)
            .Select(st => st.item)
            .ToList();

        Ui.BeginScreen();
        Ui.Header($"{label}を選ぶ: {a.name}");
        Ui.WriteLine($"  現在の{label}: {current?.displayName ?? "なし"}{DescribeItem(current)}");
        Ui.WriteLine();

        if (stock.Count == 0 && current == null)
        {
            Ui.Dim($"  ギルド倉庫に装備できる{label}がありません（商店で購入するか、クエスト報酬で入手できます）");
            await Ui.PauseAsync();
            return;
        }

        var beforeStats = a.GetFinalCombatStats();

        int idx = 1;
        var pickable = new List<EquipmentMasterData?>();
        var options = new List<MenuOption>();
        if (current != null)
        {
            Ui.WriteLine($"  {idx}. 外して倉庫へ戻す");
            options.Add(new MenuOption(idx.ToString(), "外して倉庫へ戻す"));
            pickable.Add(null); // null = 外す
            idx++;
        }
        foreach (var item in stock)
        {
            int owned = guild.GetCount(item);
            string equipped = item == current ? " [装備中]" : "";
            Ui.Write($"  {idx}. ");
            Ui.WriteRarityName(item.displayName, item.rarity);
            Ui.WriteLine($"  x{owned}{equipped}");
            Ui.Dim($"       {DescribeEquipDetail(item)}");
            options.Add(new MenuOption(
                idx.ToString(),
                $"{item.displayName}  x{owned}{equipped}",
                DescribeEquipDetail(item),
                Ui.RarityStyle(item.rarity)));
            pickable.Add(item);
            idx++;
        }

        int? sel = await Ui.SelectIndexAsync($"{label}を選択", options, "やめる");
        if (sel == null) return;

        var chosen = pickable[sel.Value - 1];
        if (chosen == null)
        {
            if (type == EquipmentType.Weapon) EquipService.UnequipWeapon(a, guild);
            else EquipService.UnequipArmor(a, guild);
            Ui.Info($"{label}を外しました");
        }
        else if (chosen == current)
        {
            Ui.Dim("すでに装備しています");
            await Ui.PauseAsync();
            return;
        }
        else if (EquipService.TryEquip(a, chosen, guild, out var reason))
        {
            Ui.Info($"{chosen.displayName} を装備しました");
        }
        else
        {
            Ui.Error($"装備できません: {reason}");
            await Ui.PauseAsync();
            return;
        }

        // 装備前後のステータス差分を表示して、選択の良し悪しをその場で確認できるようにする。
        var afterStats = a.GetFinalCombatStats();
        Ui.WriteLine();
        Ui.WriteLine("  ステータス変化:");
        ShowStatDelta("物理攻撃", beforeStats.pAtk, afterStats.pAtk);
        ShowStatDelta("物理防御", beforeStats.pDef, afterStats.pDef);
        ShowStatDelta("魔法攻撃", beforeStats.mAtk, afterStats.mAtk);
        ShowStatDelta("魔法防御", beforeStats.mDef, afterStats.mDef);
        ShowStatDelta("命中", beforeStats.hit, afterStats.hit);
        ShowStatDelta("回避", beforeStats.evade, afterStats.evade);
        ShowStatDelta("回復力", beforeStats.heal, afterStats.heal);
        await Ui.PauseAsync();
    }

    static void ShowStatDelta(string name, int before, int after)
    {
        if (before == after) return;
        int d = after - before;
        string arrow = d > 0 ? $"▲+{d}" : $"▼{d}";
        Ui.WriteLine($"    {name,-5}: {before} → {after}  {arrow}", d > 0 ? TextStyle.Info : TextStyle.Error);
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
