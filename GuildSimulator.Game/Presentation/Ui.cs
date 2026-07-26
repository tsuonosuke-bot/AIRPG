using GuildSimulator.Core.Models;

namespace GuildSimulator.Game.Presentation;

/// <summary>
/// 画面ロジックから使う入出力の入口。
/// ゲームは1プロセスに1セッションしか走らせないため、静的な現在ホストを保持する。
/// </summary>
public static class Ui
{
    static IGameIo? _io;

    public static IGameIo Io =>
        _io ?? throw new InvalidOperationException("Ui.Use() で IGameIo を設定してください");

    public static void Use(IGameIo io) => _io = io;

    // ---- 出力 ----

    public static void Write(string text, TextStyle style = TextStyle.Normal) => Io.Write(text, style);
    public static void WriteLine(string text = "", TextStyle style = TextStyle.Normal) => Io.WriteLine(text, style);
    public static void Header(string title) => Io.Header(title);
    public static void BeginScreen() => Io.BeginScreen();

    public static void Info(string msg) => Io.WriteLine(msg, TextStyle.Info);
    public static void Warn(string msg) => Io.WriteLine(msg, TextStyle.Warn);
    public static void Error(string msg) => Io.WriteLine(msg, TextStyle.Error);
    public static void Dim(string msg) => Io.WriteLine(msg, TextStyle.Dim);

    public static void WriteQuestLog(string msg) =>
        Io.WriteLine(msg, msg.Contains("レベルアップ") ? TextStyle.Warn : TextStyle.Dim);

    /// <summary>レアリティに応じた色で名前を書く（改行しない）。</summary>
    public static void WriteRarityName(string name, Rarity rarity) => Io.Write(name, RarityStyle(rarity));

    public static TextStyle RarityStyle(Rarity rarity) => rarity switch
    {
        Rarity.Uncommon => TextStyle.Info,
        Rarity.Rare => TextStyle.Accent,
        Rarity.Unique => TextStyle.Accent,
        Rarity.Legend => TextStyle.Warn,
        _ => TextStyle.Normal,
    };

    public static string RarityLabel(Rarity rarity) => rarity switch
    {
        Rarity.Common => "コモン",
        Rarity.Uncommon => "アンコモン",
        Rarity.Rare => "レア",
        Rarity.Unique => "ユニーク",
        Rarity.Legend => "レジェンド",
        _ => rarity.ToString(),
    };

    // ---- 入力 ----

    public static Task<string> SelectAsync(string prompt, IReadOnlyList<MenuOption> options) =>
        Io.SelectAsync(prompt, options);

    public static Task<string?> ReadLineAsync(string prompt) => Io.ReadLineAsync(prompt);

    public static Task PauseAsync() => Io.PauseAsync();

    public static async Task<bool> ConfirmAsync(string prompt)
    {
        var key = await Io.SelectAsync(prompt, new[]
        {
            new MenuOption("y", "はい"),
            new MenuOption("n", "いいえ"),
        });
        return key == "y";
    }

    /// <summary>min〜max の整数を選ばせる。件数が多い場合でも選択肢として並べる。</summary>
    public static async Task<int> SelectIntAsync(string prompt, int min, int max)
    {
        var options = new List<MenuOption>();
        for (int v = min; v <= max; v++) options.Add(new MenuOption(v.ToString(), v.ToString()));
        string key = await Io.SelectAsync(prompt, options);
        return int.TryParse(key, out int picked) ? picked : min;
    }

    // ---- 選択肢の組み立て補助 ----

    /// <summary>「0. 戻る」を付けた一覧から選ばせ、1始まりの番号を返す。戻る/無効なら null。</summary>
    public static async Task<int?> SelectIndexAsync(
        string prompt,
        IReadOnlyList<MenuOption> entries,
        string backLabel = "戻る")
    {
        var options = new List<MenuOption>(entries) { new("0", backLabel, Style: TextStyle.Dim) };
        string key = await Io.SelectAsync(prompt, options);
        if (int.TryParse(key, out int index) && index >= 1 && index <= entries.Count) return index;
        return null;
    }
}
