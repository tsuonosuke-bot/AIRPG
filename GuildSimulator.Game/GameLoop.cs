using GuildSimulator.Core;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Game.Data;
using GuildSimulator.Game.Screens;
using GuildSimulator.Game.Presentation;

namespace GuildSimulator.Game;

/// <summary>
/// ゲーム本体のループ。入出力は <see cref="Ui"/>、セーブ先は <see cref="ISaveStore"/> に委ねるため、
/// コンソール版・ブラウザ版のどちらからも同じコードで動く。
/// </summary>
public static class GameLoop
{
    const int MaxCandidateCount = 3;
    const int StartingGold = 300;

    /// <summary>リスタートを含めてゲームが終了するまで回す。</summary>
    public static async Task RunAsync(GameMasterData db, ISaveStore saveStore)
    {
        if (await saveStore.ExistsAsync())
            Ui.Info($"セーブデータが見つかりました（{saveStore.Description}）。メニューの「L」でロードできます。");

        while (true)
        {
            bool restart = await RunGameAsync(db, saveStore);
            if (!restart) break;
            Ui.WriteLine();
            Ui.WriteLine("ゲームをリスタートします...");
            Ui.WriteLine();
        }
    }

    static async Task<bool> RunGameAsync(GameMasterData db, ISaveStore saveStore)
    {
        // ---- 初期化 ----
        var guild = new GuildManager(startGold: StartingGold, startRank: 1);
        var questManager = new QuestManager(guild) { traitCatalog = db.traits.Values.ToList() };

        int currentTurn = 1;
        questManager.FillBoard(db.allQuests, currentTurn);

        var recruitCandidates = RecruitmentSystem.DrawCandidates(db.allAdventurers, guild, MaxCandidateCount);

        // ---- メインループ ----
        while (true)
        {
            ShopService.RefreshIfNeeded(guild, currentTurn, db.equipment.Values, db.consumables.Values);
            Ui.BeginScreen();
            Ui.Header($"ギルドシミュレーター  Turn {currentTurn}");
            int upkeepPerTurn = guild.EffectiveUpkeepPerTurn;
            int injuredCount = guild.adventurers.Count(a => a.isAlive && a.IsInjured);
            // スマホ幅で1行に情報を詰め込むと項目名の途中で折り返されるため、
            // 経済・進行・資産を短い行へ分ける。
            Ui.WriteLine($"  所持金: {guild.Gold}G（維持費 {upkeepPerTurn}G/T）");
            Ui.WriteLine($"  ギルドランク: {guild.GuildRankLabel}   ギルドポイント: {guild.GuildPoints}");
            Ui.WriteLine($"  冒険者: {guild.adventurers.Count}人"
                + (injuredCount > 0 ? $"（負傷 {injuredCount}人）" : "")
                + $"   進行中クエスト: {questManager.activeQuests.Count}件");
            // 遺物システムの凍結中は所持数の行ごと出さない（復活すれば自動で戻る）。
            Ui.WriteLine(
                (GameFeatures.RelicsEnabled ? $"  遺物: {guild.relics.Count}個   施設" : "  施設")
                + $": {guild.facilities.Count}件   雇入れ候補: {recruitCandidates.Count}人");
            ShowPromotionProgress(db.allQuests, guild, questManager);
            ShowEconomyForecast(guild, upkeepPerTurn);
            Ui.WriteLine();

            int projectedAfterUpkeep = guild.Gold - upkeepPerTurn;
            int pendingDecisionCount = questManager.activeQuests.Count(NeedsDecision);
            var menu = MainMenuBuilder.BuildMain(
                currentTurn,
                upkeepPerTurn,
                projectedAfterUpkeep,
                pendingDecisionCount,
                GameFeatures.RelicsEnabled);

            string input = await Ui.SelectAsync("選択", menu);

            switch (input.Trim().ToUpperInvariant())
            {
                case "1": await QuestBoardScreen.ShowAsync(questManager, guild, currentTurn); break;
                case "2": await ActiveQuestScreen.ShowAsync(questManager, guild); break;
                case "3": await AdventurerScreen.ShowAsync(db, guild, questManager, currentTurn); break;
                case "4":
                    await RecruitScreen.ShowAsync(
                        recruitCandidates, guild, currentTurn, db.allAdventurers, MaxCandidateCount);
                    break;
                case "5": await InventoryScreen.ShowAsync(guild); break;
                case "6": await ShopScreen.ShowAsync(db, guild, currentTurn); break;
                case "7":
                    if (GameFeatures.RelicsEnabled) await RelicScreen.ShowAsync(guild);
                    break;
                case "F": await FacilityScreen.ShowAsync(db, guild); break;
                case "G":
                    await ShowGuildManagementMenuAsync(db, guild, questManager);
                    break;
                case "9":
                    if (pendingDecisionCount > 0)
                    {
                        Ui.Warn("指示待ちのクエストがあります。すべて決定するまで次のターンへ進めません");
                        await ShowPendingChoicesAsync(questManager, guild);
                        break;
                    }
                    if (projectedAfterUpkeep <= 0
                        && !await Ui.ConfirmAsync(
                            $"次の維持費支払い後は {projectedAfterUpkeep}Gです。完了報酬がなければ破産します。ターンを進めますか？"))
                        break;
                    NextTurn(guild, questManager, ref currentTurn);
                    int recruitMin = FacilitySystem.GetRecruitMinBonus();
                    int recruitCount = GameRandom.Range(recruitMin, MaxCandidateCount + 1);
                    recruitCandidates = RecruitmentSystem.DrawCandidates(db.allAdventurers, guild, recruitCount);
                    // 報酬でGP条件を達成したターンに、昇格試験をすぐ掲示できる順序にする。
                    await ShowQuestsNeedingAttentionAsync(questManager, guild);
                    questManager.RefreshBoard(db.allQuests, currentTurn);
                    if (guild.Gold <= 0)
                        return await ShowGameOverAsync(currentTurn);
                    break;
                case "0":
                    if (!await Ui.ConfirmAsync("ゲームを終了しますか？セーブしていない進行状況は失われます。"))
                        break;
                    Ui.WriteLine("ゲーム終了");
                    return false;
                case "S":
                    await DoSaveAsync(saveStore, guild, questManager, currentTurn, recruitCandidates);
                    break;
                case "L":
                    var loaded = await DoLoadAsync(saveStore, db);
                    if (loaded != null)
                    {
                        guild = loaded.Guild;
                        questManager = loaded.QuestManager;
                        currentTurn = loaded.CurrentTurn;
                        recruitCandidates = loaded.RecruitCandidates;
                    }
                    break;
            }
        }
    }

    static async Task ShowGuildManagementMenuAsync(
        GameMasterData db,
        GuildManager guild,
        QuestManager questManager)
    {
        while (true)
        {
            Ui.BeginScreen();
            Ui.Header("ギルド管理");
            string input = await Ui.SelectAsync("選択", MainMenuBuilder.BuildGuildManagement());

            switch (input.Trim().ToUpperInvariant())
            {
                case "8": await ShowEconomyLogAsync(guild); break;
                case "B": await BurialScreen.ShowAsync(guild); break;
                case "J": await StoryJournalScreen.ShowAsync(db, questManager); break;
                case "M": await MonsterGuideScreen.ShowAsync(db, guild); break;
                case "T": await BattleSimScreen.ShowAsync(db, guild); break;
                case "H": await HelpScreen.ShowAsync(db); break;
                case "0": return;
            }
        }
    }

    static async Task DoSaveAsync(
        ISaveStore saveStore, GuildManager guild, QuestManager questManager,
        int currentTurn, List<AdventurerMasterData> recruitCandidates)
    {
        try
        {
            string json = SaveManager.Serialize(guild, questManager, currentTurn, recruitCandidates);
            await saveStore.WriteAsync(json);
            Ui.Info($"セーブしました（Turn {currentTurn}）");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error($"セーブに失敗しました: {ex.Message}");
        }
        await Ui.PauseAsync();
    }

    static async Task<LoadedGame?> DoLoadAsync(ISaveStore saveStore, GameMasterData db)
    {
        if (!await saveStore.ExistsAsync())
        {
            Ui.Error("セーブデータが見つかりません");
            await Ui.PauseAsync();
            return null;
        }
        if (!await Ui.ConfirmAsync("現在の進行状況を破棄してロードします。よろしいですか？"))
            return null;

        try
        {
            string? json = await saveStore.ReadAsync();
            if (json == null)
            {
                Ui.Error("セーブデータが見つかりません");
                await Ui.PauseAsync();
                return null;
            }
            var loaded = SaveManager.Deserialize(json, db);
            Ui.Info($"ロードしました（Turn {loaded.CurrentTurn}）");
            await Ui.PauseAsync();
            return loaded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            Ui.Error($"ロードに失敗しました: {ex.Message}");
            await Ui.PauseAsync();
            return null;
        }
    }

    static async Task<bool> ShowGameOverAsync(int turn)
    {
        Ui.WriteLine();
        Ui.Error("═══════════════════════════════════");
        Ui.Error($"  GAME OVER  （Turn {turn} にて破産）");
        Ui.Error("═══════════════════════════════════");
        Ui.WriteLine();
        string choice = await Ui.SelectAsync("選択", new[]
        {
            new MenuOption("1", "リスタート"),
            new MenuOption("0", "終了", Style: TextStyle.Dim),
        });
        return choice == "1";
    }

    // ターン進行で完了・失敗が確定したクエストをその場で処理させる。
    // これが無いと「進行中クエスト」画面を毎ターン自発的に覗かない限りクリアに気づけない。
    static async Task ShowQuestsNeedingAttentionAsync(QuestManager qm, GuildManager guild)
    {
        var needAttention = qm.activeQuests
            .Where(q => q.failed || q.CanComplete || q.HasPendingChoice || q.HasGatherDecision)
            .ToList();
        if (needAttention.Count == 0) return;

        Ui.Header("クエスト確認");
        Ui.WriteLine($"  {needAttention.Count}件のクエストに確認・指示が必要です");
        await Ui.PauseAsync();

        foreach (var q in needAttention)
            await ActiveQuestScreen.HandleQuestAsync(q, qm, guild);
    }

    static async Task ShowPendingChoicesAsync(QuestManager qm, GuildManager guild)
    {
        foreach (var q in qm.activeQuests.Where(NeedsDecision).ToList())
            await ActiveQuestScreen.HandleQuestAsync(q, qm, guild);
    }

    static bool NeedsDecision(QuestRun quest) =>
        quest.HasPendingChoice || quest.HasGatherDecision;

    static void NextTurn(GuildManager guild, QuestManager questManager, ref int currentTurn)
    {
        currentTurn++;
        int summaryTurn = currentTurn;
        var snapshots = questManager.activeQuests.ToDictionary(
            q => q,
            q => (Phase: q.currentPhase, Hp: q.unitHpCurrent, Morale: q.morale.Current, LogCount: q.logs.Count));

        questManager.AdvanceAll(currentTurn);
        var recoveryMessages = guild.AdvanceRecovery(
            currentTurn,
            adventurer => !questManager.IsAdventurerBusy(adventurer.id));
        guild.PayUpkeepForAll(currentTurn);
        Ui.Info($"Turn {currentTurn} が始まりました");
        foreach (var message in recoveryMessages)
            Ui.Info(message);

        if (snapshots.Count == 0) return;

        Ui.Header("ターン進行サマリー");
        foreach (var q in questManager.activeQuests)
        {
            if (!snapshots.TryGetValue(q, out var before)) continue;

            string status = q.failed ? "全員戦闘不能"
                : q.retreated ? "撤退"
                : q.CanComplete ? "完了可能"
                : "進行中";
            Ui.WriteLine($"  ◆ {q.def.questName}  {status}");
            Ui.WriteLine($"      エリア {before.Phase} → {q.currentPhase}/{q.def.totalPhases}"
                + $"   HP {before.Hp} → {q.unitHpCurrent}/{q.unitHpMax}"
                + $"   士気 {before.Morale} → {q.morale.Current}/{q.morale.Max}");

            var eventSummaries = q.logs
                .Skip(before.LogCount)
                .Where(log => log.StartsWith($"[Turn {summaryTurn}] エリア ") && log.Contains('/'))
                .TakeLast(3)
                .ToList();
            foreach (var log in eventSummaries)
                Ui.WriteQuestLog($"      {log}");
        }
    }

    static void ShowPromotionProgress(
        IEnumerable<QuestMasterData> allQuests,
        GuildManager guild,
        QuestManager questManager)
    {
        var promotion = allQuests
            .Where(q => q.isEmergencyQuest
                && q.rank == guild.GuildRank
                && q.requiredGuildPoints > 0)
            .OrderBy(q => q.requiredGuildPoints)
            .FirstOrDefault();
        if (promotion == null) return;

        bool isPosted = questManager.questBoard.Any(e => e.quest == promotion);
        bool isActive = questManager.activeQuests.Any(q => q.def == promotion);
        if (isPosted || isActive) return;

        int remaining = Math.Max(0, promotion.requiredGuildPoints - guild.GuildPoints);
        Ui.Dim($"  昇格試験解禁まで: ギルドポイント {guild.GuildPoints}/{promotion.requiredGuildPoints}（あと{remaining}）");
    }

    static void ShowEconomyForecast(GuildManager guild, int upkeepPerTurn)
    {
        if (upkeepPerTurn <= 0) return;

        int afterUpkeep = guild.Gold - upkeepPerTurn;
        int safeTurns = GuildManager.SafeUpkeepTurns(guild.Gold, upkeepPerTurn);
        string runway = safeTurns == int.MaxValue ? "∞" : safeTurns.ToString();

        if (afterUpkeep <= 0)
            Ui.Warn($"  ⚠ 次回の維持費支払い後は {afterUpkeep}G。クエスト報酬がなければ破産します");
        else if (safeTurns <= 2)
            Ui.Warn($"  ⚠ 資金猶予 {runway}T（次回維持費後 {afterUpkeep}G・報酬収入を除く）");
        else
            Ui.Dim($"  資金猶予: 約{runway}T（報酬収入を除く）");
    }

    static async Task ShowEconomyLogAsync(GuildManager guild)
    {
        Ui.BeginScreen();
        Ui.Header("経済ログ");
        var logs = guild.economyLogs;
        int start = Math.Max(0, logs.Count - 30);
        for (int i = start; i < logs.Count; i++)
            Ui.Dim($"  {logs[i]}");
        await Ui.PauseAsync();
    }
}
