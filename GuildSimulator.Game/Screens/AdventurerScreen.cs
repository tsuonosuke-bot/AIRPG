using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Game.Data;
using GuildSimulator.Game.Presentation;

namespace GuildSimulator.Game.Screens;

public static class AdventurerScreen
{
    public const int ClassChangeCostPerLevel = 10;

    /// <summary>クラスチェンジの解禁に必要な施設。建設するまでは職業を選び直せない。</summary>
    public const string ClassChangeFacilityId = "fac_training_hall_01";

    public static int CalculateClassChangeCost(int level) => Math.Max(1, level) * ClassChangeCostPerLevel;

    public static bool IsClassChangeUnlocked(GuildManager guild) =>
        guild.facilities.Any(f => f.id == ClassChangeFacilityId);

    static string ClassChangeFacilityName(GameMasterData db) =>
        db.facilities.TryGetValue(ClassChangeFacilityId, out var f) ? f.displayName : ClassChangeFacilityId;

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
                string condition = a.isAlive && a.IsInjured ? $"[負傷{a.injuries.Count}]" : "";
                Ui.Write($"  {i + 1}. ");
                Ui.WriteRarityName(a.name, a.master.rarity);
                Ui.Write($" Lv{a.level} ランク{a.RankLabel} {a.ClassAndRace} {busy}{condition}");
                if (!a.isAlive) Ui.Write("[死亡]", TextStyle.Error);
                Ui.WriteLine();

                entries.Add(new MenuOption(
                    (i + 1).ToString(),
                    $"{a.name} Lv{a.level}{(a.isAlive ? "" : "[死亡]")}",
                    $"ランク{a.RankLabel} {a.ClassAndRace} {busy}{condition}",
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
            string rankProgress = a.IsMaxRank
                ? "最高ランク"
                : $"格上クリア {a.higherRankClears}/{a.RequiredClearsForNextRank} → {Rank.Label(a.rank + 1)}";
            Ui.WriteLine($"  冒険者ランク: {a.RankLabel}  ({rankProgress})");
            Ui.WriteLine($"  維持費      : {GuildManager.CalculateAdventurerUpkeep(a.level)}G/T（Lv×{GuildManager.UpkeepGoldPerLevel}G）");
            Ui.WriteLine($"  状態        : {a.ConditionSummary}");
            if (a.ConditionTitle != null)
                Ui.Info($"  称号        : {a.ConditionTitle}");
            Ui.WriteLine();
            Ui.WriteLine($"  VIT:{a.vitality} MEN:{a.mental} STR:{a.strength} AGI:{a.agility} INT:{a.intelligence} SIZ:{a.constitution}");
            var s = a.GetFinalCombatStats();
            int hpMax = a.CombatHpMax > 0 ? a.CombatHpMax : s.hp;
            int hpCur = a.CombatHpMax > 0 ? a.CombatHp : s.hp;
            Ui.WriteLine($"  HP:{hpCur}/{hpMax}  装甲AV:{Math.Max(0, s.av)} 魔法装甲mAV:{Math.Max(0, s.mav)} 回避DV:{s.dv}");
            int shownPv = QudCombat.EffectivePv(a.WeaponBasePv, a.AttackStatModifier, a.MaxStatBonus,
                a.IsMagicAttack ? s.mpv : s.pv);
            Ui.WriteLine($"  貫通{(a.IsMagicAttack ? "mPV" : "PV")}:{shownPv} ダメージ:{(string.IsNullOrWhiteSpace(a.DamageDice) ? QudCombat.DEFAULT_DAMAGE_DICE : a.DamageDice)}/貫通  命中:{s.toHit:+#;-#;+0} 回復力:{s.heal}");
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
            ShowClassMastery(a);
            ShowConditions(a);
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
                bool classChangeUnlocked = IsClassChangeUnlocked(guild);
                if (classChangeUnlocked)
                {
                    int ccCost = CalculateClassChangeCost(a.level);
                    options.Add(new MenuOption("c", $"クラスチェンジ（{ccCost}G）"));
                }
                else
                {
                    Ui.Dim($"  （クラスチェンジは「{ClassChangeFacilityName(db)}」の建設で解禁されます）");
                }
            }
            if (!a.isAlive && !busy)
            {
                int burialCost = GuildManager.CalculateBurialCost(a.level);
                options.Add(new MenuOption("d", $"埋葬する（{burialCost}G）"));
            }
            options.Add(new MenuOption("0", "戻る", Style: TextStyle.Dim));

            string input = await Ui.SelectAsync("選択", options);
            if (input == "e" && a.isAlive && !busy) { await ManageEquipmentAsync(a, guild); continue; }
            if (input == "c" && a.isAlive && !busy && IsClassChangeUnlocked(guild)) { await ChangeClassAsync(a, guild, db); continue; }
            if (input == "d" && !a.isAlive && !busy)
            {
                if (await BuryAdventurerAsync(a, guild, currentTurn)) return;
                continue;
            }
            return;
        }
    }

    /// <summary>
    /// 職業スキルの解禁は「その職業での正規クリア回数」で決まる。今どこまで進んでいて、
    /// 次に何があと何回で開くのかを出しておかないと、プレイヤーからは進捗が見えない。
    /// </summary>
    static void ShowClassMastery(AdventurerData a)
    {
        if (a.currentClass == null) return;
        int clears = a.CurrentClassClearCount;
        Ui.WriteLine($"  クラス習熟度: {clears}"
            + $"（{a.currentClass.className}で適正ランク{a.SuitableRankRangeLabel}のクエストを正規クリアした回数）");

        var next = a.currentClass.classSkills
            .Where(e => e.Skill != null && e.requiredClearCount > clears)
            .OrderBy(e => e.requiredClearCount)
            .FirstOrDefault();
        if (next != null)
            Ui.Dim($"    次のスキル: {next.Skill!.skillName}（あと{next.requiredClearCount - clears}回）");
        else
            Ui.Dim("    この職業のスキルはすべて習得済み");
    }

    static void ShowProfile(AdventurerData a)
    {
        var m = a.master;
        Ui.WriteLine("  人物記録:");
        if (m.gender != Gender.Unspecified)
            Ui.WriteLine($"    性別: {GenderLabel(m.gender)}");
        ShowProfileLine("経歴", m.background);
        if (string.IsNullOrWhiteSpace(m.background))
            Ui.Dim("    詳しい人物記録はまだない");
    }

    static void ShowProfileLine(string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            Ui.WriteLine($"    {label}: {value}");
    }

    static string GenderLabel(Gender gender) => gender switch
    {
        Gender.Male => "男性",
        Gender.Female => "女性",
        _ => "不明",
    };

    static string DescribeItem(EquipmentMasterData? item)
    {
        if (item == null) return "";
        var parts = EquipmentText.TraitParts(item.Traits);
        parts.AddRange(EquipmentText.BonusParts(item.bonus));
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
        if (!IsClassChangeUnlocked(guild))
        {
            Ui.Error($"クラスチェンジには「{ClassChangeFacilityName(db)}」の建設が必要です");
            await Ui.PauseAsync();
            return;
        }

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
        var afterEquipment = a.GetEquipped(slot);
        Ui.WriteLine();
        Ui.WriteLine("  ステータス・装備変化:");
        bool hasChange = false;
        hasChange |= ShowStatDelta("HP", beforeStats.hp, afterStats.hp);
        hasChange |= ShowStatDelta("士気", beforeStats.san, afterStats.san);
        hasChange |= ShowStatDelta("装甲AV", beforeStats.av, afterStats.av);
        hasChange |= ShowStatDelta("魔装甲mAV", beforeStats.mav, afterStats.mav);
        hasChange |= ShowStatDelta("貫通PV", beforeStats.pv, afterStats.pv);
        hasChange |= ShowStatDelta("魔貫通mPV", beforeStats.mpv, afterStats.mpv);
        hasChange |= ShowStatDelta("回避DV", beforeStats.dv, afterStats.dv);
        hasChange |= ShowStatDelta("命中", beforeStats.toHit, afterStats.toHit);
        hasChange |= ShowStatDelta("回復力", beforeStats.heal, afterStats.heal);

        string beforeEffect = DescribeEquippedEffect(current);
        string afterEffect = DescribeEquippedEffect(afterEquipment);
        if (!string.Equals(beforeEffect, afterEffect, StringComparison.Ordinal))
        {
            Ui.WriteLine($"    装備効果: {beforeEffect} → {afterEffect}");
            hasChange = true;
        }
        if (!hasChange)
            Ui.Dim("    数値と装備効果に変化はありません");
        await Ui.PauseAsync();
    }

    static void ShowConditions(AdventurerData adventurer)
    {
        if (adventurer.injuries.Count == 0 && adventurer.scars.Count == 0) return;
        Ui.WriteLine();
        if (adventurer.injuries.Count > 0)
        {
            Ui.WriteLine("  負傷（負傷中でも出発可能。出発させずターンを進めると休養）:");
            foreach (var injury in adventurer.injuries)
                Ui.Warn($"    ・{injury.DisplayName}: {injury.EffectDescription} / 休養あと{injury.remainingRestTurns}T");
        }
        if (adventurer.scars.Count > 0)
        {
            Ui.WriteLine("  傷痕・後遺症:");
            foreach (var scar in adventurer.scars)
                Ui.Dim($"    ・{scar.DisplayName}: {scar.EffectDescription} / 称号「{scar.Title}」");
        }
    }

    static bool ShowStatDelta(string name, int before, int after)
    {
        if (before == after) return false;
        int d = after - before;
        string arrow = d > 0 ? $"▲+{d}" : $"▼{d}";
        Ui.WriteLine($"    {name,-5}: {before} → {after}  {arrow}", d > 0 ? TextStyle.Info : TextStyle.Error);
        return true;
    }

    static string DescribeEquippedEffect(EquipmentMasterData? item) =>
        item == null ? "なし" : DescribeEquipDetail(item);

    static string DescribeEquipDetail(EquipmentMasterData item)
    {
        var parts = EquipmentText.WeaponParts(item);
        parts.AddRange(EquipmentText.BonusParts(item.bonus));
        parts.Add($"重量{item.weight}");
        return string.Join(" ", parts);
    }
}
