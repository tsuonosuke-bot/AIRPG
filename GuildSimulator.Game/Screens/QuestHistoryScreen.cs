using GuildSimulator.Core.GameData;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Game.Presentation;

namespace GuildSimulator.Game.Screens;

/// <summary>完了したクエストを新しい順に選び、当時の詳細ログを再閲覧する画面。</summary>
internal static class QuestHistoryScreen
{
    const int PageSize = 10;

    public static async Task ShowAsync(QuestManager questManager)
    {
        var newestFirst = questManager.QuestHistory.Reverse().ToList();
        if (newestFirst.Count == 0)
        {
            Ui.BeginScreen();
            Ui.Header("完了クエスト履歴");
            Ui.Dim("  完了したクエストはまだありません");
            await Ui.PauseAsync();
            return;
        }

        int page = 0;
        while (true)
        {
            int pageCount = (newestFirst.Count + PageSize - 1) / PageSize;
            page = Math.Clamp(page, 0, pageCount - 1);
            int start = page * PageSize;
            var visible = newestFirst.Skip(start).Take(PageSize).ToList();

            Ui.BeginScreen();
            Ui.Header("完了クエスト履歴");
            Ui.WriteLine($"  {start + 1}〜{start + visible.Count} / 全{newestFirst.Count}件（新しい順・最大{QuestManager.QuestHistoryLimit}件）");
            Ui.Dim("  クエストを選ぶと、当時の遠征ログと戦闘別ログを確認できます");
            Ui.WriteLine();

            var options = visible.Select((entry, index) => HistoryOption(entry, index + 1)).ToList();
            if (start + visible.Count < newestFirst.Count)
                options.Add(new MenuOption("o", "さらに古い履歴"));
            if (page > 0)
                options.Add(new MenuOption("n", "新しい履歴へ戻る"));
            options.Add(new MenuOption("0", "記録・資料へ戻る", Style: TextStyle.Dim));

            string choice = await Ui.SelectAsync("クエスト", options);
            if (int.TryParse(choice, out int selected)
                && selected >= 1
                && selected <= visible.Count)
                await QuestLogScreen.ShowAsync(visible[selected - 1]);
            else if (choice == "o")
                page++;
            else if (choice == "n")
                page--;
            else
                return;
        }
    }

    static MenuOption HistoryOption(QuestHistoryEntry entry, int index)
    {
        var logIndex = QuestLogIndexer.Build(entry.Logs);
        string turn = entry.StartedTurn == entry.CompletedTurn
            ? $"Turn {entry.StartedTurn}"
            : $"Turn {entry.StartedTurn}〜{entry.CompletedTurn}";
        return new MenuOption(
            index.ToString(),
            $"{OutcomeLabel(entry.Outcome)}  {entry.QuestName}",
            $"{turn} / 遠征ログ {logIndex.ExpeditionLogs.Count}件 / 戦闘 {logIndex.Battles.Count}戦",
            OutcomeStyle(entry.Outcome));
    }

    static string OutcomeLabel(QuestHistoryOutcome outcome) => outcome switch
    {
        QuestHistoryOutcome.Success => "成功",
        QuestHistoryOutcome.Retreat => "撤退",
        _ => "失敗",
    };

    static TextStyle OutcomeStyle(QuestHistoryOutcome outcome) => outcome switch
    {
        QuestHistoryOutcome.Success => TextStyle.Info,
        QuestHistoryOutcome.Retreat => TextStyle.Warn,
        _ => TextStyle.Error,
    };
}
