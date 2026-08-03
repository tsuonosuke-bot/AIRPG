using GuildSimulator.Balance;
using GuildSimulator.Game.Data;

try
{
    var options = CommandLine.Parse(args);
    if (options.Help)
    {
        CommandLine.PrintHelp();
        return;
    }

    var configuration = BalanceReportWriter.ReadConfiguration(options.ConfigPath);
    if (options.Runs.HasValue) configuration.runs = options.Runs.Value;
    if (options.Seed.HasValue) configuration.seed = options.Seed.Value;
    var errors = MasterValidator.Validate(MasterLoader.Load(options.DataPath));
    if (errors.Count > 0)
        throw new InvalidDataException("Master validation failed:" + Environment.NewLine
            + string.Join(Environment.NewLine, errors.Select(x => $"  - {x}")));

    var db = MasterLoader.Load(options.DataPath);
    BalanceReport? baseline = options.ComparePath == null
        ? null
        : BalanceReportWriter.ReadReport(options.ComparePath);
    var report = new BalanceRunner(db).Run(
        configuration,
        Path.GetFullPath(options.ConfigPath),
        baseline,
        options.ComparePath == null ? null : Path.GetFullPath(options.ComparePath));
    BalanceReportWriter.Write(report, options.OutputPath);

    Console.WriteLine($"Balance Lab: {report.scenarios.Count} scenarios / seed {report.seed}");
    foreach (var result in report.scenarios)
    {
        Console.WriteLine($"  {result.id}: clear {result.clearRatePercent:F1}% / retreat {result.retreatRatePercent:F1}%"
            + $" / fail {result.failureRatePercent:F1}% / HP {result.meanRemainingHpPercent:F1}%");
        foreach (var step in result.campaignSteps)
            Console.WriteLine($"    {step.questId}: reach {step.reachRatePercent:F1}% / clear {step.clearRatePercent:F1}%"
                + $" / level {step.meanStartingLevel:F1}->{step.meanEndingLevel:F1}"
                + $" / rank {step.meanStartingRank:F1}->{step.meanEndingRank:F1}");
    }
    Console.WriteLine($"JSON={Path.GetFullPath(options.OutputPath)}");
    Console.WriteLine($"CSV={Path.ChangeExtension(Path.GetFullPath(options.OutputPath), ".csv")}");
}
catch (Exception error)
{
    Console.Error.WriteLine($"Balance Lab failed: {error.Message}");
    Environment.ExitCode = 1;
}

sealed class CommandLine
{
    public required string ConfigPath { get; init; }
    public required string DataPath { get; init; }
    public required string OutputPath { get; init; }
    public string? ComparePath { get; init; }
    public int? Runs { get; init; }
    public int? Seed { get; init; }
    public bool Help { get; init; }

    public static CommandLine Parse(string[] args)
    {
        string config = DefaultConfigPath();
        string data = DefaultDataPath();
        string output = Path.Combine(Directory.GetCurrentDirectory(), "outputs", "balance-lab", "balance-report.json");
        string? compare = null;
        int? runs = null, seed = null;
        bool help = false;
        for (int i = 0; i < args.Length; i++)
        {
            string value = args[i];
            if (value is "--help" or "-h") { help = true; continue; }
            if (i + 1 >= args.Length) throw new ArgumentException($"Missing value after {value}");
            string next = args[++i];
            switch (value)
            {
                case "--config": config = next; break;
                case "--data": data = next; break;
                case "--output": output = next; break;
                case "--compare": compare = next; break;
                case "--runs": runs = int.Parse(next); break;
                case "--seed": seed = int.Parse(next); break;
                default: throw new ArgumentException($"Unknown option: {value}");
            }
        }
        return new CommandLine
        {
            ConfigPath = Path.GetFullPath(config), DataPath = Path.GetFullPath(data),
            OutputPath = Path.GetFullPath(output),
            ComparePath = compare == null ? null : Path.GetFullPath(compare),
            Runs = runs, Seed = seed, Help = help,
        };
    }

    static string DefaultConfigPath()
    {
        string output = Path.Combine(AppContext.BaseDirectory, "scenarios", "default.json");
        return File.Exists(output)
            ? output
            : Path.Combine(Directory.GetCurrentDirectory(), "GuildSimulator.Balance", "scenarios", "default.json");
    }

    static string DefaultDataPath()
    {
        string output = Path.Combine(AppContext.BaseDirectory, "Data");
        return Directory.Exists(output)
            ? output
            : Path.Combine(Directory.GetCurrentDirectory(), "GuildSimulator.Game", "Data");
    }

    public static void PrintHelp() => Console.WriteLine("""
Balance Lab
  dotnet run --project GuildSimulator.Balance -- [options]

Options:
  --config <path>   Scenario JSON (default: scenarios/default.json)
  --data <path>     Master JSON directory
  --output <path>   Output JSON; a CSV is written next to it
  --runs <count>    Override the default trial count
  --seed <number>   Override the deterministic seed
  --compare <path>  Compare with a previous Balance Lab JSON report
""");
}
