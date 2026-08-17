namespace GuildSimulator.Game.Presentation;

/// <summary>行や選択肢の意味づけ。CLIは色、Webはクラス名へ変換する。</summary>
public enum TextStyle
{
    Normal,
    Dim,
    Info,
    Warn,
    Error,
    Accent,
}

/// <summary>選択肢の操作上の役割。ホスト側が配置や強調へ変換する。</summary>
public enum MenuRole
{
    Default,
    Primary,
    Danger,
}

/// <summary>
/// 選択肢1件。<see cref="Key"/> はCLIで入力する文字列であり、Webではボタンの識別子になる。
/// </summary>
public sealed record MenuOption(
    string Key,
    string Label,
    string? Detail = null,
    TextStyle Style = TextStyle.Normal,
    string? Group = null,
    MenuRole Role = MenuRole.Default);

/// <summary>
/// 画面の入出力先。コンソールとブラウザで実装を差し替えることで、
/// 画面ロジック（Screens配下）を両方のホストで共有する。
/// </summary>
public interface IGameIo
{
    /// <summary>改行せずに書き出す。直後の Write/WriteLine と同じ行に連結される。</summary>
    void Write(string text, TextStyle style = TextStyle.Normal);

    void WriteLine(string text = "", TextStyle style = TextStyle.Normal);

    /// <summary>見出し。区切り線の描き方は実装に委ねる。</summary>
    void Header(string title);

    /// <summary>画面を切り替える区切り。CLIでは何もしなくてよい。</summary>
    void BeginScreen();

    /// <summary>選択肢を提示し、選ばれた <see cref="MenuOption.Key"/> を返す。</summary>
    Task<string> SelectAsync(string prompt, IReadOnlyList<MenuOption> options);

    /// <summary>自由入力を1行受け取る。入力が閉じられた場合は null。</summary>
    Task<string?> ReadLineAsync(string prompt);

    Task PauseAsync();
}
