using GuildSimulator.Core.Models;

namespace GuildSimulator.Cli.Screens;

public static class ConsoleHelper
{
    public static void Header(string title)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"══════════════════════════════");
        Console.WriteLine($"  {title}");
        Console.WriteLine($"══════════════════════════════");
        Console.ResetColor();
    }

    public static void Info(string msg) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine(msg); Console.ResetColor(); }
    public static void Warn(string msg) { Console.ForegroundColor = ConsoleColor.Yellow; Console.WriteLine(msg); Console.ResetColor(); }
    public static void Error(string msg) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine(msg); Console.ResetColor(); }
    public static void Dim(string msg) { Console.ForegroundColor = ConsoleColor.DarkGray; Console.WriteLine(msg); Console.ResetColor(); }

    public static void WriteRarityName(string name, Rarity rarity)
    {
        Console.ForegroundColor = rarity switch
        {
            Rarity.Uncommon => ConsoleColor.Cyan,
            Rarity.Rare => ConsoleColor.DarkMagenta,
            Rarity.Unique => ConsoleColor.Magenta,
            Rarity.Legend => ConsoleColor.DarkYellow,
            _ => Console.ForegroundColor,
        };
        Console.Write(name);
        Console.ResetColor();
    }

    public static string RarityLabel(Rarity rarity) => rarity switch
    {
        Rarity.Common => "コモン",
        Rarity.Uncommon => "アンコモン",
        Rarity.Rare => "レア",
        Rarity.Unique => "ユニーク",
        Rarity.Legend => "レジェンド",
        _ => rarity.ToString(),
    };

    public static int PromptInt(string prompt, int min, int max)
    {
        while (true)
        {
            Console.Write($"{prompt} [{min}-{max}]: ");
            if (int.TryParse(Console.ReadLine(), out int v) && v >= min && v <= max) return v;
            Error("無効な入力です");
        }
    }

    public static bool Confirm(string prompt)
    {
        Console.Write($"{prompt} (y/n): ");
        return Console.ReadLine()?.Trim().ToLower() == "y";
    }

    public static void PressAnyKey()
    {
        Dim("── Enterで続ける ──");
        Console.ReadLine();
    }
}
