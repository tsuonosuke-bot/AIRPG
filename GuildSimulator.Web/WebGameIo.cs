using GuildSimulator.Game.Presentation;

namespace GuildSimulator.Web;

/// <summary>出力1行を構成する、同じ装飾でまとめられた文字列。</summary>
public sealed record OutputSegment(string Text, TextStyle Style);

/// <summary>
/// ブラウザ向けの <see cref="IGameIo"/>。
///
/// ゲームループは <c>await</c> で入力待ちに入り、その間ブラウザへ制御が戻る。
/// 画面のボタンが押されると <see cref="Submit"/> が待機中のタスクを完了させ、ループが再開する。
/// これによりコンソール版の同期的なコードをそのままブラウザで動かせる。
/// </summary>
public sealed class WebGameIo : IGameIo
{
    readonly List<List<OutputSegment>> _lines = new() { new List<OutputSegment>() };
    TaskCompletionSource<string>? _pending;

    // 直前の入力が終わった時点の行数。これ以降の出力は「プレイヤーがまだ読んでいない
    // メッセージ」なので、画面を切り替えても消さずに次の画面へ持ち越す。
    int _readLineCount;

    /// <summary>これまでに出力された行。最後の行は書きかけの可能性がある。</summary>
    public IReadOnlyList<IReadOnlyList<OutputSegment>> Lines => _lines;

    /// <summary>入力待ちのときに表示する選択肢。待っていないときは空。</summary>
    public IReadOnlyList<MenuOption> Options { get; private set; } = Array.Empty<MenuOption>();

    public string Prompt { get; private set; } = "";

    /// <summary>自由入力待ちなら true（選択肢ではなくテキスト欄を出す）。</summary>
    public bool IsTextInput { get; private set; }

    public bool IsWaiting => _pending != null;

    /// <summary>出力や入力待ちの状態が変わったときに発火する。画面はこれで再描画する。</summary>
    public event Action? StateChanged;

    // ---- 出力 ----

    public void Write(string text, TextStyle style = TextStyle.Normal)
    {
        if (text.Length == 0) return;
        _lines[^1].Add(new OutputSegment(text, style));
    }

    public void WriteLine(string text = "", TextStyle style = TextStyle.Normal)
    {
        if (text.Length > 0) _lines[^1].Add(new OutputSegment(text, style));
        _lines.Add(new List<OutputSegment>());
    }

    public void Header(string title)
    {
        // 画面先頭では余分な空行を作らない。
        if (_lines.Count > 1 || _lines[0].Count > 0) WriteLine();
        WriteLine("══════════════════════════════", TextStyle.Accent);
        WriteLine($"  {title}", TextStyle.Accent);
        WriteLine("══════════════════════════════", TextStyle.Accent);
    }

    /// <summary>
    /// スマホでは履歴が長いと読みにくいので、画面が切り替わるたびに出力を流す。
    /// ただし直前の入力以降に出たメッセージ（「〜しました」等）はまだ読まれていないため残す。
    /// </summary>
    public void BeginScreen()
    {
        var unread = _lines.Skip(_readLineCount).ToList();
        _lines.Clear();
        if (unread.Count > 0 && unread.Any(line => line.Count > 0))
            _lines.AddRange(unread);
        else
            _lines.Add(new List<OutputSegment>());
        _readLineCount = 0;
    }

    // ---- 入力 ----

    public Task<string> SelectAsync(string prompt, IReadOnlyList<MenuOption> options)
    {
        Prompt = prompt;
        Options = options;
        IsTextInput = false;
        return WaitForInput();
    }

    public async Task<string?> ReadLineAsync(string prompt)
    {
        Prompt = prompt;
        Options = Array.Empty<MenuOption>();
        IsTextInput = true;
        return await WaitForInput();
    }

    public Task PauseAsync() =>
        SelectAsync("", new[] { new MenuOption("", "続ける") });

    Task<string> WaitForInput()
    {
        // 継続を非同期に走らせ、クリックハンドラの中でゲームループが再入しないようにする。
        _pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        StateChanged?.Invoke();
        return _pending.Task;
    }

    /// <summary>画面から入力を渡してゲームループを再開させる。</summary>
    public void Submit(string value)
    {
        var pending = _pending;
        if (pending == null) return;

        _pending = null;
        Options = Array.Empty<MenuOption>();
        IsTextInput = false;
        // 末尾は書きかけの行なので、その手前までを「読み終えた」とみなす。
        _readLineCount = _lines.Count - 1;
        pending.SetResult(value);
    }
}
