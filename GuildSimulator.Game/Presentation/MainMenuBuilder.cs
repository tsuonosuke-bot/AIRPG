namespace GuildSimulator.Game.Presentation;

/// <summary>CLIとWebで共有するメインメニューと記録・資料サブメニューを組み立てる。</summary>
internal static class MainMenuBuilder
{
    public static IReadOnlyList<MenuOption> BuildMain(
        int currentTurn,
        int upkeepPerTurn,
        int projectedAfterUpkeep,
        int pendingDecisionCount,
        bool relicsEnabled,
        int storyLeadCount = 0)
    {
        var menu = new List<MenuOption>
        {
            new(
                "1",
                "クエストボード",
                storyLeadCount > 0 ? $"新たな物語調査が掲示されています（{storyLeadCount}件）" : null,
                storyLeadCount > 0 ? TextStyle.Accent : TextStyle.Normal,
                Group: "クエスト"),
            new("2", "進行中クエスト", Group: "クエスト"),
            new("3", "一覧・装備管理", Group: "冒険者"),
            new("4", "雇う", Group: "冒険者"),
            new("5", "倉庫", Group: "拠点運営"),
            new("6", "商店", Group: "拠点運営"),
        };

        if (relicsEnabled)
            menu.Add(new MenuOption("7", "遺物一覧", Group: "拠点運営"));

        menu.Add(new MenuOption("F", "施設", Group: "拠点運営"));

        string turnLabel = pendingDecisionCount > 0
            ? $"指示待ちを解決（{pendingDecisionCount}件）"
            : "ターンを進める";
        string turnDetail = pendingDecisionCount > 0
            ? $"ターン進行前に判断が必要です\n解決後: Turn {currentTurn} → {currentTurn + 1}"
            : $"Turn {currentTurn} → {currentTurn + 1}\n維持費 {upkeepPerTurn}G / 支払後 {projectedAfterUpkeep}G（報酬前）";
        if (projectedAfterUpkeep <= 0)
            turnDetail += "\nクエスト報酬がなければ破産します";

        menu.Add(new MenuOption(
            "9",
            turnLabel,
            turnDetail,
            pendingDecisionCount > 0 || projectedAfterUpkeep <= 0
                ? TextStyle.Warn
                : TextStyle.Accent,
            "ターン操作",
            MenuRole.Primary));

        menu.Add(new MenuOption(
            "G",
            "記録・資料",
            "経済ログ・各種記録・図鑑・シミュレーター・ヘルプ",
            Group: "情報"));
        menu.Add(new MenuOption("S", "セーブする", Group: "セーブデータ"));
        menu.Add(new MenuOption("L", "ロードする", Group: "セーブデータ"));
        menu.Add(new MenuOption(
            "0",
            "ゲーム終了",
            "セーブしていない進行は失われます",
            TextStyle.Dim,
            "システム",
            MenuRole.Danger));

        return menu;
    }

    public static IReadOnlyList<MenuOption> BuildRecordsAndReferences() => new[]
    {
        new MenuOption("8", "経済ログ", Group: "記録"),
        new MenuOption("Q", "完了クエスト履歴", "遠征ログ・戦闘別ログを再閲覧", Group: "記録"),
        new MenuOption("B", "埋葬記録", Group: "記録"),
        new MenuOption("J", "調査記録", "現在の事件・手掛かり・選んだ結末", Group: "記録"),
        new MenuOption("M", "モンスター図鑑", Group: "資料"),
        new MenuOption("T", "戦闘シミュレーター", Group: "ツール"),
        new MenuOption("H", "ヘルプ・用語集", Group: "ツール"),
        new MenuOption("0", "メインメニューへ戻る", Style: TextStyle.Dim, Group: "ナビゲーション"),
    };
}
