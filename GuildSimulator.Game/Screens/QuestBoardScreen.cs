using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Game.Presentation;

namespace GuildSimulator.Game.Screens;

public static class QuestBoardScreen
{
    public static async Task ShowAsync(QuestManager questManager, GuildManager guild, int currentTurn)
    {
        while (true)
        {
            Ui.BeginScreen();
            Ui.Header("クエストボード");
            var board = questManager.questBoard;
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
                var detail = new List<string>();
                if (!string.IsNullOrWhiteSpace(q.clientName))
                    detail.Add($"依頼人: {q.clientName}");
                if (!string.IsNullOrWhiteSpace(q.description))
                    detail.Add(q.description);
                detail.Add($"難易度 {diff.label}（スコア{diff.score:0}）  基本報酬 資金:{q.rewardGold}G 経験値:{q.rewardExp} ギルドポイント:{q.rewardGuildPoints}");
                int estimatedUpkeep = guild.EffectiveUpkeepPerTurn * estTurns;
                int estimatedNet = guild.EstimateNetAfterUpkeep(q.rewardGold, estTurns);
                string netText = $"予想収支: 基本報酬{q.rewardGold}G - 維持費{estimatedUpkeep}G = {estimatedNet:+#;-#;0}G（概算）";
                detail.Add(estimatedNet < 0 ? $"⚠ {netText}" : netText);
                detail.Add("追加収入: 宝箱・敵ドロップ・選択イベントは上の概算に含みません（結果により大きく変動）");
                if (q.IsGatherQuest)
                    detail.Add($"採取: {q.gatherItemName} x{q.gatherTargetCount}"
                        + $"（目標超過1個につき +{q.gatherGoldPerItem}G / 必要数を集めた時点で帰還"
                        + $" / {q.totalPhases}フェーズで足りなければ延長か撤退を選ぶ）");
                string bossInfo = diff.hasBoss ? $"  ボス:脅威度{diff.BossThreatLabel}" : "";
                detail.Add($"場所: {q.Dungeon?.dungeonName ?? "？"}  敵の脅威度{diff.EnemyThreatRange}"
                    + $"  戦闘{diff.combatChance * 100:0}% 罠{diff.trapChance * 100:0}%{bossInfo}");
                // 習熟度は適正ランクのクエストでしか増えない。誰を出せば伸びるのかを受注前に見せる。
                int suitableCount = availableAdvs.Count(a => a.IsSuitableQuestRank(q.rank));
                detail.Add($"習熟度: ランク{Rank.SuitableAdventurerRangeLabel(q.rank)}の冒険者に入る"
                    + $"（待機中 {suitableCount}/{availableAdvs.Count}人が該当）");
                detail.Add($"掲示期限: あと{e.RemainingTurns(currentTurn, questManager.BoardExpireTurns)}ターン");

                entries.Add(new MenuOption(
                    (i + 1).ToString(),
                    $"【{Rank.Label(q.rank)}】{q.questName}  所要:{estTurns}T{emg}{story}",
                    string.Join(Environment.NewLine, detail),
                    q.isEmergencyQuest ? TextStyle.Warn : TextStyle.Normal));
            }

            int? sel = await Ui.SelectIndexAsync("受注するクエスト", entries);
            if (sel == null) return;
            await SelectAndStartAsync(board[sel.Value - 1].quest, questManager, guild, currentTurn);
        }
    }

    static async Task SelectAndStartAsync(
        QuestMasterData def, QuestManager qm, GuildManager guild, int currentTurn)
    {
        var formation = new AdventurerData?[6];
        var advs = guild.adventurers;

        while (formation.Any(x => x == null))
        {
            // 配置を1人確定するたびに画面を描き直す。Web版で変更前と変更後の
            // 「現在の編成」が同じ画面に積み重ならないようにする。
            Ui.BeginScreen();
            Ui.Header($"編成: {def.questName}");
            Ui.WriteLine("冒険者を選び、次に配置先を指定してください");
            Ui.WriteLine();
            ShowFormation(formation);

            var available = advs.Where((a, i) =>
                a.isAlive &&
                !qm.IsAdventurerBusy(a.id) &&
                !formation.Contains(a)).ToList();
            if (available.Count == 0)
            {
                Ui.Dim("  配置可能な冒険者をすべて編成しました");
                break;
            }

            Ui.WriteLine();

            var memberOptions = new List<MenuOption>();
            for (int i = 0; i < available.Count; i++)
            {
                var a = available[i];
                memberOptions.Add(new MenuOption(
                    (i + 1).ToString(),
                    $"{a.name} Lv{a.level}" + (a.IsInjured ? $" [負傷{a.injuries.Count}]" : ""),
                    a.ClassAndRace + (a.IsInjured ? $" / {a.ConditionSummary}" : ""),
                    Ui.RarityStyle(a.master.rarity)));
            }

            int? pick = await Ui.SelectIndexAsync("追加する冒険者", memberOptions, "編成を確定");
            if (pick == null) break;

            var openSlots = Enumerable.Range(0, formation.Length)
                .Where(slot => formation[slot] == null)
                .ToList();
            var slotOptions = openSlots
                .Select((slot, i) => new MenuOption((i + 1).ToString(), PositionName(slot)))
                .ToList();

            int? slotPick = await Ui.SelectIndexAsync(
                $"{available[pick.Value - 1].name} の配置先", slotOptions, "配置をやめる");
            if (slotPick == null)
            {
                Ui.Warn("配置をキャンセルしました");
                continue;
            }
            formation[openSlots[slotPick.Value - 1]] = available[pick.Value - 1];
        }

        int count = formation.Count(x => x != null);
        if (count == 0) { Ui.Warn("編成が空のためキャンセル"); return; }

        Ui.BeginScreen();
        Ui.Header("編成確認");
        ShowFormation(formation);
        ShowPartyPreview(formation, def);
        var policy = await SelectPolicyAsync();
        if (policy == null) return;
        var carriedConsumables = await SelectConsumablesAsync(guild, formation);
        Ui.WriteLine($"  遠征方針: {QuestManager.PolicyName(policy.Value)}");
        if (carriedConsumables.Count > 0)
            Ui.WriteLine($"  持ち込み（出発時消費）: {string.Join(", ", carriedConsumables.Select(x => x.DisplayName))}");
        if (!await Ui.ConfirmAsync("このメンバーで受注しますか？")) return;

        if (qm.TryStartQuestWithConsumables(
            def, formation, currentTurn, out var error, carriedConsumables, policy.Value))
            Ui.Info($"クエスト「{def.questName}」を受注しました！ （Turn {currentTurn} 開始）");
        else
            Ui.Error($"受注失敗: {error}");

        await Ui.PauseAsync();
    }

    static async Task<ExpeditionPolicy?> SelectPolicyAsync()
    {
        Ui.WriteLine();
        string key = await Ui.SelectAsync("遠征方針", new[]
        {
            new MenuOption("1", "生還優先", "損耗（HP）が危険域へ入る前に撤退する"),
            new MenuOption("2", "依頼達成優先", "行動可能な限り任務を続行する"),
            new MenuOption("0", "受注をやめる", Style: TextStyle.Dim),
        });
        return key switch
        {
            "1" => ExpeditionPolicy.SurvivalFirst,
            "2" => ExpeditionPolicy.ObjectiveFirst,
            _ => null,
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

    static void ShowFormation(AdventurerData?[] formation)
    {
        Ui.WriteLine("  現在の編成:");
        for (int i = 0; i < formation.Length; i++)
        {
            Ui.Write($"    {PositionName(i),-4}: ");
            if (formation[i] != null)
                Ui.WriteRarityName(formation[i]!.name, formation[i]!.master.rarity);
            else
                Ui.Write("空");
            Ui.WriteLine();
        }
    }

    static string PositionName(int slot) => slot < 3 ? $"前衛{slot + 1}" : $"後衛{slot - 2}";

    static void ShowPartyPreview(AdventurerData?[] formation, QuestMasterData def)
    {
        var members = formation.Where(a => a != null).Select(a => a!).ToList();
        if (members.Count == 0) return;

        var perMember = UnitCalculator.CalcPerMember(
            formation.Cast<IUnitMember?>().ToArray(), isAllySide: true);
        int totalHp = perMember.Sum(x => x.stats.hp);
        int totalMorale = perMember.Sum(x => x.stats.san);
        int avgLevel = (int)Math.Round(members.Average(a => a.level));

        var diff = DungeonDifficulty.Evaluate(def);

        Ui.WriteLine();
        Ui.Header("パーティ戦力");
        Ui.WriteLine($"  平均レベル: {avgLevel}   合計HP: {totalHp}   推定士気: {totalMorale}");
        Ui.WriteLine($"  クエスト難易度: {diff.label}（スコア{diff.score:0}）  敵の脅威度: {diff.EnemyThreatRange}");
        if (diff.hasBoss)
            Ui.WriteLine($"  ボス: 脅威度{diff.BossThreatLabel}");
        // 士気の格上ショックは「敵の脅威度 － 味方の認定ランク」で決まる。編成前に気づけるようにする。
        int avgRank = (int)Math.Round(members.Average(a => (double)a.rank));
        if (avgRank < diff.enemyThreatMax)
            Ui.Warn($"  ⚠ 敵の脅威度({Rank.Label(diff.enemyThreatMax)})がパーティの平均ランク({Rank.Label(avgRank)})を上回っています"
                + "（遭遇時に士気を削られます）");
        var injured = members.Where(a => a.IsInjured).ToList();
        if (injured.Count > 0)
            Ui.Warn($"  ⚠ 負傷者を編成中: {string.Join("、", injured.Select(a => a.name))}（負傷補正を含む戦力です）");
    }
}
