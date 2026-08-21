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
                    + $"\n基本報酬 資金:{q.rewardGold}G 経験値:{q.rewardExp} ギルドポイント:{q.rewardGuildPoints}"
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
        Ui.WriteLine($"  基本報酬 資金:{q.rewardGold}G 経験値:{q.rewardExp} ギルドポイント:{q.rewardGuildPoints}");
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
        var advs = guild.adventurers;
        int partyCapacity = guild.PartyCapacity;

        while (formation.Any(x => x == null)
            && formation.Count(member => member != null) < partyCapacity)
        {
            // 配置を1人確定するたびに画面を描き直す。Web版で変更前と変更後の
            // 「現在の編成」が同じ画面に積み重ならないようにする。
            Ui.BeginScreen();
            Ui.Header($"編成: {def.questName}");
            Ui.WriteLine("冒険者を選び、次に配置先を指定してください");
            Ui.WriteLine();
            ShowFormation(formation, partyCapacity);

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
        ShowFormation(formation, partyCapacity);
        ShowPartyPreview(formation, def, partyCapacity);
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
            new MenuOption("1", "生還優先",
                $"パーティHP{BattleResolver.SurvivalPartyHpPercent}%以下、または誰かが{BattleResolver.SurvivalMemberHpPercent}%以下で撤退する"),
            new MenuOption("2", "依頼達成優先",
                "行動可能な限り任務を続行する。戦闘不能者が出るほど帰還時の死亡リスクが高まる"),
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
