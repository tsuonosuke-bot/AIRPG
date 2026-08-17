namespace GuildSimulator.Game.Presentation;

/// <summary>CLIとWebで共有するメインメニューとギルド管理サブメニューを組み立てる。</summary>
internal static class MainMenuBuilder
{
    public static IReadOnlyList<MenuOption> BuildMain(
        int currentTurn,
        int upkeepPerTurn,
        int projectedAfterUpkeep,
        int pendingDecisionCount,
        bool relicsEnabled)
    {
        var menu = new List<MenuOption>
        {
            new("1", "クエストボード", Group: "クエスト"),
            new("2", "進行中クエスト", Group: "クエスト"),
            new("3", "一覧・装備管理", Group: "冒険者"),
            new("4", "雇う", Group: "冒険者"),
            new("5", "倉庫", Group: "ギルド資産"),
            new("6", "商店", Group: "ギルド資産"),
        };

        if (relicsEnabled)
            menu.Add(new MenuOption("7", "遺物一覧", Group: "ギルド資産"));

        menu.Add(new MenuOption("F", "施設", Group: "ギルド資産"));

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
            "ギルド管理",
            "経済ログ・記録・図鑑・シミュレーター・ヘルプ",
            Group: "その他"));
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

    public static IReadOnlyList<MenuOption> BuildGuildManagement() => new[]
    {
        new MenuOption("8", "経済ログ", Group: "記録"),
        new MenuOption("B", "埋葬記録", Group: "記録"),
        new MenuOption("J", "調査記録", Group: "記録"),
        new MenuOption("M", "モンスター図鑑", Group: "資料"),
        new MenuOption("T", "戦闘シミュレーター", Group: "ツール"),
        new MenuOption("H", "ヘルプ・用語集", Group: "ツール"),
        new MenuOption("0", "メインメニューへ戻る", Style: TextStyle.Dim, Group: "ナビゲーション"),
    };
}
