using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems;
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
    public const string ClassChangeFacilityId = "fac_class_change_01";

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
                Ui.Write($" Lv{a.level}/{a.LevelCap} ランク{a.RankLabel} {a.ClassAndRace} {busy}{condition}");
                if (!a.isAlive) Ui.Write("[死亡]", TextStyle.Error);
                Ui.WriteLine();

                entries.Add(new MenuOption(
                    (i + 1).ToString(),
                    $"{a.name} Lv{a.level}/{a.LevelCap}{(a.isAlive ? "" : "[死亡]")}",
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
            string levelProgress;
            if (a.level > a.LevelCap)
                levelProgress = a.IsMaxRank
                    ? $"{a.RankLabel}ランク上限Lv{a.LevelCap}を超過・成長停止"
                    : $"{a.RankLabel}ランク上限Lv{a.LevelCap}を超過・昇格まで成長停止";
            else if (a.IsAtLevelCap)
                levelProgress = a.IsMaxRank
                    ? $"{a.RankLabel}ランク上限"
                    : $"{a.RankLabel}ランク上限・昇格で上限Lv{Rank.LevelCap(a.rank + 1)}";
            else
                levelProgress = $"経験値 {a.experience}/{a.RequiredExpForNextLevel}";
            Ui.WriteLine($"  レベル      : {a.level}/{a.LevelCap}  ({levelProgress})");
            string rankProgress;
            if (a.IsMaxRank)
                rankProgress = "最高ランク";
            else if (a.CanRankUp)
                rankProgress = $"昇格可能 → {Rank.Label(a.rank + 1)}";
            else
            {
                var req = a.NextRankRequirement!.Value;
                rankProgress = $"格上クリア {a.higherRankClears}/{req.higherRankClears}"
                    + $"・累積適正 {a.suitableRankClearsTotal}/{req.suitableTotalClears}"
                    + $" → {Rank.Label(a.rank + 1)}";
            }
            Ui.WriteLine($"  冒険者ランク: {a.RankLabel}  ({rankProgress})");
            Ui.Dim($"    認定ランクは敵の脅威度と比較され、格上1段につき遭遇時の士気が最大{MoraleState.ThreatGapFlat}減る。昇格するとこの差を縮めやすい");
            Ui.WriteLine($"  維持費      : {GuildManager.CalculateAdventurerUpkeep(a.level, a.rank)}G/T"
                + $"（Lv×{GuildManager.UpkeepGoldPerLevel}G ＋ ランク昇格ぶん×{GuildManager.UpkeepGoldPerRank}G）");
            Ui.WriteLine($"  状態        : {a.ConditionSummary}");
            if (a.ConditionTitle != null)
                Ui.Info($"  称号        : {a.ConditionTitle}");
            Ui.WriteLine();
            Ui.WriteLine($"  VIT:{a.vitality} MEN:{a.mental} STR:{a.strength} AGI:{a.agility} INT:{a.intelligence} SIZ:{a.constitution} APP:{a.appearance}");
            var s = a.GetFinalCombatStats();
            int hpMax = a.CombatHpMax > 0 ? a.CombatHpMax : s.hp;
            int hpCur = a.CombatHpMax > 0 ? a.CombatHp : s.hp;
            Ui.WriteLine($"  HP:{hpCur}/{hpMax}  装甲AV:{Math.Max(0, s.av)} 魔法装甲mAV:{Math.Max(0, s.mav)} 回避DV:{s.dv}");
            int shownPv = QudCombat.EffectivePv(a.WeaponBasePv, a.AttackStatModifier, a.MaxStatBonus,
                a.IsMagicAttack ? s.mpv : s.pv);
            Ui.WriteLine($"  貫通{(a.IsMagicAttack ? "mPV" : "PV")}:{shownPv} ダメージ:{(string.IsNullOrWhiteSpace(a.DamageDice) ? QudCombat.DEFAULT_DAMAGE_DICE : a.DamageDice)}/貫通  命中:{s.toHit:+#;-#;+0} 回復力:{s.heal}");
            ShowCarryStatus(a, "  ");
            Ui.WriteLine();
            foreach (var slot in EquipService.AllSlots)
            {
                var item = a.GetEquipped(slot);
                Ui.Write($"  {EquipService.SlotDisplayName(slot)}: ");
                if (item != null) Ui.WriteRarityName(item.displayName, item.rarity); else Ui.Write("なし");
                Ui.WriteLine(DescribeItem(item));
            }
            Ui.WriteLine();
            ShowSkillSummary(a);
            ShowClassMastery(a);
            ShowConditions(a);
            ShowTraitsAndRecords(a, db);
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
            if (a.AllLearnedSkills.Any())
                options.Add(new MenuOption("s", "スキル詳細", "習得履歴と現在の発動条件を確認する"));
            if (!a.isAlive)
                Ui.Dim("  （死亡者は装備・クラスを変更できません）");
            else if (busy)
                Ui.Dim("  （出発中は装備・クラスを変更できません。帰還後に変更してください）");
            else
            {
                options.Add(new MenuOption("e", "装備を変更する"));
                if (a.CanRankUp)
                {
                    int promotedMasteryCap = Rank.MasteryCap(a.rank + 1);
                    int promotedMastery = Math.Min(
                        promotedMasteryCap, a.CurrentClassMastery + AdventurerData.RankUpMasteryGain);
                    options.Add(new MenuOption("r",
                        $"昇格させる（{Rank.Label(a.rank)}→{Rank.Label(a.rank + 1)}）",
                        $"レベル上限Lv{a.LevelCap}→Lv{Rank.LevelCap(a.rank + 1)}、"
                        + $"習熟度上限{a.MasteryCap}→{promotedMasteryCap}、"
                        + $"全能力値+{AdventurerData.RankUpStatGain}、"
                        + $"{a.currentClass?.className ?? "職業"}習熟度+{promotedMastery - a.CurrentClassMastery}。"));
                }
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
            if (a.isAlive && !busy)
            {
                int upkeep = GuildManager.CalculateAdventurerUpkeep(a.level, a.rank);
                options.Add(new MenuOption(
                    "x",
                    "解雇する",
                    $"在籍枠が1つ空き、維持費が{upkeep}G/T減ります。装備は倉庫に戻ります",
                    TextStyle.Warn));
            }
            if (!a.isAlive && !busy)
            {
                int burialCost = GuildManager.CalculateBurialCost(a.level);
                options.Add(new MenuOption("d", $"埋葬する（{burialCost}G）"));
            }
            options.Add(new MenuOption("0", "戻る", Style: TextStyle.Dim));

            string input = await Ui.SelectAsync("選択", options);
            if (input == "s") { await ShowSkillDetailsAsync(a); continue; }
            if (input == "e" && a.isAlive && !busy) { await ManageEquipmentAsync(a, guild); continue; }
            if (input == "r" && a.isAlive && !busy && a.CanRankUp) { await ConfirmRankUpAsync(a, guild); continue; }
            if (input == "c" && a.isAlive && !busy && IsClassChangeUnlocked(guild)) { await ChangeClassAsync(a, guild, db); continue; }
            if (input == "x" && a.isAlive && !busy)
            {
                if (await DismissAdventurerAsync(a, guild)) return;
                continue;
            }
            if (input == "d" && !a.isAlive && !busy)
            {
                if (await BuryAdventurerAsync(a, guild, currentTurn)) return;
                continue;
            }
            return;
        }
    }

    /// <summary>
    /// 職業スキルの解禁は「その職業での習熟度」で決まる。今どこまで進んでいて、
    /// 次に何があと何回で開くのか、ランク上限に阻まれているかを表示する。
    /// </summary>
    static void ShowClassMastery(AdventurerData a)
    {
        if (a.currentClass == null) return;
        int mastery = a.CurrentClassMastery;
        Ui.WriteLine($"  クラス習熟度: {mastery}/{a.MasteryCap}");
        if (a.IsAtMasteryCap)
        {
            if (a.IsMaxRank)
                Ui.Dim($"    {a.RankLabel}ランクの習熟度上限");
            else
                Ui.Dim($"    {a.RankLabel}ランクの習熟度上限。"
                    + $"{Rank.Label(a.rank + 1)}へ昇格すると上限{Rank.MasteryCap(a.rank + 1)}まで解放");
        }
        else
        {
            Ui.Dim($"    {a.currentClass.className}で適正ランク{a.SuitableRankRangeLabel}を正規クリアすると"
                + $"、現在のINT {a.intelligence}で+{a.MasteryPerSuitableClear}");
        }

        var next = a.currentClass.classSkills
            .Where(e => e.Skill != null && e.requiredClearCount > mastery)
            .OrderBy(e => e.requiredClearCount)
            .FirstOrDefault();
        if (next != null)
        {
            if (next.requiredClearCount > a.MasteryCap)
            {
                string gate = a.IsMaxRank
                    ? $"{a.RankLabel}ランク上限を超えるため現在は習得不可"
                    : $"{Rank.Label(a.rank + 1)}ランクへの昇格が必要";
                Ui.Dim($"    次のスキル: {next.Skill!.skillName}"
                    + $"（必要習熟度 {next.requiredClearCount}・{gate}）");
            }
            else
            {
                int remaining = next.requiredClearCount - mastery;
                int estimatedClears = (int)Math.Ceiling(remaining / (double)a.MasteryPerSuitableClear);
                Ui.Dim($"    次のスキル: {next.Skill!.skillName}（あと{remaining} / 現在のINTなら約{estimatedClears}回）");
            }
        }
        else
            Ui.Dim("    この職業のスキルはすべて習得済み");
    }

    static void ShowSkillSummary(AdventurerData a)
    {
        var skills = a.Skills;
        if (skills.Count == 0)
        {
            Ui.WriteLine("  スキル: なし");
            return;
        }

        Ui.WriteLine("  スキル: " + string.Join(", ", skills.Select(skill =>
            $"{SkillState(skill, a, isEffectiveTier: true).marker}{skill.skillName}")));
        Ui.Dim("    ○現在有効  △隊列条件あり  ×装備条件未達（全履歴は「スキル詳細」）");
    }

    static async Task ShowSkillDetailsAsync(AdventurerData a)
    {
        Ui.BeginScreen();
        Ui.Header($"習得スキル詳細: {a.name}");
        Ui.WriteLine("  ○ 現在の装備で有効  △ 前衛・後衛の配置時に有効");
        Ui.WriteLine("  × 装備条件未達      ▽ 上位Lvに置換済み");
        Ui.WriteLine();

        var effectiveTiers = a.Skills.ToHashSet();
        var learned = a.ExportLearnedSkills();
        Ui.WriteLine($"  習得履歴 {learned.Count}件 / 現在採用 {effectiveTiers.Count}件");
        Ui.WriteLine();

        foreach (var (skill, ownerClass) in learned)
        {
            var state = SkillState(skill, a, effectiveTiers.Contains(skill));
            string source = ownerClass?.className ?? "固有・イベント";
            Ui.WriteLine($"  {state.marker} {skill.skillName}  [{state.label}]  習得元: {source}");

            var details = SkillEffectParts(skill);
            string requirements = SkillRequirementText(skill);
            if (requirements.Length > 0) details.Add(requirements);
            if (skill.scope == SkillScope.UnitAura) details.Add("隊全体");
            Ui.Dim($"      {(details.Count > 0 ? string.Join(" / ", details) : "効果説明なし")}");
        }

        await Ui.PauseAsync();
    }

    static (string marker, string label) SkillState(
        SkillMasterData skill,
        AdventurerData adventurer,
        bool isEffectiveTier)
    {
        if (!isEffectiveTier) return ("▽", "上位Lvに置換");
        if (!UnitCalculator.MeetsGearRequirements(skill, adventurer)) return ("×", "装備条件未達");
        if (skill.frontOnly) return ("△", "前衛時に有効");
        if (skill.backOnly) return ("△", "後衛時に有効");
        return ("○", "有効");
    }

    static List<string> SkillEffectParts(SkillMasterData skill)
    {
        var parts = EquipmentText.BonusParts(skill.add);
        if (Math.Abs(skill.mul.hp - 1f) > 0.001f) parts.Add($"HPx{skill.mul.hp:0.##}");
        if (Math.Abs(skill.mul.san - 1f) > 0.001f) parts.Add($"士気x{skill.mul.san:0.##}");
        if (Math.Abs(skill.mul.heal - 1f) > 0.001f) parts.Add($"回復力x{skill.mul.heal:0.##}");
        parts.AddRange(EquipmentText.ExpeditionParts(skill.expedition));
        parts.AddRange(EquipmentText.BattleParts(skill.battle));
        if (!string.IsNullOrWhiteSpace(skill.unarmedDamageDice))
            parts.Add($"素手{skill.unarmedDamageDice}");
        if (skill.battleStartStatuses.Count > 0)
            parts.Add($"戦闘開始時効果{skill.battleStartStatuses.Count}件");
        if (skill.onHitStatuses.Count > 0)
            parts.Add($"命中時効果{skill.onHitStatuses.Count}件");
        return parts;
    }

    static string SkillRequirementText(SkillMasterData skill)
    {
        var parts = new List<string>();
        if (skill.requireWeaponType)
            parts.Add($"{EquipmentText.WeaponClassName(skill.requiredWeaponType)}装備");
        if (skill.requireArmorType) parts.Add($"{ArmorName(skill.requiredArmorType)}装備");
        if (skill.requireUnarmed) parts.Add("素手");
        if (skill.requireTwoHanded) parts.Add(skill.requirePhysicalWeapon ? "両手物理武器" : "両手武器");
        else if (skill.requirePhysicalWeapon) parts.Add("物理武器");
        if (skill.requireShield) parts.Add("盾");
        if (skill.requireOffHandWeapon) parts.Add("左手武器");
        if (skill.frontOnly) parts.Add("前衛");
        if (skill.backOnly) parts.Add("後衛");
        return parts.Count == 0 ? "" : $"条件: {string.Join("・", parts)}";
    }

    static string ArmorName(ArmorType type) => type switch
    {
        ArmorType.Cloth => "布防具",
        ArmorType.LightArmor => "軽鎧",
        ArmorType.Plate => "重鎧",
        _ => "防具",
    };

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

    /// <summary>
    /// 在籍枠と維持費を空けるための解雇。埋葬と違って費用は取らないが、
    /// 育てた分は戻らないので、何を失うかを見せてから確認する。
    /// </summary>
    static async Task<bool> DismissAdventurerAsync(AdventurerData a, GuildManager guild)
    {
        int upkeep = GuildManager.CalculateAdventurerUpkeep(a.level, a.rank);
        Ui.WriteLine($"  {a.name}（Lv{a.level} ランク{a.RankLabel} {a.ClassAndRace}）を解雇します。");
        Ui.WriteLine($"  装備は倉庫に戻り、維持費が{upkeep}G/T減ります（解雇費用はかかりません）。");
        Ui.WriteLine($"  在籍: {guild.RosterCount}/{guild.RosterCapacity}人"
            + $" → {guild.RosterCount - 1}/{guild.RosterCapacity}人");
        Ui.Warn("  ⚠ レベル・ランク・クラス習熟度は戻りません。");
        Ui.Warn("     同じ人物が雇入れ候補に戻ることはありますが、そのときは初期状態からです。");
        if (!await Ui.ConfirmAsync($"{a.name} を解雇しますか？")) return false;

        EquipService.UnequipAll(a, guild);
        if (!guild.TryDismissAdventurer(a, out var reason))
        {
            Ui.Error(reason);
            await Ui.PauseAsync();
            return false;
        }
        Ui.Info($"{a.name} を解雇しました");
        await Ui.PauseAsync();
        return true;
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

    static async Task ConfirmRankUpAsync(AdventurerData a, GuildManager guild)
    {
        var beforeStats = a.GetFinalCombatStats();
        int beforeCarry = a.CarryLimit;
        int rankGain = AdventurerData.RankUpStatGain;
        int expectedCarryAfter = a.constitution + rankGain
            + (a.strength + rankGain + a.vitality + rankGain) / 2
            + a.SkillCarryBonus;
        string suitableBefore = a.SuitableRankRangeLabel;
        int levelCapAfter = Rank.LevelCap(a.rank + 1);
        int masteryCapAfter = Rank.MasteryCap(a.rank + 1);
        int masteryAfter = Math.Min(
            masteryCapAfter, a.CurrentClassMastery + AdventurerData.RankUpMasteryGain);
        int masteryGain = masteryAfter - a.CurrentClassMastery;
        var learned = a.AllLearnedSkills.ToHashSet();
        var expectedUnlocks = a.currentClass?.classSkills
            .Where(entry => entry.Skill != null
                && entry.requiredClearCount > a.CurrentClassMastery
                && entry.requiredClearCount <= masteryAfter
                && !learned.Contains(entry.Skill))
            .Select(entry => entry.Skill!)
            .Distinct()
            .ToList() ?? new List<SkillMasterData>();

        Ui.BeginScreen();
        Ui.Header($"昇格: {a.name}");
        Ui.WriteLine($"  現在: {Rank.Label(a.rank)}");
        Ui.WriteLine($"  昇格後: {Rank.Label(a.rank + 1)}");
        Ui.WriteLine();
        Ui.WriteLine($"  ・レベル上限 Lv{a.LevelCap} → Lv{levelCapAfter}");
        Ui.WriteLine($"  ・習熟度上限 {a.MasteryCap} → {masteryCapAfter}");
        Ui.WriteLine($"  ・全能力値 +{AdventurerData.RankUpStatGain}");
        if (a.currentClass != null)
            Ui.WriteLine($"  ・{a.currentClass.className} 習熟度 +{masteryGain}（{a.CurrentClassMastery} → {masteryAfter}）");
        if (expectedUnlocks.Count > 0)
            Ui.Info($"  ・習得見込み: 「{string.Join("」「", expectedUnlocks.Select(skill => skill.skillName))}」");
        Ui.WriteLine($"  ・積載上限 {beforeCarry} → {expectedCarryAfter}");
        Ui.WriteLine($"  ・適正クエスト {suitableBefore} → {Rank.SuitableRangeLabel(a.rank + 1)}");
        Ui.WriteLine($"  ・敵との認定ランク差を1段縮め、格上遭遇時の士気ショックを軽減");
        int upkeepBefore = GuildManager.CalculateAdventurerUpkeep(a.level, a.rank);
        int upkeepAfter = GuildManager.CalculateAdventurerUpkeep(a.level, a.rank + 1);
        Ui.WriteLine($"  ・維持費 {upkeepBefore}G/T → {upkeepAfter}G/T");
        Ui.WriteLine();

        if (!await Ui.ConfirmAsync("昇格させますか？")) return;

        if (!a.TryRankUp(out var result))
        {
            Ui.Error("昇格条件を満たしていません");
            await Ui.PauseAsync();
            return;
        }

        Ui.Info(result.HistoryLine());
        var afterStats = a.GetFinalCombatStats();
        Ui.WriteLine("  実戦値の変化:");
        bool changed = false;
        changed |= ShowStatDelta("HP", beforeStats.hp, afterStats.hp);
        changed |= ShowStatDelta("士気", beforeStats.san, afterStats.san);
        changed |= ShowStatDelta("装甲AV", beforeStats.av, afterStats.av);
        changed |= ShowStatDelta("魔装甲mAV", beforeStats.mav, afterStats.mav);
        changed |= ShowStatDelta("回避DV", beforeStats.dv, afterStats.dv);
        changed |= ShowStatDelta("命中", beforeStats.toHit, afterStats.toHit);
        changed |= ShowStatDelta("積載上限", beforeCarry, a.CarryLimit);
        if (!changed) Ui.Dim("    実戦値の段階は変わりませんでした（能力値と習熟度は上昇済み）");
        guild.economyLogs.Add($"昇格: {a.name} {result.HistoryLine()}");
        await Ui.PauseAsync();
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
        var unlockedSkills = a.ChangeClass(chosen);
        Ui.Info($"{a.name} のクラスを {chosen.className} に変更しました");
        if (unlockedSkills.Count > 0)
            Ui.Info($"{a.name}がスキル「{string.Join("」「", unlockedSkills.Select(skill => skill.skillName))}」を習得しました");
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
            ShowCarryStatus(a, "  ");
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
        int beforeWeight = a.TotalEquipmentWeight;

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
        int afterWeight = a.TotalEquipmentWeight;
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
        if (beforeWeight != afterWeight)
        {
            int delta = afterWeight - beforeWeight;
            Ui.WriteLine($"    装備重量: {beforeWeight} → {afterWeight}  {(delta > 0 ? $"▲+{delta}" : $"▼{delta}")}",
                delta > 0 ? TextStyle.Error : TextStyle.Info);
            hasChange = true;
        }
        ShowCarryStatus(a, "    ");

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

    /// <summary>
    /// 身につけた特性と、それを生んだ遠征記録。
    /// 解禁に必要な数はヘルプの「特性と解禁条件」に並べてあるので、ここでは現在値だけを出す。
    /// </summary>
    static void ShowTraitsAndRecords(AdventurerData adventurer, GameMasterData db)
    {
        var learned = adventurer.AllLearnedSkills.ToHashSet();
        var traits = db.traits.Values
            .Where(t => t.Skill != null && learned.Contains(t.Skill))
            .ToList();

        if (traits.Count > 0)
        {
            Ui.WriteLine();
            Ui.WriteLine("  特性（遠征での戦い方から身についたもの）:");
            var lens = TraitSystem.LensOf(adventurer);
            foreach (var trait in traits)
            {
                // いま構えている得物から見た値で出す。杖に持ち替えれば物理特性は死んでいる。
                var effect = TraitAnalysis.Evaluate(trait.Skill!, lens);
                string cost = effect.Drawbacks.Count == 0
                    ? "代償なし"
                    : $"代償 {string.Join("、", effect.Drawbacks)}";
                string benefit = effect.Benefits.Count == 0
                    ? $"いまの得物では効果なし（{TraitAnalysis.LensName(lens)}型）"
                    : string.Join("、", effect.Benefits);
                Ui.Info($"    ・{trait.traitName}: {benefit}（{cost}）");
            }
        }

        if (adventurer.records.IsEmpty) return;

        Ui.WriteLine();
        Ui.WriteLine("  くぐってきたもの:");
        foreach (var type in ExpeditionRecordTypes.All)
        {
            int count = adventurer.records[type];
            if (count <= 0) continue;
            string mark = ExpeditionRecordTypes.IsRisk(type) ? "◆" : "・";
            Ui.Dim($"    {mark}{ExpeditionRecordTypes.DisplayName(type)}: {count}");
        }
        Ui.Dim("    ◆は命を危険に晒した記録。素直な強化の特性はここからしか生えない");
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

    static void ShowCarryStatus(AdventurerData adventurer, string indent)
    {
        string line = $"{indent}装備重量: {adventurer.TotalEquipmentWeight}/{adventurer.CarryLimit}";
        if (adventurer.OverweightAmount <= 0)
        {
            Ui.WriteLine(line);
            return;
        }

        Ui.Warn($"{line}  [過積載 +{adventurer.OverweightAmount}: 命中-{adventurer.OverweightToHitPenalty} / DV-{adventurer.OverweightDvPenalty}]");
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
