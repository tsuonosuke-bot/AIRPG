using GuildSimulator.Cli;
using GuildSimulator.Game;
using GuildSimulator.Game.Data;
using GuildSimulator.Game.Presentation;

// 入出力がリダイレクトされている環境ではエンコーディング設定が失敗しうるので握りつぶす。
try
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    if (!Console.IsInputRedirected) Console.InputEncoding = System.Text.Encoding.UTF8;
}
catch (IOException) { }

Ui.Use(new ConsoleGameIo());

// ---- データロード ----
string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
if (!Directory.Exists(dataDir))
    dataDir = Path.Combine(Directory.GetCurrentDirectory(), "Data");

Console.WriteLine("データ読み込み中...");
var db = MasterLoader.Load(dataDir);

if (args.Contains("--validate-master", StringComparer.OrdinalIgnoreCase))
{
    var errors = MasterValidator.Validate(db);
    if (errors.Count == 0)
    {
        Console.WriteLine("マスタデータ検証: OK");
        Console.WriteLine($"  冒険者 {db.allAdventurers.Count} / 装備 {db.equipment.Count}"
            + $" / 敵 {db.enemies.Count} / 消費アイテム {db.consumables.Count}"
            + $" / 選択イベント {db.choiceEvents.Count} / 手掛かり {db.clues.Count}");
    }
    else
    {
        Console.WriteLine($"マスタデータ検証: {errors.Count}件のエラー");
        foreach (var error in errors) Console.WriteLine($"  - {error}");
        Environment.ExitCode = 1;
    }
    return;
}

await GameLoop.RunAsync(db, new FileSaveStore(DefaultSavePath()));

static string DefaultSavePath()
{
    string dir = Path.Combine(AppContext.BaseDirectory, "Saves");
    if (!Directory.Exists(dir))
    {
        try { Directory.CreateDirectory(dir); }
        catch (IOException) { dir = Directory.GetCurrentDirectory(); }
        catch (UnauthorizedAccessException) { dir = Directory.GetCurrentDirectory(); }
    }
    return Path.Combine(dir, "save1.json");
}
