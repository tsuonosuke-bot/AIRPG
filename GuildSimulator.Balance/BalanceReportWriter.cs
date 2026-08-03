using System.Globalization;
using System.Text;
using System.Text.Json;

namespace GuildSimulator.Balance;

public static class BalanceReportWriter
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = BalanceJsonContext.Default,
    };

    public static BalanceConfiguration ReadConfiguration(string path) =>
        JsonSerializer.Deserialize<BalanceConfiguration>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"Could not read configuration: {path}");

    public static BalanceReport ReadReport(string path) =>
        JsonSerializer.Deserialize<BalanceReport>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"Could not read balance report: {path}");

    public static void Write(BalanceReport report, string jsonPath)
    {
        string fullPath = Path.GetFullPath(jsonPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(
            fullPath,
            JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine,
            new UTF8Encoding(false));
        File.WriteAllText(Path.ChangeExtension(fullPath, ".csv"), ToCsv(report), new UTF8Encoding(true));
    }

    public static string ToCsv(BalanceReport report)
    {
        string[] headers =
        {
            "id", "name", "type", "runs", "seed", "winRatePercent", "clearRatePercent",
            "retreatRatePercent", "failureRatePercent", "bankruptcyRatePercent", "meanRounds",
            "meanTurns", "meanRemainingHpPercent", "meanGoldDelta", "meanGatherExtensions", "meanChests",
            "meanEndingLevel", "meanEndingRank", "meanCompletedSteps",
            "deltaWinRatePoints", "deltaClearRatePoints", "deltaRetreatRatePoints", "deltaFailureRatePoints",
            "deltaMeanRemainingHpPoints", "deltaMeanGold", "deltaMeanEndingLevel", "deltaMeanEndingRank",
            "deltaMeanCompletedSteps",
        };
        var lines = new List<string> { string.Join(",", headers) };
        foreach (var x in report.scenarios)
        {
            string[] values =
            {
                Csv(x.id), Csv(x.name), Csv(x.type), Number(x.runs), Number(x.seed), Number(x.winRatePercent),
                Number(x.clearRatePercent), Number(x.retreatRatePercent), Number(x.failureRatePercent),
                Number(x.bankruptcyRatePercent), Number(x.meanRounds), Number(x.meanTurns),
                Number(x.meanRemainingHpPercent), Number(x.meanGoldDelta), Number(x.meanGatherExtensions),
                Number(x.meanChests), Number(x.meanEndingLevel), Number(x.meanEndingRank),
                Number(x.meanCompletedSteps), Number(x.baselineDelta?.winRatePoints), Number(x.baselineDelta?.clearRatePoints),
                Number(x.baselineDelta?.retreatRatePoints), Number(x.baselineDelta?.failureRatePoints),
                Number(x.baselineDelta?.meanRemainingHpPoints), Number(x.baselineDelta?.meanGoldDelta),
                Number(x.baselineDelta?.meanEndingLevel), Number(x.baselineDelta?.meanEndingRank),
                Number(x.baselineDelta?.meanCompletedSteps),
            };
            lines.Add(string.Join(",", values));
        }
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    static string Number(double? value) => value?.ToString("0.####", CultureInfo.InvariantCulture) ?? "";
    static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
