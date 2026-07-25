using GuildSimulator.Cli.Data;
using GuildSimulator.Cli.Screens;
using GuildSimulator.Core;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;

// 入出力がリダイレクトされている環境ではエンコーディング設定が失敗しうるので握りつぶす。
try
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    if (!Console.IsInputRedirected) Console.InputEncoding = System.Text.Encoding.UTF8;
}
catch (IOException) { }

// ---- データロード ----
string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
if (!Directory.Exists(dataDir))
    dataDir = Path.Combine(Directory.GetCurrentDirectory(), "Data");

Console.WriteLine("データ読み込み中...");
var db = MasterLoader.Load(dataDir);

if (args.Contains("--validate-master", StringComparer.OrdinalIgnoreCase))
{
    var errors = MasterValidator.Validate(db);
    if (errors.Count == 0)
    {
        Console.WriteLine("マスタデータ検証: OK");
        Console.WriteLine($"  冒険者 {db.allAdventurers.Count} / 装備 {db.equipment.Count}"
            + $" / 敵 {db.enemies.Count} / 消費アイテム {db.consumables.Count}"
            + $" / 選択イベント {db.choiceEvents.Count}");
    }
    else
    {
        Console.WriteLine($"マスタデータ検証: {errors.Count}件のエラー");
        foreach (var error in errors) Console.WriteLine($"  - {error}");
        Environment.ExitCode = 1;
    }
    return;
}

string savePath = SaveManager.DefaultSavePath;
if (SaveManager.Exists(savePath))
    Console.WriteLine($"セーブデータが見つかりました（{savePath}）。メニューの「l」でロードできます。");

// ---- ゲームループ（リスタート対応）----
while (true)
{
    bool restart = RunGame(db, savePath);
    if (!restart) break;
    Console.WriteLine("\nゲームをリスタートします...");
    Console.WriteLine();
}

static bool RunGame(GameMasterData db, string savePath)
{
    // ---- 初期化 ----
    var guild = new GuildManager(startGold: 200, startRank: 1);
    var questManager = new QuestManager(guild);

    int currentTurn = 1;
    questManager.FillBoard(db.allQuests, currentTurn);

    const int MaxCandidateCount = 3;
    var recruitCandidates = RecruitmentSystem.DrawCandidates(db.allAdventurers, guild, MaxCandidateCount);

    // ---- メインループ ----
    while (true)
    {
        ShopService.RefreshIfNeeded(guild, currentTurn, db.equipment.Values, db.consumables.Values);
        ConsoleHelper.Header($"ギルドシミュレーター  Turn {currentTurn}");
        int upkeepPerTurn = guild.EffectiveUpkeepPerTurn;
        Console.WriteLine($"  所持金: {guild.Gold}G（維持費 {upkeepPerTurn}G/T）   ギルドランク: {guild.GuildRank}   ギルドポイント: {guild.GuildPoints}");
        Console.WriteLine($"  冒険者: {guild.adventurers.Count}人   進行中クエスト: {questManager.activeQuests.Count}件   遺物: {guild.relics.Count}個");
        Console.WriteLine($"  雇入れ候補: {recruitCandidates.Count}人");
        ShowPromotionProgress(db.allQuests, guild, questManager);
        ShowEconomyForecast(guild, upkeepPerTurn);
        Console.WriteLine();
        Console.WriteLine("  【クエスト】");
        Console.WriteLine("    1. クエストボード");
        Console.WriteLine("    2. 進行中クエスト");
        Console.WriteLine();
        Console.WriteLine("  【冒険者】");
        Console.WriteLine("    3. 冒険者一覧・装備管理");
        Console.WriteLine("    4. 冒険者を雇う");
        Console.WriteLine();
        Console.WriteLine("  【ギルド資産】");
        Console.WriteLine("    5. 倉庫インベントリ");
        Console.WriteLine("    6. 商店（装備・消費アイテム）");
        Console.WriteLine("    7. 遺物一覧");
        Console.WriteLine();
        Console.WriteLine("  【ギルド管理】");
        Console.WriteLine("    8. 経済ログ");
        Console.WriteLine();
        Console.WriteLine("  【ターン操作】");
        Console.WriteLine("    9. ターンを進める");
        Console.WriteLine("    0. ゲーム終了");
        Console.WriteLine();
        Console.WriteLine("    H. ヘルプ・用語集");
        Console.WriteLine();
        Console.WriteLine("  【セーブデータ】");
        Console.WriteLine("    S. セーブする");
        Console.WriteLine("    L. ロードする");
        Console.Write("\n選択: ");

        var input = Console.ReadLine();
        if (input == null) return false;   // 標準入力が閉じられた（パイプ実行など）

        switch (input.Trim().ToUpperInvariant())
        {
            case "1": QuestBoardScreen.Show(questManager, guild, currentTurn); break;
            case "2": ActiveQuestScreen.Show(questManager, guild); break;
            case "3": AdventurerScreen.Show(guild, questManager); break;
            case "4": RecruitScreen.Show(recruitCandidates, guild, currentTurn); break;
            case "5": InventoryScreen.Show(guild); break;
            case "6": ShopScreen.Show(db, guild, currentTurn); break;
            case "7": RelicScreen.Show(guild); break;
            case "8": ShowEconomyLog(guild); break;
            case "H": HelpScreen.Show(); break;
            case "9":
                if (questManager.HasPendingChoices)
                {
                    ConsoleHelper.Warn("未解決の選択イベントがあります。すべて決定するまで次のターンへ進めません");
                    ShowPendingChoices(questManager, guild);
                    break;
                }
                NextTurn(guild, questManager, ref currentTurn);
                recruitCandidates = RecruitmentSystem.DrawCandidates(db.allAdventurers, guild, GameRandom.Range(0, MaxCandidateCount + 1));
                // 報酬でGP条件を達成したターンに、昇格試験をすぐ掲示できる順序にする。
                ShowQuestsNeedingAttention(questManager, guild);
                questManager.RefreshBoard(db.allQuests, currentTurn);
                if (guild.Gold <= 0)
                    return ShowGameOver(currentTurn);
                break;
            case "0": Console.WriteLine("ゲーム終了"); return false;
            case "S":
                DoSave(savePath, guild, questManager, currentTurn, recruitCandidates);
                break;
            case "L":
                var loaded = DoLoad(savePath, db);
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

static void DoSave(
    string savePath, GuildManager guild, QuestManager questManager,
    int currentTurn, List<AdventurerMasterData> recruitCandidates)
{
    try
    {
        SaveManager.Save(savePath, guild, questManager, currentTurn, recruitCandidates);
        ConsoleHelper.Info($"セーブしました（Turn {currentTurn}）");
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        ConsoleHelper.Error($"セーブに失敗しました: {ex.Message}");
    }
    ConsoleHelper.PressAnyKey();
}

static LoadedGame? DoLoad(string savePath, GameMasterData db)
{
    if (!SaveManager.Exists(savePath))
    {
        ConsoleHelper.Error("セーブデータが見つかりません");
        ConsoleHelper.PressAnyKey();
        return null;
    }
    if (!ConsoleHelper.Confirm("現在の進行状況を破棄してロードします。よろしいですか？"))
        return null;

    try
    {
        var loaded = SaveManager.Load(savePath, db);
        ConsoleHelper.Info($"ロードしました（Turn {loaded.CurrentTurn}）");
        ConsoleHelper.PressAnyKey();
        return loaded;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
    {
        ConsoleHelper.Error($"ロードに失敗しました: {ex.Message}");
        ConsoleHelper.PressAnyKey();
        return null;
    }
}

static bool ShowGameOver(int turn)
{
    Console.WriteLine();
    ConsoleHelper.Error("═══════════════════════════════════");
    ConsoleHelper.Error($"  GAME OVER  （Turn {turn} にて破産）");
    ConsoleHelper.Error("═══════════════════════════════════");
    Console.WriteLine();
    Console.WriteLine("  1. リスタート");
    Console.WriteLine("  0. 終了");
    Console.Write("選択: ");
    return Console.ReadLine()?.Trim() == "1";
}

// ターン進行で完了・失敗が確定したクエストをその場で処理させる。
// これが無いと「進行中クエスト」画面を毎ターン自発的に覗かない限りクリアに気づけない。
static void ShowQuestsNeedingAttention(QuestManager qm, GuildManager guild)
{
    var needAttention = qm.activeQuests.Where(q => q.failed || q.CanComplete || q.HasPendingChoice).ToList();
    if (needAttention.Count == 0) return;

    ConsoleHelper.Header("結果報告");
    Console.WriteLine($"  {needAttention.Count}件のクエストが結果待ちです");
    ConsoleHelper.PressAnyKey();

    foreach (var q in needAttention)
        ActiveQuestScreen.HandleQuest(q, qm, guild);
}

static void ShowPendingChoices(QuestManager qm, GuildManager guild)
{
    foreach (var q in qm.activeQuests.Where(q => q.HasPendingChoice).ToList())
        ActiveQuestScreen.HandleQuest(q, qm, guild);
}

static void NextTurn(GuildManager guild, QuestManager questManager, ref int currentTurn)
{
    currentTurn++;
    int summaryTurn = currentTurn;
    var snapshots = questManager.activeQuests.ToDictionary(
        q => q,
        q => (Phase: q.currentPhase, Hp: q.unitHpCurrent, Morale: q.morale.Current, LogCount: q.logs.Count));

    questManager.AdvanceAll(currentTurn);
    guild.PayUpkeepForAll(currentTurn);
    ConsoleHelper.Info($"Turn {currentTurn} が始まりました");

    if (snapshots.Count == 0) return;

    ConsoleHelper.Header("ターン進行サマリー");
    foreach (var q in questManager.activeQuests)
    {
        if (!snapshots.TryGetValue(q, out var before)) continue;

        string status = q.failed ? "全滅"
            : q.retreated ? "撤退"
            : q.CanComplete ? "完了可能"
            : "進行中";
        Console.WriteLine($"  ◆ {q.def.questName}  {status}");
        Console.WriteLine($"      Phase {before.Phase} → {q.currentPhase}/{q.def.totalPhases}"
            + $"   HP {before.Hp} → {q.unitHpCurrent}/{q.unitHpMax}"
            + $"   士気 {before.Morale} → {q.morale.Current}/{q.morale.Max}");

        var eventSummaries = q.logs
            .Skip(before.LogCount)
            .Where(log => log.StartsWith($"[Turn {summaryTurn}] Phase ") && log.Contains('/'))
            .TakeLast(3)
            .ToList();
        foreach (var log in eventSummaries)
            ConsoleHelper.WriteQuestLog($"      {log}");
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
    ConsoleHelper.Dim($"  昇格試験解禁まで: ギルドポイント {guild.GuildPoints}/{promotion.requiredGuildPoints}（あと{remaining}）");
}
static void ShowEconomyForecast(GuildManager guild, int upkeepPerTurn)

{
    if (upkeepPerTurn <= 0) return;

    int afterUpkeep = guild.Gold - upkeepPerTurn;
    int safeTurns = GuildManager.SafeUpkeepTurns(guild.Gold, upkeepPerTurn);
    string runway = safeTurns == int.MaxValue ? "∞" : safeTurns.ToString();

    if (afterUpkeep <= 0)
        ConsoleHelper.Warn($"  ⚠ 次回の維持費支払い後は {afterUpkeep}G。クエスト報酬がなければ破産します");
    else if (safeTurns <= 2)
        ConsoleHelper.Warn($"  ⚠ 資金猶予 {runway}T（次回維持費後 {afterUpkeep}G・報酬収入を除く）");
    else
        ConsoleHelper.Dim($"  資金猶予: 約{runway}T（報酬収入を除く）");
}

static void ShowEconomyLog(GuildManager guild)
{
    ConsoleHelper.Header("経済ログ");
    var logs = guild.economyLogs;
    int start = Math.Max(0, logs.Count - 30);
    for (int i = start; i < logs.Count; i++)
        ConsoleHelper.Dim($"  {logs[i]}");
    ConsoleHelper.PressAnyKey();
}
