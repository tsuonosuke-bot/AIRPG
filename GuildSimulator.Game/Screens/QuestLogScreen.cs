using System.Text.RegularExpressions;
using GuildSimulator.Core.GameData;
using GuildSimulator.Game.Presentation;

namespace GuildSimulator.Game.Screens;

/// <summary>クエストの詳細ログを、遠征ログと戦闘ごとのログへ分けて閲覧する画面。</summary>
internal static class QuestLogScreen
{
    const int LogPageSize = 10;
    const int BattleListPageSize = 8;

    static readonly Regex AreaPrefix = new(
        @"^\s*(?:エリア|Phase) \d+:\s*",
        RegexOptions.CultureInvariant);

    public static Task ShowAsync(QuestRun quest) =>
        ShowAsync(quest.def.questName, quest.logs, "クエスト詳細へ戻る");

    public static Task ShowAsync(QuestHistoryEntry history) =>
        ShowAsync(history.QuestName, history.Logs, "クエスト履歴へ戻る");

    static async Task ShowAsync(
        string questName,
        IReadOnlyList<string> logs,
        string returnLabel)
    {
        while (true)
        {
            var index = QuestLogIndexer.Build(logs);
            Ui.BeginScreen();
            Ui.Header($"詳細ログ: {questName}");
            Ui.Dim("  戦闘記録は一戦ごとに分けてあります");
            Ui.WriteLine();

            var options = new List<MenuOption>();
            if (index.ExpeditionLogs.Count > 0)
                options.Add(new MenuOption(
                    "e",
                    "遠征ログ",
                    $"戦闘以外の進行・採取・選択・報酬  {index.ExpeditionLogs.Count}件"));
            else
                Ui.Dim("  遠征ログ: なし");

            if (index.Battles.Count > 0)
                options.Add(new MenuOption(
                    "b",
                    "戦闘ログ",
                    $"戦闘を選んで見直す  {index.Battles.Count}戦",
                    TextStyle.Accent));
            else
                Ui.Dim("  戦闘ログ: なし");

            options.Add(new MenuOption("0", returnLabel, Style: TextStyle.Dim));
            string choice = await Ui.SelectAsync("ログ種別", options);
            if (choice == "e")
                await ShowExpeditionLogsAsync(questName, index.ExpeditionLogs);
            else if (choice == "b")
                await ShowBattleListAsync(questName, index.Battles);
            else
                return;
        }
    }

    static async Task ShowExpeditionLogsAsync(string questName, IReadOnlyList<string> logs)
    {
        int offset = 0; // 0 = 最新ページ。増えるほど過去へ遡る
        while (true)
        {
            int maxOffset = Math.Max(0, (logs.Count - 1) / LogPageSize * LogPageSize);
            offset = Math.Clamp(offset, 0, maxOffset);
            int skip = Math.Max(0, logs.Count - LogPageSize - offset);
            int take = Math.Min(LogPageSize, logs.Count - skip);

            Ui.BeginScreen();
            Ui.Header($"遠征ログ: {questName}");
            Ui.Dim("  戦闘以外の進行・採取・選択・報酬");
            Ui.WriteLine($"  {(logs.Count == 0 ? 0 : skip + 1)}〜{skip + take} / 全{logs.Count}件");
            Ui.WriteLine();
            foreach (string log in logs.Skip(skip).Take(take))
                Ui.WriteQuestLog($"    {log}");

            var options = new List<MenuOption>();
            if (skip > 0) options.Add(new MenuOption("o", "さらに古いログ"));
            if (offset > 0) options.Add(new MenuOption("n", "新しいログへ戻る"));
            options.Add(new MenuOption("0", "ログ種別へ戻る", Style: TextStyle.Dim));
            string choice = await Ui.SelectAsync("ページ", options);
            if (choice == "o") offset += LogPageSize;
            else if (choice == "n") offset = Math.Max(0, offset - LogPageSize);
            else return;
        }
    }

    static async Task ShowBattleListAsync(string questName, IReadOnlyList<QuestBattleLog> battles)
    {
        var newestFirst = battles.Reverse().ToList();
        int page = 0;
        while (true)
        {
            int pageCount = Math.Max(1, (newestFirst.Count + BattleListPageSize - 1) / BattleListPageSize);
            page = Math.Clamp(page, 0, pageCount - 1);
            int start = page * BattleListPageSize;
            var visible = newestFirst.Skip(start).Take(BattleListPageSize).ToList();

            Ui.BeginScreen();
            Ui.Header($"戦闘ログ一覧: {questName}");
            Ui.WriteLine($"  {start + 1}〜{start + visible.Count} / 全{newestFirst.Count}戦（新しい順）");
            Ui.Dim("  見直す戦闘を選んでください");
            Ui.WriteLine();

            var options = visible.Select((battle, index) => new MenuOption(
                (index + 1).ToString(),
                $"Turn {battle.Turn} / エリア {battle.Phase} / {BattleName(battle)}",
                BattleListDetail(battle),
                OutcomeStyle(battle.Result))).ToList();
            if (start + visible.Count < newestFirst.Count)
                options.Add(new MenuOption("o", "さらに古い戦闘"));
            if (page > 0)
                options.Add(new MenuOption("n", "新しい戦闘へ戻る"));
            options.Add(new MenuOption("0", "ログ種別へ戻る", Style: TextStyle.Dim));

            string choice = await Ui.SelectAsync("戦闘", options);
            if (int.TryParse(choice, out int selected)
                && selected >= 1
                && selected <= visible.Count)
                await ShowBattleAsync(visible[selected - 1]);
            else if (choice == "o") page++;
            else if (choice == "n") page--;
            else return;
        }
    }

    static async Task ShowBattleAsync(QuestBattleLog battle)
    {
        var lines = BattleDetailLines(battle);
        int page = 0;

        while (true)
        {
            int pageCount = Math.Max(1, (lines.Count + LogPageSize - 1) / LogPageSize);
            page = Math.Clamp(page, 0, pageCount - 1);
            int skip = page * LogPageSize;
            int take = Math.Min(LogPageSize, lines.Count - skip);

            Ui.BeginScreen();
            Ui.Header($"戦闘ログ: Turn {battle.Turn} エリア {battle.Phase}");
            Ui.WriteLine($"  対戦: {BattleName(battle)}");
            Ui.Write("  結果: ");
            Ui.WriteLine(OutcomeLabel(battle.Result), OutcomeStyle(battle.Result));
            if (!string.IsNullOrWhiteSpace(battle.Result))
                Ui.Dim($"        {battle.Result}");
            if (battle.Rounds > 0)
                Ui.WriteLine($"  ラウンド: {battle.Rounds}");
            Ui.WriteLine();
            Ui.WriteLine($"  戦闘詳細 {skip + 1}〜{skip + take} / 全{lines.Count}件");
            foreach (string line in lines.Skip(skip).Take(take))
                WriteBattleLine(line);

            var options = new List<MenuOption>();
            if (skip + take < lines.Count) options.Add(new MenuOption("p", "次のページ"));
            if (page > 0) options.Add(new MenuOption("n", "前のページ"));
            options.Add(new MenuOption("0", "戦闘一覧へ戻る", Style: TextStyle.Dim));
            string choice = await Ui.SelectAsync("ページ", options);
            if (choice == "p") page++;
            else if (choice == "n") page--;
            else return;
        }
    }

    static string BattleName(QuestBattleLog battle)
    {
        const string bossPrefix = "ボス遭遇：";
        const string encounterPrefix = "敵遭遇：";
        if (battle.Title.StartsWith(bossPrefix, StringComparison.Ordinal))
            return $"ボス：{battle.Title[bossPrefix.Length..]}";
        if (battle.Title.StartsWith(encounterPrefix, StringComparison.Ordinal))
            return battle.Title[encounterPrefix.Length..];
        return battle.Opponent;
    }

    static string BattleListDetail(QuestBattleLog battle)
    {
        string rounds = battle.Rounds > 0 ? $" / {battle.Rounds}ラウンド" : "";
        return $"{OutcomeLabel(battle.Result)}{rounds} / 詳細{BattleDetailLines(battle).Count}件";
    }

    static List<string> BattleDetailLines(QuestBattleLog battle) => battle.Lines
        .Where(line => !IsResultSummary(line))
        .Select(CompactBattleLine)
        .Where(line => line.Length > 0)
        .ToList();

    static string OutcomeLabel(string result)
    {
        if (result.Contains("勝利", StringComparison.Ordinal)) return "勝利";
        if (result.Contains("全員戦闘不能", StringComparison.Ordinal)
            || result.Contains("失敗", StringComparison.Ordinal)) return "敗北";
        if (result.Contains("撤退", StringComparison.Ordinal)
            || result.Contains("引き上げ", StringComparison.Ordinal)) return "撤退";
        return string.IsNullOrWhiteSpace(result) ? "結果記録なし" : "終了";
    }

    static TextStyle OutcomeStyle(string result) => OutcomeLabel(result) switch
    {
        "勝利" => TextStyle.Info,
        "敗北" => TextStyle.Error,
        "撤退" => TextStyle.Warn,
        _ => TextStyle.Normal,
    };

    static bool IsResultSummary(string line) =>
        line.StartsWith("[Turn ", StringComparison.Ordinal)
        && (line.Contains(": 敵遭遇：", StringComparison.Ordinal)
            || line.Contains(": ボス遭遇：", StringComparison.Ordinal));

    static string CompactBattleLine(string line)
    {
        int battleStart = line.IndexOf("戦闘開始 ", StringComparison.Ordinal);
        if (battleStart >= 0) return line[battleStart..];
        return AreaPrefix.Replace(line, "").TrimStart();
    }

    static void WriteBattleLine(string line)
    {
        TextStyle style = line.StartsWith("戦闘開始", StringComparison.Ordinal)
            || line.StartsWith("── ラウンド", StringComparison.Ordinal)
                ? TextStyle.Accent
                : line.Contains("戦闘不能", StringComparison.Ordinal)
                    || line.Contains("士気が尽き", StringComparison.Ordinal)
                        ? TextStyle.Warn
                        : line.Contains("撃破", StringComparison.Ordinal)
                            || line.Contains("勝利", StringComparison.Ordinal)
                                ? TextStyle.Info
                                : TextStyle.Dim;
        Ui.WriteLine($"    {line}", style);
    }
}
