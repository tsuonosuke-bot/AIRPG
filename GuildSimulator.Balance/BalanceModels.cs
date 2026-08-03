using System.Text.Json.Serialization;

namespace GuildSimulator.Balance;

public sealed class BalanceConfiguration
{
    public int schemaVersion { get; set; } = 1;
    public int seed { get; set; } = 12345;
    public int runs { get; set; } = 1000;
    public List<BalanceScenario> scenarios { get; set; } = new();
}

public sealed class BalanceScenario
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public string type { get; set; } = "battle";
    public int runs { get; set; }
    public List<string> partyIds { get; set; } = new();
    public List<BalancePartyMember> party { get; set; } = new();
    public int partyLevel { get; set; }
    public int partyRank { get; set; }
    public string enemyUnitId { get; set; } = "";
    public string questId { get; set; } = "";
    public List<string> questIds { get; set; } = new();
    public string policy { get; set; } = "ObjectiveFirst";
    public int startingGold { get; set; } = 5000;
    public int startingGuildRank { get; set; }
    public int maxTurns { get; set; } = 50;
    public int maxGatherExtensions { get; set; } = 3;
    public bool autoRankUp { get; set; } = true;
    public bool continueOnFailure { get; set; }
}

public sealed class BalancePartyMember
{
    public string id { get; set; } = "";
    public int level { get; set; }
    public int rank { get; set; }
    public Dictionary<string, string> equipment { get; set; } = new();
}

public sealed class BalanceReport
{
    public int schemaVersion { get; set; } = 1;
    public DateTime generatedAtUtc { get; set; } = DateTime.UtcNow;
    public int seed { get; set; }
    public int defaultRuns { get; set; }
    public string configurationPath { get; set; } = "";
    public string? baselinePath { get; set; }
    public List<BalanceScenarioResult> scenarios { get; set; } = new();
}

public sealed class BalanceScenarioResult
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public string type { get; set; } = "";
    public int runs { get; set; }
    public int seed { get; set; }
    public double winRatePercent { get; set; }
    public double clearRatePercent { get; set; }
    public double retreatRatePercent { get; set; }
    public double failureRatePercent { get; set; }
    public double bankruptcyRatePercent { get; set; }
    public double meanRounds { get; set; }
    public double meanTurns { get; set; }
    public double meanRemainingHpPercent { get; set; }
    public double meanGoldDelta { get; set; }
    public double meanGatherExtensions { get; set; }
    public double meanChests { get; set; }
    public double meanEndingLevel { get; set; }
    public double meanEndingRank { get; set; }
    public double meanCompletedSteps { get; set; }
    public List<BalanceCampaignStepResult> campaignSteps { get; set; } = new();
    public BalanceScenarioDelta? baselineDelta { get; set; }
}

public sealed class BalanceCampaignStepResult
{
    public string questId { get; set; } = "";
    public string name { get; set; } = "";
    public int reachedRuns { get; set; }
    public double reachRatePercent { get; set; }
    public double clearRatePercent { get; set; }
    public double retreatRatePercent { get; set; }
    public double failureRatePercent { get; set; }
    public double meanStartingLevel { get; set; }
    public double meanEndingLevel { get; set; }
    public double meanStartingRank { get; set; }
    public double meanEndingRank { get; set; }
}

public sealed class BalanceScenarioDelta
{
    public double winRatePoints { get; set; }
    public double clearRatePoints { get; set; }
    public double retreatRatePoints { get; set; }
    public double failureRatePoints { get; set; }
    public double bankruptcyRatePoints { get; set; }
    public double meanRounds { get; set; }
    public double meanTurns { get; set; }
    public double meanRemainingHpPoints { get; set; }
    public double meanGoldDelta { get; set; }
    public double meanGatherExtensions { get; set; }
    public double meanChests { get; set; }
    public double meanEndingLevel { get; set; }
    public double meanEndingRank { get; set; }
    public double meanCompletedSteps { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(BalanceConfiguration))]
[JsonSerializable(typeof(BalanceReport))]
internal partial class BalanceJsonContext : JsonSerializerContext;
