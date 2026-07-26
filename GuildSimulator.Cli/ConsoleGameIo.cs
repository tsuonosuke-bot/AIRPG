using GuildSimulator.Game.Presentation;

namespace GuildSimulator.Cli;

/// <summary>コンソール向けの <see cref="IGameIo"/>。従来のCLI表示をそのまま再現する。</summary>
public sealed class ConsoleGameIo : IGameIo
{
    public void Write(string text, TextStyle style = TextStyle.Normal)
    {
        WithColor(style, () => Console.Write(text));
    }

    public void WriteLine(string text = "", TextStyle style = TextStyle.Normal)
    {
        WithColor(style, () => Console.WriteLine(text));
    }

    public void Header(string title)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("══════════════════════════════");
        Console.WriteLine($"  {title}");
        Console.WriteLine("══════════════════════════════");
        Console.ResetColor();
    }

    // CLIでは画面のクリアを行わず、そのまま書き足していく従来の挙動を保つ。
    public void BeginScreen() { }

    public Task<string> SelectAsync(string prompt, IReadOnlyList<MenuOption> options)
    {
        while (true)
        {
            string? lastGroup = null;
            foreach (var option in options)
            {
                if (option.Group != null && option.Group != lastGroup)
                {
                    Console.WriteLine();
                    Console.WriteLine($"  【{option.Group}】");
                    lastGroup = option.Group;
                }
                string key = option.Key.Length == 0 ? "Enter" : option.Key;
                WithColor(option.Style, () => Console.WriteLine($"    {key}. {option.Label}"));
                if (!string.IsNullOrWhiteSpace(option.Detail))
                    WithColor(TextStyle.Dim, () => Console.WriteLine($"        {option.Detail}"));
            }
            Console.Write($"\n{prompt}: ");

            string? line = Console.ReadLine();
            if (line == null) return Task.FromResult(FallbackKey(options));

            string trimmed = line.Trim();
            var match = options.FirstOrDefault(
                o => string.Equals(o.Key, trimmed, StringComparison.OrdinalIgnoreCase));
            if (match != null) return Task.FromResult(match.Key);

            WithColor(TextStyle.Error, () => Console.WriteLine("無効な入力です"));
        }
    }

    public Task<string?> ReadLineAsync(string prompt)
    {
        Console.Write($"{prompt}: ");
        return Task.FromResult(Console.ReadLine());
    }

    public Task PauseAsync()
    {
        WithColor(TextStyle.Dim, () => Console.WriteLine("── Enterで続ける ──"));
        Console.ReadLine();
        return Task.CompletedTask;
    }

    /// <summary>標準入力が閉じられたときの既定値。「戻る/やめる」に相当するキーを優先する。</summary>
    static string FallbackKey(IReadOnlyList<MenuOption> options) =>
        options.FirstOrDefault(o => o.Key == "0")?.Key
        ?? options.LastOrDefault()?.Key
        ?? "";

    static void WithColor(TextStyle style, Action write)
    {
        var color = ColorOf(style);
        if (color == null) { write(); return; }

        Console.ForegroundColor = color.Value;
        write();
        Console.ResetColor();
    }

    static ConsoleColor? ColorOf(TextStyle style) => style switch
    {
        TextStyle.Dim => ConsoleColor.DarkGray,
        TextStyle.Info => ConsoleColor.Green,
        TextStyle.Warn => ConsoleColor.Yellow,
        TextStyle.Error => ConsoleColor.Red,
        TextStyle.Accent => ConsoleColor.Magenta,
        _ => null,
    };
}
