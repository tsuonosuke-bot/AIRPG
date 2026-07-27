using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Game.Data;
using GuildSimulator.Game.Presentation;

namespace GuildSimulator.Game.Screens;

public static class AdventurerScreen
{
    const int ClassChangeCostPerLevel = 10;

    public static int CalculateClassChangeCost(int level) => Math.Max(1, level) * ClassChangeCostPerLevel;

    public static async Task ShowAsync(GameMasterData db, GuildManager guild, QuestManager? questManager = null, int currentTurn = 0)
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
            await ShowDetailAsync(db, advs[sel.Value - 1], guild, questManager, currentTurn);
        }
    }

    static async Task ShowDetailAsync(GameMasterData db, AdventurerData a, GuildManager guild, QuestManager? questManager, int currentTurn)
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
            foreach (var slot in EquipService.AllSlots)
            {
                var item = a.GetEquipped(slot);
                Ui.Write($"  {EquipService.SlotDisplayName(slot)}: ");
                if (item != null) Ui.WriteRarityName(item.displayName, item.rarity); else Ui.Write("なし");
                Ui.WriteLine(DescribeItem(item));
            }
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
                Ui.Dim("  （死亡者は装備・クラスを変更できません）");
            else if (busy)
                Ui.Dim("  （出発中は装備・クラスを変更できません。帰還後に変更してください）");
            else
            {
                options.Add(new MenuOption("e", "装備を変更する"));
                int ccCost = CalculateClassChangeCost(a.level);
                options.Add(new MenuOption("c", $"クラスチェンジ（{ccCost}G）"));
            }
            if (!a.isAlive && !busy)
            {
                int burialCost = GuildManager.CalculateBurialCost(a.level);
                options.Add(new MenuOption("d", $"埋葬する（{burialCost}G）"));
            }
            options.Add(new MenuOption("0", "戻る", Style: TextStyle.Dim));

            string input = await Ui.SelectAsync("選択", options);
            if (input == "e" && a.isAlive && !busy) { await ManageEquipmentAsync(a, guild); continue; }
            if (input == "c" && a.isAlive && !busy) { await ChangeClassAsync(a, guild, db); continue; }
            if (input == "d" && !a.isAlive && !busy)
            {
                if (await BuryAdventurerAsync(a, guild, currentTurn)) return;
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

    static async Task<bool> BuryAdventurerAsync(AdventurerData a, GuildManager guild, int currentTurn)
    {
        int cost = GuildManager.CalculateBurialCost(a.level);
        Ui.WriteLine($"  {a.name} を埋葬します。装備は倉庫に戻ります。");
        Ui.WriteLine($"  埋葬費: {cost}G（所持: {guild.Gold}G）");
        if (!await Ui.ConfirmAsync("埋葬しますか？")) return false;

        EquipService.UnequipAll(a, guild);
        if (!guild.TryBuryAdventurer(a, currentTurn, out var reason))
        {
            Ui.Error(reason);
            await Ui.PauseAsync();
            return false;
        }
        Ui.Info($"{a.name} を埋葬しました。安らかに眠れ。");
        await Ui.PauseAsync();
        return true;
    }

    static async Task ChangeClassAsync(AdventurerData a, GuildManager guild, GameMasterData db)
    {
        Ui.BeginScreen();
        Ui.Header($"クラスチェンジ: {a.name}");
        Ui.WriteLine($"  現在のクラス: {a.currentClass?.className ?? "なし"}");
        Ui.WriteLine($"  種族: {a.race?.raceName ?? "不明"}");

        var allowed = a.race != null && a.race.allowedClassIds.Count > 0
            ? a.race.allowedClassIds : null;
        var candidates = db.classes.Values
            .Where(c => c != a.currentClass)
            .Where(c => allowed == null || allowed.Contains(c.id))
            .ToList();

        if (candidates.Count == 0)
        {
            Ui.Dim("  変更できるクラスがありません");
            await Ui.PauseAsync();
            return;
        }

        int cost = CalculateClassChangeCost(a.level);
        Ui.WriteLine($"  費用: {cost}G（所持: {guild.Gold}G）");
        Ui.WriteLine();

        if (guild.Gold < cost)
        {
            Ui.Error($"  ゴールドが不足しています（必要: {cost}G）");
            await Ui.PauseAsync();
            return;
        }

        var options = new List<MenuOption>();
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            string growths = $"VIT:{c.vitGrowth:+0.00;-0.00} STR:{c.strGrowth:+0.00;-0.00} INT:{c.intGrowth:+0.00;-0.00} AGI:{c.agiGrowth:+0.00;-0.00} MEN:{c.mentGrowth:+0.00;-0.00}";
            string skillNames = string.Join(", ", c.classSkills
                .Where(e => e.Skill != null)
                .Select(e => $"{e.Skill!.skillName}({e.requiredClearCount})"));
            Ui.WriteLine($"  {i + 1}. {c.className}");
            Ui.Dim($"       成長率: {growths}");
            if (skillNames.Length > 0)
                Ui.Dim($"       スキル: {skillNames}");
            options.Add(new MenuOption(
                (i + 1).ToString(),
                c.className,
                growths));
        }

        int? sel = await Ui.SelectIndexAsync("クラスを選択", options, "やめる");
        if (sel == null) return;

        var chosen = candidates[sel.Value - 1];
        Ui.WriteLine();
        Ui.WriteLine($"  {a.currentClass?.className ?? "なし"} → {chosen.className} に変更します（{cost}G）");
        if (!await Ui.ConfirmAsync("よろしいですか？")) return;

        guild.SpendGold(cost, $"クラスチェンジ: {a.name}（{a.currentClass?.className ?? "?"} → {chosen.className}）");
        a.ChangeClass(chosen);
        Ui.Info($"{a.name} のクラスを {chosen.className} に変更しました");
        await Ui.PauseAsync();
    }

    static async Task ManageEquipmentAsync(AdventurerData a, GuildManager guild)
    {
        while (true)
        {
            Ui.BeginScreen();
            Ui.Header($"装備変更: {a.name}");
            foreach (var slot in EquipService.AllSlots)
            {
                var item = a.GetEquipped(slot);
                Ui.WriteLine($"  {EquipService.SlotDisplayName(slot)}: {item?.displayName ?? "なし"}");
            }
            Ui.WriteLine();

            var slotOptions = new List<MenuOption>();
            int idx = 1;
            foreach (var slot in EquipService.AllSlots)
            {
                slotOptions.Add(new MenuOption(idx.ToString(), $"{EquipService.SlotDisplayName(slot)}を変更"));
                idx++;
            }
            slotOptions.Add(new MenuOption("0", "戻る", Style: TextStyle.Dim));

            string line = await Ui.SelectAsync("スロットを選択", slotOptions);
            if (line == "0") return;
            if (int.TryParse(line, out int slotIdx) && slotIdx >= 1 && slotIdx <= EquipService.AllSlots.Count)
                await ChooseAndEquipSlotAsync(a, guild, EquipService.AllSlots[slotIdx - 1]);
        }
    }

    static async Task ChooseAndEquipSlotAsync(AdventurerData a, GuildManager guild, EquipSlot slot)
    {
        string label = EquipService.SlotDisplayName(slot);
        var current = a.GetEquipped(slot);

        var stock = guild.GetInventoryView()
            .Where(st => st.item.CanEquipTo(slot) && st.count > 0)
            .Select(st => st.item)
            .ToList();

        Ui.BeginScreen();
        Ui.Header($"{label}を選ぶ: {a.name}");
        Ui.WriteLine($"  現在の{label}: {current?.displayName ?? "なし"}{DescribeItem(current)}");
        Ui.WriteLine();

        if (stock.Count == 0 && current == null)
        {
            Ui.Dim($"  ギルド倉庫に装備できる{label}用装備がありません（商店で購入するか、クエスト報酬で入手できます）");
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
            pickable.Add(null);
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
            EquipService.Unequip(a, slot, guild);
            Ui.Info($"{label}を外しました");
        }
        else if (chosen == current)
        {
            Ui.Dim("すでに装備しています");
            await Ui.PauseAsync();
            return;
        }
        else if (EquipService.TryEquip(a, chosen, slot, guild, out var reason))
        {
            Ui.Info($"{chosen.displayName} を{label}に装備しました");
        }
        else
        {
            Ui.Error($"装備できません: {reason}");
            await Ui.PauseAsync();
            return;
        }

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
