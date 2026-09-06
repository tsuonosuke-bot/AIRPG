using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Game.Presentation;

namespace GuildSimulator.Game.Screens;

public static class QuestBoardScreen
{
    sealed record QuestObjective(string TypeLabel, string Summary, string Detail);

    public static async Task ShowAsync(QuestManager questManager, GuildManager guild, int currentTurn)
    {
        while (true)
        {
            Ui.BeginScreen();
            Ui.Header("クエストボード");
            var board = questManager.questBoard
                .OrderByDescending(entry => entry.quest.isStoryQuest)
                .ThenByDescending(entry => entry.quest.isEmergencyQuest)
                .ToList();
            var availableAdvs = guild.adventurers.Where(a => a.isAlive && !questManager.IsAdventurerBusy(a.id)).ToList();
            int partyAvgLevel = availableAdvs.Count > 0 ? (int)Math.Round(availableAdvs.Average(a => a.level)) : 0;
            Ui.WriteLine($"  受注可能: ギルドランク{guild.GuildRankLabel}以下    待機中冒険者: {availableAdvs.Count}人（平均Lv{partyAvgLevel}）");
            Ui.WriteLine();
            if (board.Count == 0)
            {
                Ui.Warn("掲示中のクエストはありません");
                await Ui.PauseAsync();
                return;
            }

            var entries = new List<MenuOption>();
            for (int i = 0; i < board.Count; i++)
            {
                var e = board[i];
                var q = e.quest;
                string emg = q.isEmergencyQuest ? " [緊急]" : "";
                string story = q.isStoryQuest ? " [物語]" : "";
                int estTurns = (int)Math.Ceiling((double)q.totalPhases / q.phasesPerTurn);
                var diff = DungeonDifficulty.Evaluate(q);
                var objective = DescribeObjective(q);

                // 一覧は一目で比較できる要点だけにする。詳細はタップ後の確認画面で見せる。
                string posting = q.isStoryQuest
                    ? "物語専用枠: 受注まで継続掲示"
                    : $"掲示期限: あと{e.RemainingTurns(currentTurn, questManager.BoardExpireTurns)}ターン";
                string summary = $"達成条件: {objective.Summary}　危険度目安: {DifficultyLabel(diff)}"
                    + $"\n基本報酬（活躍手当込み） 資金:{QuestRewardService.AdjustedBaseGold(q.rewardGold)}G"
                    + $" 経験値:{QuestRewardService.AdjustedBaseExp(q.rewardExp)} ギルドポイント:{q.rewardGuildPoints}"
                    + $"　{posting}";

                entries.Add(new MenuOption(
                    (i + 1).ToString(),
                    $"【{Rank.Label(q.rank)}】【{objective.TypeLabel}】{q.questName}  所要:{estTurns}T{emg}{story}",
                    summary,
                    q.isStoryQuest ? TextStyle.Accent
                        : q.isEmergencyQuest ? TextStyle.Warn : TextStyle.Normal));
            }

            int? sel = await Ui.SelectIndexAsync("受注するクエスト", entries);
            if (sel == null) return;
            var entry = board[sel.Value - 1];
            if (await ShowQuestDetailAsync(entry, questManager, currentTurn, availableAdvs))
                await SelectAndStartAsync(entry.quest, questManager, guild, currentTurn);
        }
    }

    /// <summary>タップ後に詳細を出し、受注するかどうかをここで確定させる。</summary>
    static async Task<bool> ShowQuestDetailAsync(
        QuestBoardEntry e, QuestManager questManager, int currentTurn, List<AdventurerData> availableAdvs)
    {
        var q = e.quest;
        int estTurns = (int)Math.Ceiling((double)q.totalPhases / q.phasesPerTurn);
        var diff = DungeonDifficulty.Evaluate(q);
        var objective = DescribeObjective(q);
        string emg = q.isEmergencyQuest ? " [緊急]" : "";
        string story = q.isStoryQuest ? " [物語]" : "";

        Ui.BeginScreen();
        Ui.Header($"【{Rank.Label(q.rank)}】【{objective.TypeLabel}】{q.questName}{emg}{story}");
        if (!string.IsNullOrWhiteSpace(q.clientName))
            Ui.WriteLine($"  依頼人: {q.clientName}");
        if (!string.IsNullOrWhiteSpace(q.description))
            Ui.WriteLine($"  {q.description}");
        Ui.WriteLine();
        Ui.WriteLine($"  種別: {objective.TypeLabel}");
        Ui.WriteLine($"  達成条件: {objective.Detail}", TextStyle.Info);
        Ui.WriteLine($"  所要目安: {estTurns}ターン（予定{q.totalPhases}エリア）");
        Ui.WriteLine($"  依頼ランク: {Rank.Label(q.rank)}　危険度目安: {DifficultyLabel(diff)}");
        Ui.Dim("    危険度の順: 楽勝 < 軽め < 標準 < 危険 < 過酷");
        Ui.WriteLine($"  基本報酬（活躍手当込み） 資金:{QuestRewardService.AdjustedBaseGold(q.rewardGold)}G"
            + $" 経験値:{QuestRewardService.AdjustedBaseExp(q.rewardExp)} ギルドポイント:{q.rewardGuildPoints}");
        if (q.IsGatherQuest)
            Ui.WriteLine($"  採取ルール: 目標超過1個につき +{q.gatherGoldPerItem}G / 必要数を集めた時点で帰還"
                + $" / {q.totalPhases}エリアで足りなければ延長か撤退を選ぶ");
        string bossInfo = diff.hasBoss
            ? $"  討伐対象脅威:{diff.BossThreatLabel}（最終エリアで確定戦闘）"
            : "";
        Ui.WriteLine($"  場所: {q.Dungeon?.dungeonName ?? "？"}  通常遭遇:{diff.EnemyThreatSummary}"
            + $"  編成:{diff.EnemyFormationSummary}  戦闘{diff.combatChance * 100:0}% 罠{diff.trapChance * 100:0}%{bossInfo}");
        // 習熟度は適正ランクのクエストでしか増えない。誰を出せば伸びるのかを受注前に見せる。
        int suitableCount = availableAdvs.Count(a => a.IsSuitableQuestRank(q.rank));
        Ui.WriteLine($"  適正ランク: {Rank.SuitableAdventurerRangeLabel(q.rank)}（このランク帯の冒険者が正規クリアすると習熟度が入る）"
            + $"（待機中 {suitableCount}/{availableAdvs.Count}人が該当）");
        if (q.isStoryQuest)
            Ui.Info("  物語専用枠: 受注するまで掲示され続けます");
        else
            Ui.WriteLine($"  掲示期限: あと{e.RemainingTurns(currentTurn, questManager.BoardExpireTurns)}ターン");
        Ui.WriteLine();

        return await Ui.ConfirmAsync("このクエストを受注しますか？");
    }

    static async Task SelectAndStartAsync(
        QuestMasterData def, QuestManager qm, GuildManager guild, int currentTurn)
    {
        var formation = new AdventurerData?[GuildManager.FormationSlotCount];
        int partyCapacity = guild.PartyCapacity;
        // 方針も毎回選び直させない。前回出発したときの方針を初期値にする。
        var policy = guild.lastParty?.policy ?? ExpeditionPolicy.SurvivalFirst;
        var notices = new List<string>();

        while (true)
        {
            // 編成をひとつ動かすたびに画面を描き直す。Web版で変更前と変更後の
            // 「現在の編成」が同じ画面に積み重ならないようにする。
            Ui.BeginScreen();
            Ui.Header($"編成: {def.questName}");
            Ui.WriteLine("冒険者を選ぶと空いている位置へ自動で配置します（位置の調整は「配置を変える」から）");
            Ui.WriteLine();
            ShowFormation(formation, partyCapacity);
            Ui.WriteLine($"  遠征方針: {QuestManager.PolicyName(policy)}");
            foreach (string notice in notices) Ui.Warn($"  {notice}");
            notices.Clear();
            Ui.WriteLine();

            int memberCount = formation.Count(member => member != null);
            var available = SelectableMembers(guild, qm, formation);
            var options = new List<MenuOption>();

            if (memberCount >= partyCapacity)
                Ui.Dim("  編成上限まで埋まっています。入れ替えるには「配置を変える」で外してください");
            else if (available.Count == 0)
                Ui.Dim("  いま出せる冒険者をすべて編成しました");
            else
                for (int i = 0; i < available.Count; i++)
                {
                    var a = available[i];
                    options.Add(new MenuOption(
                        (i + 1).ToString(),
                        $"{a.name} Lv{a.level}" + (a.IsInjured ? $" [負傷{a.injuries.Count}]" : ""),
                        a.ClassAndRace + (a.IsInjured ? $" / {a.ConditionSummary}" : ""),
                        Ui.RarityStyle(a.master.rarity),
                        Group: "追加する冒険者"));
                }

            const string menuGroup = "編成メニュー";
            var saved = SavedPartyChoices(guild);
            if (saved.Count > 0)
                options.Add(new MenuOption("l", "保存した編成を呼び出す",
                    string.Join(" / ", saved.Select(p => p.name)), Group: menuGroup));
            if (memberCount > 0)
            {
                options.Add(new MenuOption("s", "いまの編成を保存する",
                    $"名前を付けて{GuildManager.PartyPresetLimit}件まで残せます", Group: menuGroup));
                options.Add(new MenuOption("e", "配置を変える / 外す", Group: menuGroup));
            }
            options.Add(new MenuOption("p", "遠征方針を変える",
                $"現在: {QuestManager.PolicyName(policy)}", Group: menuGroup));
            if (memberCount > 0)
                options.Add(new MenuOption("d", "この編成で進む", Role: MenuRole.Primary, Group: menuGroup));
            options.Add(new MenuOption("0", "受注をやめる", Style: TextStyle.Dim, Group: menuGroup));

            string key = await Ui.SelectAsync("操作", options);
            if (key == "0") { Ui.Warn("受注をキャンセルしました"); return; }
            if (key == "l") { await LoadPartyAsync(guild, qm, formation, partyCapacity, notices, value => policy = value); continue; }
            if (key == "s") { await SavePartyAsync(guild, formation, policy, notices); continue; }
            if (key == "e") { await EditPlacementAsync(formation); continue; }
            if (key == "p") { policy = await SelectPolicyAsync(policy); continue; }
            if (key == "d")
            {
                if (await ConfirmAndStartAsync(def, qm, guild, currentTurn, formation, policy, partyCapacity))
                    return;
                continue;
            }
            if (int.TryParse(key, out int pick) && pick >= 1 && pick <= available.Count)
            {
                var member = available[pick - 1];
                int? slot = PreferredSlot(member, formation);
                if (slot == null) notices.Add("空いている配置がありません");
                else formation[slot.Value] = member;
            }
        }
    }

    /// <summary>いま編成に入れられる冒険者（生存・遠征中でない・未配置）。</summary>
    static List<AdventurerData> SelectableMembers(
        GuildManager guild, QuestManager qm, AdventurerData?[] formation) =>
        guild.adventurers
            .Where(a => a.isAlive && !qm.IsAdventurerBusy(a.id) && !formation.Contains(a))
            .ToList();

    /// <summary>
    /// 追加した冒険者を置く位置。武器の間合いで前衛・後衛を決め、希望の列が埋まっていれば
    /// 反対の列へ回す。ここで妥当な位置に入るから、追加のたびに配置を選ばせずに済む。
    /// </summary>
    static int? PreferredSlot(AdventurerData member, AdventurerData?[] formation)
    {
        var front = Enumerable.Range(0, GuildManager.FrontRowSlotCount);
        var rear = Enumerable.Range(
            GuildManager.FrontRowSlotCount,
            formation.Length - GuildManager.FrontRowSlotCount);
        var order = UsesRangedOrSupportWeapon(member)
            ? rear.Concat(front)
            : front.Concat(rear);
        foreach (int slot in order)
            if (formation[slot] == null) return slot;
        return null;
    }

    /// <summary>呼び出せる編成。「前回の編成」は保存し忘れの受け皿なので先頭に置く。</summary>
    static List<PartyPreset> SavedPartyChoices(GuildManager guild)
    {
        var choices = new List<PartyPreset>();
        if (guild.lastParty != null) choices.Add(guild.lastParty);
        choices.AddRange(guild.partyPresets);
        return choices;
    }

    static async Task LoadPartyAsync(
        GuildManager guild,
        QuestManager qm,
        AdventurerData?[] formation,
        int partyCapacity,
        List<string> notices,
        Action<ExpeditionPolicy> applyPolicy)
    {
        while (true)
        {
            var choices = SavedPartyChoices(guild);
            if (choices.Count == 0) return;

            Ui.BeginScreen();
            Ui.Header("保存した編成");
            var options = choices
                .Select((preset, i) => new MenuOption(
                    (i + 1).ToString(),
                    preset.name,
                    $"{DescribePreset(preset, guild)}　方針:{QuestManager.PolicyName(preset.policy)}"))
                .ToList();
            if (guild.partyPresets.Count > 0)
                options.Add(new MenuOption("x", "保存した編成を削除する", Style: TextStyle.Dim));

            string key = await Ui.SelectAsync("呼び出す編成",
                new List<MenuOption>(options) { new("0", "戻る", Style: TextStyle.Dim) });
            if (key == "x") { await DeletePartyPresetAsync(guild); continue; }
            if (!int.TryParse(key, out int pick) || pick < 1 || pick > choices.Count) return;

            ApplyPreset(choices[pick - 1], formation, guild, qm, partyCapacity, notices);
            applyPolicy(choices[pick - 1].policy);
            return;
        }
    }

    /// <summary>保存内容を一覧で読めるようにする。いま出せない相手はその理由を添える。</summary>
    static string DescribePreset(PartyPreset preset, GuildManager guild)
    {
        var names = new List<string>();
        foreach (string? id in preset.memberIds)
        {
            if (string.IsNullOrEmpty(id)) continue;
            var member = guild.adventurers.FirstOrDefault(a => a.id == id);
            names.Add(member == null ? "（離脱）" : member.isAlive ? member.name : $"{member.name}（死亡）");
        }
        return names.Count == 0 ? "メンバーなし" : string.Join("、", names);
    }

    /// <summary>
    /// 保存した編成を現在の盤面へ流し込む。いま連れて行けない相手は飛ばし、
    /// 何が欠けたのかを編成画面へ持ち帰る（黙って人数が減ると気づけない）。
    /// </summary>
    static void ApplyPreset(
        PartyPreset preset,
        AdventurerData?[] formation,
        GuildManager guild,
        QuestManager qm,
        int partyCapacity,
        List<string> notices)
    {
        for (int slot = 0; slot < formation.Length; slot++) formation[slot] = null;

        var missing = new List<string>();
        int placed = 0;
        for (int slot = 0; slot < formation.Length && slot < preset.memberIds.Length; slot++)
        {
            string? id = preset.memberIds[slot];
            if (string.IsNullOrEmpty(id)) continue;

            var member = guild.adventurers.FirstOrDefault(a => a.id == id);
            if (member == null || !member.isAlive) { missing.Add(member?.name ?? "離脱した隊員"); continue; }
            if (qm.IsAdventurerBusy(member.id)) { missing.Add($"{member.name}（遠征中）"); continue; }
            if (placed >= partyCapacity) { missing.Add($"{member.name}（編成上限）"); continue; }

            formation[slot] = member;
            placed++;
        }

        // 上限より前の空きスロットへ詰め直す必要はない。位置ごと保存してあるので並びは保たれる。
        if (placed == 0) notices.Add("⚠ この編成のメンバーは誰も出せません");
        else if (missing.Count > 0)
            notices.Add($"⚠ 外れたメンバー: {string.Join("、", missing)}");
    }

    static async Task SavePartyAsync(
        GuildManager guild,
        AdventurerData?[] formation,
        ExpeditionPolicy policy,
        List<string> notices)
    {
        Ui.BeginScreen();
        Ui.Header("編成を保存");
        Ui.WriteLine($"  保存済み: {guild.partyPresets.Count}/{GuildManager.PartyPresetLimit}件");
        if (guild.partyPresets.Count > 0)
            Ui.Dim("    同じ名前で保存すると上書きします: "
                + string.Join("、", guild.partyPresets.Select(p => p.name)));
        Ui.WriteLine();

        string? input = await Ui.ReadLineAsync(
            $"編成名（{PartyPreset.MaxNameLength}文字まで／空欄なら自動）");
        string name = (input ?? "").Trim();
        if (name.Length == 0) name = DefaultPresetName(guild, formation);

        if (guild.TrySavePartyPreset(name, formation, policy, out string reason))
            notices.Add($"編成「{name}」を保存しました");
        else
            notices.Add($"⚠ 保存できません: {reason}");
    }

    static string DefaultPresetName(GuildManager guild, AdventurerData?[] formation)
    {
        var members = formation.Where(a => a != null).Select(a => a!).ToList();
        string lead = members.Count > 0 ? members[0].name : "編成";
        string candidate = members.Count > 1 ? $"{lead}隊" : lead;
        if (candidate.Length > PartyPreset.MaxNameLength)
            candidate = candidate[..PartyPreset.MaxNameLength];

        // 同名を自動で選ぶと既存の編成を黙って上書きしてしまう。空いている番号を足す。
        string unique = candidate;
        for (int suffix = 2; guild.partyPresets.Any(p => p.name == unique); suffix++)
            unique = $"{candidate}{suffix}";
        return unique;
    }

    static async Task DeletePartyPresetAsync(GuildManager guild)
    {
        var options = guild.partyPresets
            .Select((preset, i) => new MenuOption(
                (i + 1).ToString(), preset.name, DescribePreset(preset, guild)))
            .ToList();
        int? pick = await Ui.SelectIndexAsync("削除する編成", options);
        if (pick == null) return;

        var target = guild.partyPresets[pick.Value - 1];
        if (await Ui.ConfirmAsync($"「{target.name}」を削除しますか？"))
            guild.RemovePartyPreset(target);
    }

    /// <summary>配置済みの隊員を選び、位置の入れ替えか編成からの除外を行う。</summary>
    static async Task EditPlacementAsync(AdventurerData?[] formation)
    {
        while (true)
        {
            var placed = Enumerable.Range(0, formation.Length)
                .Where(slot => formation[slot] != null)
                .ToList();
            if (placed.Count == 0) return;

            Ui.BeginScreen();
            Ui.Header("配置の変更");
            var memberOptions = placed
                .Select((slot, i) => new MenuOption(
                    (i + 1).ToString(),
                    $"{PositionName(slot)}: {formation[slot]!.name}",
                    formation[slot]!.ClassAndRace,
                    Ui.RarityStyle(formation[slot]!.master.rarity)))
                .ToList();
            int? pick = await Ui.SelectIndexAsync("動かす隊員", memberOptions, "編成へ戻る");
            if (pick == null) return;

            int from = placed[pick.Value - 1];
            var target = formation[from]!;

            var slotOptions = Enumerable.Range(0, formation.Length)
                .Where(slot => slot != from)
                .Select(slot => new MenuOption(
                    (slot + 1).ToString(),
                    formation[slot] == null
                        ? $"{PositionName(slot)}（空き）へ移す"
                        : $"{PositionName(slot)}の{formation[slot]!.name}と入れ替える"))
                .ToList();
            slotOptions.Add(new MenuOption("x", $"{target.name}を編成から外す", Style: TextStyle.Warn));

            string key = await Ui.SelectAsync($"{target.name}の移動先",
                new List<MenuOption>(slotOptions) { new("0", "やめる", Style: TextStyle.Dim) });
            if (key == "x") { formation[from] = null; continue; }
            if (!int.TryParse(key, out int slotNumber)) continue;

            int to = slotNumber - 1;
            if (to < 0 || to >= formation.Length || to == from) continue;
            (formation[from], formation[to]) = (formation[to], formation[from]);
        }
    }

    /// <summary>編成確認から出発まで。受注できたらtrue、編成画面へ戻るならfalse。</summary>
    static async Task<bool> ConfirmAndStartAsync(
        QuestMasterData def,
        QuestManager qm,
        GuildManager guild,
        int currentTurn,
        AdventurerData?[] formation,
        ExpeditionPolicy policy,
        int partyCapacity)
    {
        Ui.BeginScreen();
        Ui.Header("編成確認");
        ShowFormation(formation, partyCapacity);
        ShowPartyPreview(formation, def, partyCapacity);
        var carriedConsumables = await SelectConsumablesAsync(guild, formation);
        Ui.WriteLine($"  遠征方針: {QuestManager.PolicyName(policy)}");
        if (carriedConsumables.Count > 0)
            Ui.WriteLine($"  持ち込み（出発時消費）: {string.Join(", ", carriedConsumables.Select(x => x.DisplayName))}");
        if (!await Ui.ConfirmAsync("このメンバーで受注しますか？")) return false;

        if (qm.TryStartQuestWithConsumables(
            def, formation, currentTurn, out var error, carriedConsumables, policy))
        {
            // 保存し忘れても次の受注で呼び出せるように、出発した編成は必ず控えておく。
            guild.RecordLastParty(formation, policy);
            Ui.Info($"クエスト「{def.questName}」を受注しました！ （Turn {currentTurn} 開始）");
            await Ui.PauseAsync();
            return true;
        }

        Ui.Error($"受注失敗: {error}");
        await Ui.PauseAsync();
        return false;
    }

    static async Task<ExpeditionPolicy> SelectPolicyAsync(ExpeditionPolicy current)
    {
        Ui.BeginScreen();
        Ui.Header("遠征方針");
        string key = await Ui.SelectAsync("遠征方針", new[]
        {
            new MenuOption("1", "生還優先" + (current == ExpeditionPolicy.SurvivalFirst ? "（現在）" : ""),
                $"パーティHP{BattleResolver.SurvivalPartyHpPercent}%以下、または誰かが{BattleResolver.SurvivalMemberHpPercent}%以下で撤退する"),
            new MenuOption("2", "依頼達成優先" + (current == ExpeditionPolicy.ObjectiveFirst ? "（現在）" : ""),
                "行動可能な限り任務を続行する。戦闘不能者が出るほど帰還時の死亡リスクが高まる"),
            new MenuOption("0", "変更しない", Style: TextStyle.Dim),
        });
        return key switch
        {
            "1" => ExpeditionPolicy.SurvivalFirst,
            "2" => ExpeditionPolicy.ObjectiveFirst,
            _ => current,
        };
    }

    static async Task<List<ConsumableUse>> SelectConsumablesAsync(
        GuildManager guild, AdventurerData?[] formation)
    {
        var selected = new List<ConsumableUse>();
        for (int slot = 1; slot <= 2; slot++)
        {
            var stock = guild.GetConsumablesView()
                .Where(s => s.count > selected.Count(x => x.item == s.item))
                .ToList();
            if (stock.Count == 0) break;

            var options = stock
                .Select((s, i) => new MenuOption(
                    (i + 1).ToString(),
                    $"{s.item.displayName} x{s.count}",
                    s.item.description,
                    Ui.RarityStyle(s.item.rarity)))
                .ToList();

            int? pick = await Ui.SelectIndexAsync(
                $"持ち込みスロット{slot}（出発時に消費）", options, "選択を終了");
            if (pick == null) break;
            var item = stock[pick.Value - 1].item;
            AdventurerData? target = null;
            if (item.RequiresTarget)
            {
                var members = formation.Where(a => a != null).Select(a => a!).ToList();
                var targetOptions = members.Select((a, i) => new MenuOption(
                    (i + 1).ToString(),
                    $"{a.name} Lv{a.level}",
                    a.ClassAndRace,
                    Ui.RarityStyle(a.master.rarity))).ToList();
                int? targetPick = await Ui.SelectIndexAsync(
                    $"{item.displayName}を使う冒険者", targetOptions, "道具選択へ戻る");
                if (targetPick == null)
                {
                    slot--;
                    continue;
                }
                target = members[targetPick.Value - 1];
            }
            selected.Add(new ConsumableUse(item, target));
        }
        return selected;
    }

    static void ShowFormation(AdventurerData?[] formation, int partyCapacity)
    {
        int memberCount = formation.Count(member => member != null);
        Ui.WriteLine($"  現在の編成: {memberCount}/{partyCapacity}人"
            + $"（編成枠強化で最大{GuildManager.MaximumPartyCapacity}人）");
        Ui.Dim("    配置位置は前衛3＋後衛3の6マス。人数上限以内なら好きな位置を選べます");
        for (int i = 0; i < formation.Length; i++)
        {
            Ui.Write($"    {PositionName(i),-4}: ");
            if (formation[i] != null)
                Ui.WriteRarityName(formation[i]!.name, formation[i]!.master.rarity);
            else
                Ui.Write("空");
            Ui.WriteLine();
        }
        if (memberCount >= partyCapacity && partyCapacity < GuildManager.MaximumPartyCapacity)
            Ui.Dim("    現在の編成上限に達しました。ギルド施設を建てると1人ずつ拡張できます");
    }

    static string PositionName(int slot) => slot < GuildManager.FrontRowSlotCount
        ? $"前衛{slot + 1}"
        : $"後衛{slot - GuildManager.FrontRowSlotCount + 1}";

    internal static void ShowPartyPreview(
        AdventurerData?[] formation,
        QuestMasterData def,
        int? partyCapacity = null)
    {
        var members = formation.Where(a => a != null).Select(a => a!).ToList();
        if (members.Count == 0) return;

        var perMember = UnitCalculator.CalcPerMember(
            formation.Cast<IUnitMember?>().ToArray(), isAllySide: true);
        int totalHp = perMember.Sum(x => x.stats.hp);
        int totalMorale = perMember.Sum(x => x.stats.san);
        int avgLevel = (int)Math.Round(members.Average(a => a.level));

        var diff = DungeonDifficulty.Evaluate(def);
        var frontMembers = formation.Take(3).Where(a => a != null).Select(a => a!).ToList();
        var rearMembers = formation.Skip(3).Where(a => a != null).Select(a => a!).ToList();
        int healerCount = members.Count(member => member.Weapon?.IsHealWeapon == true);

        Ui.WriteLine();
        Ui.Header("パーティ戦力");
        Ui.WriteLine($"  平均レベル: {avgLevel}   合計HP: {totalHp}   推定士気: {totalMorale}");
        Ui.WriteLine($"  配置役割: 前衛 {frontMembers.Count}人 / 後衛 {rearMembers.Count}人"
            + $" / 回復役 {healerCount}人");
        int maxAppearance = AppearanceSystem.HighestAppearance(formation);
        int fameBonus = AppearanceSystem.GuildPointBonusPercent(formation);
        int battleMorale = AppearanceSystem.BattleMoralePerRound(
            formation.Cast<IUnitMember?>());
        Ui.WriteLine($"  最高APP: {maxAppearance}   名声ボーナス: +{fameBonus}%"
            + $"   戦闘中の士気回復: +{battleMorale}/ラウンド");
        Ui.WriteLine($"  クエスト危険度: {DifficultyLabel(diff)}  通常遭遇: {diff.EnemyThreatSummary}"
            + $"  編成: {diff.EnemyFormationSummary}");
        if (diff.hasBoss)
            Ui.WriteLine($"  ボス: 脅威度{diff.BossThreatLabel} / {diff.bossMemberCount}体（確定戦闘）");
        var assessment = DungeonDifficulty.EvaluateParty(def, members);
        string assessmentText = $"  編成相対評価: {assessment.Label}"
            + $"（人数 {assessment.MemberCount}/{assessment.RecommendedSize}人目安、"
            + $"平均認定{assessment.AverageRankLabel}/評価基準{assessment.TargetThreatLabel}）";
        if (assessment.Score < 0) Ui.Warn(assessmentText);
        else Ui.Info(assessmentText);
        if (partyCapacity.HasValue && assessment.RecommendedSize > partyCapacity.Value)
            Ui.Warn($"  ⚠ 現在の編成上限は{partyCapacity.Value}人です。"
                + "ギルド施設で上限を拡張すると推奨人数へ近づけます");
        Ui.Dim("    ※人数・認定ランク・負傷状態による目安。装備や相性、乱数で結果は変わります");

        if (frontMembers.Count == 0)
            Ui.Warn("  ⚠ 前衛不在: 後衛への攻撃を遮る隊員がいません");

        var rearMelee = rearMembers
            .Where(member => !UsesRangedOrSupportWeapon(member))
            .ToList();
        if (rearMelee.Count > 0)
            Ui.Warn($"  ⚠ 後衛の近接役: {string.Join("、", rearMelee.Select(member => member.name))}"
                + $"（命中-{BattleResolver.REAR_MELEE_TO_HIT_PENALTY}）");

        bool sustainedCombatExpected = diff.hasBoss || diff.expectedFights >= 2f;
        if (members.Count >= 3 && healerCount == 0 && sustainedCombatExpected)
            Ui.Warn("  ⚠ 回復役不在: 3人以上で連戦またはボス戦に臨みます（回復武器の装備者なし）");

        // 最大値だけで危険度を断定せず、通常遭遇の確率と確定ボスを分けて知らせる。
        int avgRank = assessment.AverageRank;
        if (assessment.OutrankedEncounterChancePercent >= 0.5f)
        {
            int shock = Math.Min(
                MoraleState.ThreatGapFlatCap,
                (diff.enemyThreatMax - avgRank) * MoraleState.ThreatGapFlat);
            Ui.Warn($"  ⚠ 格上との通常遭遇見込み {assessment.OutrankedEncounterChancePercent:0.#}%"
                + $"（最大{assessment.MaximumEncounterThreatLabel}、遭遇時の士気 最大-{shock}）");
        }
        if (diff.hasBoss && avgRank < diff.bossThreat)
        {
            int shock = Math.Min(
                MoraleState.ThreatGapFlatCap,
                (diff.bossThreat - avgRank) * MoraleState.ThreatGapFlat);
            Ui.Warn($"  ⚠ ボス{diff.BossThreatLabel}は確定戦闘"
                + $"（平均認定{Rank.Label(avgRank)}、遭遇時の士気 -{shock}）");
        }
        var overweight = members.Where(member => member.OverweightAmount > 0).ToList();
        foreach (var member in overweight)
            Ui.Warn($"  ⚠ {member.name} は過積載 {member.TotalEquipmentWeight}/{member.CarryLimit}"
                + $"（命中-{member.OverweightToHitPenalty} / DV-{member.OverweightDvPenalty}）");
        var injured = members.Where(a => a.IsInjured).ToList();
        if (injured.Count > 0)
            Ui.Warn($"  ⚠ 負傷者を編成中: {string.Join("、", injured.Select(a => a.name))}（負傷補正を含む戦力です）");

        // 戦力の警告をひとまとめに読ませてから、戦闘の外の話へ移る。
        ShowExpeditionEffects(formation, def);
    }

    /// <summary>
    /// 戦闘の外に効くスキルの合算を、受注前に見せる。
    ///
    /// 報酬や行軍速度を動かすスキルは戦闘画面にも冒険者の実戦値にも出てこないので、
    /// 「誰を連れて行くと何が変わるのか」はここで見せないとプレイヤーに届かない。
    /// 効果ゼロのときも行を残すのは、この仕組み自体の存在を知らせるため。
    /// </summary>
    static void ShowExpeditionEffects(AdventurerData?[] formation, QuestMasterData def)
    {
        var effects = PartySkillEffects.Of(formation);

        Ui.WriteLine();
        Ui.Header("遠征補正（隊全体）");

        int phasesPerTurn = effects.PhasesPerTurnFor(def);
        int estTurns = effects.EstimatedTurnsFor(def);
        int baseTurns = PartySkillEffects.None.EstimatedTurnsFor(def);
        string march = $"  行軍: {phasesPerTurn}エリア/ターン"
            + $"（全{def.totalPhases}エリア → 所要目安 {estTurns}ターン）";
        if (effects.phasesPerTurnBonus != 0)
            Ui.Info(march + $"　※基本{def.phasesPerTurn}エリア{effects.phasesPerTurnBonus:+#;-#;0}"
                + $"／補正なしなら{baseTurns}ターン");
        else
            Ui.WriteLine(march);

        // 行軍以外はスキル一覧と同じ書式に揃える。同じ効果が画面ごとに違う名前で出ると読み解けない。
        var otherParts = EquipmentText.ExpeditionParts(new SkillExpeditionEffect
        {
            goldPercent = effects.goldPercent,
            expPercent = effects.expPercent,
            treasureChancePercent = effects.treasureChancePercent,
            trapChancePercent = effects.trapChancePercent,
            enemyEncounterChancePercent = effects.enemyEncounterChancePercent,
            healEventChancePercent = effects.healEventChancePercent,
            restHealPercent = effects.restHealPercent,
            enemyDropChancePercent = effects.enemyDropChancePercent,
            rareDropChancePercent = effects.rareDropChancePercent,
        });
        if (otherParts.Count > 0)
            Ui.WriteLine("  報酬・道中: " + string.Join("　", otherParts));

        var sources = new List<string>();
        var dormant = new List<string>();
        foreach (var member in formation)
        {
            if (member == null) continue;
            var live = new List<string>();
            foreach (var skill in member.Skills)
            {
                if (skill.expedition.IsEmpty) continue;
                if (UnitCalculator.MeetsGearRequirements(skill, member)) live.Add(skill.skillName);
                else dormant.Add($"{member.name}「{skill.skillName}」");
            }
            if (live.Count > 0) sources.Add($"{member.name}「{string.Join("」「", live)}」");
        }

        if (sources.Count > 0)
            Ui.Dim("    内訳: " + string.Join(" / ", sources));
        else
            Ui.Dim("    このスキルの持ち主がいません（交渉術・教導・幌馬車などは連れて行くだけで隊全体に効きます）");
        if (dormant.Count > 0)
            Ui.Warn($"  ⚠ 装備条件を満たさず効いていない遠征スキル: {string.Join("、", dormant)}");
    }

    static bool UsesRangedOrSupportWeapon(AdventurerData member)
    {
        var weapon = member.Weapon;
        if (weapon == null) return false;
        return weapon.attackKind != AttackKind.Physical
            || weapon.weaponType == WeaponType.Bow;
    }

    static QuestObjective DescribeObjective(QuestMasterData q)
    {
        if (q.IsGatherQuest)
        {
            string itemName = string.IsNullOrWhiteSpace(q.gatherItemName) ? "採取物" : q.gatherItemName;
            return new(
                "採取",
                $"{itemName}×{q.gatherTargetCount}",
                $"{itemName}×{q.gatherTargetCount}を採取");
        }

        if (q.BossEnemy != null)
        {
            var members = q.BossEnemy.Formation
                .Where(member => member != null)
                .Select(member => member!)
                .ToList();
            if (members.Count == 0)
            {
                string unitName = string.IsNullOrWhiteSpace(q.BossEnemy.unitName)
                    ? "指定された敵"
                    : q.BossEnemy.unitName;
                return new("討伐", unitName, $"{unitName}を討伐");
            }

            var targetGroups = members
                .GroupBy(member => member.id)
                .Select(group => new
                {
                    Name = string.IsNullOrWhiteSpace(group.First().baseName)
                        ? "名称不明の敵"
                        : group.First().baseName,
                    Count = group.Count(),
                })
                .ToList();
            string targets = string.Join("、", targetGroups.Select(target => $"{target.Name}×{target.Count}"));
            return new(
                "討伐",
                $"{targets}（計{members.Count}体）",
                $"{targets}（合計{members.Count}体）を討伐");
        }

        string location = q.Dungeon?.dungeonName ?? "目的地";
        return new(
            "踏破",
            $"{location}・{q.totalPhases}エリア",
            $"{location}を{q.totalPhases}エリア踏破");
    }

    static string DifficultyLabel(DungeonDifficulty.Rating difficulty)
        => $"{difficulty.label}（5段階中{difficulty.level}）";
}
