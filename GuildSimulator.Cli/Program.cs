using GuildSimulator.Cli.Data;
using GuildSimulator.Cli.Screens;
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

// ---- ゲームループ（リスタート対応）----
while (true)
{
    bool restart = RunGame(db);
    if (!restart) break;
    Console.WriteLine("\nゲームをリスタートします...");
    Console.WriteLine();
}

static bool RunGame(GameMasterData db)
{
    // ---- 初期化 ----
    var guild = new GuildManager(startGold: 200, startRank: 1);
    var questManager = new QuestManager(guild);

    int currentTurn = 1;
    questManager.FillBoard(db.allQuests, currentTurn);

    const int CandidateCount = 3;
    var recruitCandidates = DrawCandidates(db.allAdventurers, guild, CandidateCount);

    // ---- メインループ ----
    while (true)
    {
        ConsoleHelper.Header($"ギルドシミュレーター  Turn {currentTurn}");
        int upkeepPerTurn = guild.adventurers.Where(a => a.isAlive).Sum(a => a.master.upkeepGold);
        Console.WriteLine($"  Gold: {guild.Gold}G（維持費 {upkeepPerTurn}G/T）   ギルドランク: {guild.GuildRank}   GP: {guild.GuildPoints}");
        Console.WriteLine($"  冒険者: {guild.adventurers.Count}人   進行中クエスト: {questManager.activeQuests.Count}件");
        Console.WriteLine($"  クエストボード: {questManager.questBoard.Count}/{questManager.BoardCapacity}枚   遺物: {guild.relics.Count}個");
        Console.WriteLine($"  雇入れ候補: {recruitCandidates.Count}人（ターン終了で入れ替わり）");
        Console.WriteLine();
        Console.WriteLine("  1. ターンを進める");
        Console.WriteLine("  2. クエストボード");
        Console.WriteLine("  3. 進行中クエスト");
        Console.WriteLine("  4. 冒険者一覧");
        Console.WriteLine("  5. 冒険者を雇う");
        Console.WriteLine("  6. 経済ログ");
        Console.WriteLine("  7. 遺物一覧");
        Console.WriteLine("  0. 終了");
        Console.Write("\n選択: ");

        var input = Console.ReadLine();
        if (input == null) return false;   // 標準入力が閉じられた（パイプ実行など）

        switch (input.Trim())
        {
            case "1":
                NextTurn(guild, questManager, ref currentTurn);
                questManager.RefreshBoard(db.allQuests, currentTurn);
                recruitCandidates = DrawCandidates(db.allAdventurers, guild, CandidateCount);
                // 報酬を先に受け取ってから破産チェック（維持費と報酬が同ターンに確定するため）。
                ShowQuestsNeedingAttention(questManager, guild);
                if (guild.Gold <= 0)
                    return ShowGameOver(currentTurn);
                break;
            case "2": QuestBoardScreen.Show(questManager, guild, currentTurn); break;
            case "3": ActiveQuestScreen.Show(questManager, guild); break;
            case "4": AdventurerScreen.Show(guild, questManager); break;
            case "5": RecruitScreen.Show(recruitCandidates, guild, currentTurn); break;
            case "6": ShowEconomyLog(guild); break;
            case "7": RelicScreen.Show(guild); break;
            case "0": Console.WriteLine("ゲーム終了"); return false;
        }
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

static List<AdventurerMasterData> DrawCandidates(
    List<AdventurerMasterData> pool,
    GuildManager guild,
    int count)
{
    // 雇入れ候補はギルドランク+1まで。ただし格上（ランク+1）は最大1枠までに抑える。
    // そうしないと高ランク・高額な候補ばかり並び、資金的にどれも雇えない回が続いてしまう。
    var unhired = pool.Where(m => !guild.adventurers.Any(a => a.master == m)).ToList();

    var sameOrBelow = unhired
        .Where(m => Math.Max(1, m.defaultRank) <= guild.GuildRank)
        .OrderBy(_ => Guid.NewGuid())
        .ToList();
    var oneRankAbove = unhired
        .Where(m => Math.Max(1, m.defaultRank) == guild.GuildRank + 1)
        .OrderBy(_ => Guid.NewGuid())
        .ToList();

    var picked = sameOrBelow.Take(count - 1).ToList();
    picked.AddRange(oneRankAbove.Take(count - picked.Count));
    if (picked.Count < count)
        picked.AddRange(sameOrBelow.Skip(picked.Count).Take(count - picked.Count));

    return picked.OrderBy(_ => Guid.NewGuid()).Take(count).ToList();
}

// ターン進行で完了・失敗が確定したクエストをその場で処理させる。
// これが無いと「進行中クエスト」画面を毎ターン自発的に覗かない限りクリアに気づけない。
static void ShowQuestsNeedingAttention(QuestManager qm, GuildManager guild)
{
    var needAttention = qm.activeQuests.Where(q => q.failed || q.CanComplete).ToList();
    if (needAttention.Count == 0) return;

    ConsoleHelper.Header("結果報告");
    Console.WriteLine($"  {needAttention.Count}件のクエストが結果待ちです");
    ConsoleHelper.PressAnyKey();

    foreach (var q in needAttention)
        ActiveQuestScreen.HandleQuest(q, qm, guild);
}

static void NextTurn(GuildManager guild, QuestManager questManager, ref int currentTurn)
{
    currentTurn++;
    questManager.AdvanceAll(currentTurn);
    guild.PayUpkeepForAll(currentTurn);
    ConsoleHelper.Info($"Turn {currentTurn} が始まりました");
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
