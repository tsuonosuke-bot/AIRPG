using GuildSimulator.Core;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Core.Systems.Quest;
using GuildSimulator.Game.Data;

namespace GuildSimulator.Balance;

public sealed class BalanceRunner(GameMasterData db)
{
    readonly Dictionary<string, AdventurerMasterData> adventurers =
        db.allAdventurers.ToDictionary(x => x.id, StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, QuestMasterData> quests =
        db.allQuests.ToDictionary(x => x.id, StringComparer.OrdinalIgnoreCase);

    public BalanceReport Run(
        BalanceConfiguration configuration,
        string configurationPath = "",
        BalanceReport? baseline = null,
        string? baselinePath = null)
    {
        if (configuration.schemaVersion != 1)
            throw new InvalidDataException($"Unsupported balance configuration schemaVersion: {configuration.schemaVersion}");
        if (configuration.runs <= 0)
            throw new InvalidDataException("runs must be greater than 0.");
        if (configuration.scenarios.Count == 0)
            throw new InvalidDataException("At least one scenario is required.");

        var report = new BalanceReport
        {
            seed = configuration.seed,
            defaultRuns = configuration.runs,
            configurationPath = configurationPath,
            baselinePath = baselinePath,
        };

        for (int index = 0; index < configuration.scenarios.Count; index++)
        {
            var scenario = configuration.scenarios[index];
            ValidateScenario(scenario);
            int scenarioSeed = unchecked(configuration.seed + index * 1_000_003);
            int runs = scenario.runs > 0 ? scenario.runs : configuration.runs;
            var result = scenario.type.ToLowerInvariant() switch
            {
                "battle" => RunBattle(scenario, runs, scenarioSeed),
                "quest" => RunQuest(scenario, runs, scenarioSeed),
                _ => throw new InvalidDataException($"{scenario.id}: type must be battle or quest."),
            };

            var previous = baseline?.scenarios.FirstOrDefault(x =>
                string.Equals(x.id, result.id, StringComparison.OrdinalIgnoreCase));
            if (previous != null) result.baselineDelta = Delta(result, previous);
            report.scenarios.Add(result);
        }

        return report;
    }

    void ValidateScenario(BalanceScenario scenario)
    {
        if (string.IsNullOrWhiteSpace(scenario.id))
            throw new InvalidDataException("Every scenario needs an id.");
        if (scenario.partyIds.Count == 0 || scenario.partyIds.Count > 6)
            throw new InvalidDataException($"{scenario.id}: partyIds must contain 1 to 6 adventurers.");
        foreach (var id in scenario.partyIds)
            if (!adventurers.ContainsKey(id))
                throw new InvalidDataException($"{scenario.id}: unknown adventurer '{id}'.");
        if (scenario.type.Equals("battle", StringComparison.OrdinalIgnoreCase)
            && !db.enemyUnits.ContainsKey(scenario.enemyUnitId))
            throw new InvalidDataException($"{scenario.id}: unknown enemy unit '{scenario.enemyUnitId}'.");
        if (scenario.type.Equals("quest", StringComparison.OrdinalIgnoreCase)
            && !quests.ContainsKey(scenario.questId))
            throw new InvalidDataException($"{scenario.id}: unknown quest '{scenario.questId}'.");
        if (scenario.maxTurns <= 0)
            throw new InvalidDataException($"{scenario.id}: maxTurns must be greater than 0.");
    }

    BalanceScenarioResult RunBattle(BalanceScenario scenario, int runs, int seed)
    {
        int wins = 0, retreats = 0, failures = 0;
        long rounds = 0;
        double hpPercent = 0;
        var enemyTemplate = db.enemyUnits[scenario.enemyUnitId];
        var policy = ParsePolicy(scenario.policy, scenario.id);

        for (int runIndex = 0; runIndex < runs; runIndex++)
        {
            using var random = GameRandom.UseSeed(unchecked(seed + runIndex));
            var party = CreateParty(scenario.partyIds);
            var enemies = enemyTemplate.Formation
                .Take(6)
                .Select(master => master == null ? null : new EnemyData(master))
                .ToArray();
            Array.Resize(ref enemies, 6);
            InitializeCombatStats(party.Cast<IUnitMember?>().ToArray(), true);
            InitializeCombatStats(enemies.Cast<IUnitMember?>().ToArray(), false);
            var morale = new MoraleState(
                UnitCalculator.CalcPerMember(party.Cast<IUnitMember?>().ToArray(), true).Sum(x => x.stats.san));
            var result = BattleResolver.Resolve(
                party.Cast<IUnitMember?>().ToArray(),
                enemies.Cast<IUnitMember?>().ToArray(),
                new List<string>(), 1, 1, morale, policy);

            bool failed = !party.Any(a => a != null && a.isAlive && !a.isIncapacitated);
            bool won = !result.adventurersRetreated && !failed
                && !enemies.Any(e => e != null && e.isAlive);
            if (won) wins++;
            if (result.adventurersRetreated) retreats++;
            if (failed) failures++;
            rounds += result.rounds;
            hpPercent += RemainingHpPercent(party);
        }

        return new BalanceScenarioResult
        {
            id = scenario.id,
            name = DisplayName(scenario),
            type = "battle",
            runs = runs,
            seed = seed,
            winRatePercent = Rate(wins, runs),
            clearRatePercent = Rate(wins, runs),
            retreatRatePercent = Rate(retreats, runs),
            failureRatePercent = Rate(failures, runs),
            meanRounds = Mean(rounds, runs),
            meanRemainingHpPercent = Mean(hpPercent, runs),
        };
    }

    BalanceScenarioResult RunQuest(BalanceScenario scenario, int runs, int seed)
    {
        int clears = 0, retreats = 0, failures = 0, bankruptcies = 0;
        long turns = 0, gatherExtensions = 0, chests = 0;
        double hpPercent = 0, goldDelta = 0;
        var quest = quests[scenario.questId];
        var policy = ParsePolicy(scenario.policy, scenario.id);

        for (int runIndex = 0; runIndex < runs; runIndex++)
        {
            using var random = GameRandom.UseSeed(unchecked(seed + runIndex));
            var guild = new GuildManager(scenario.startingGold, quest.rank);
            var party = CreateParty(scenario.partyIds);
            foreach (var member in party.Where(x => x != null)) guild.AddAdventurer(member!);
            var manager = new QuestManager(guild);
            if (!manager.TryStartQuest(quest, party, 1, out var error, policy: policy))
                throw new InvalidOperationException($"{scenario.id}: quest could not start: {error}");
            var questRun = manager.activeQuests.Single();
            int elapsed = 0;
            bool bankrupt = false;
            bool terminalStateCaptured = false;
            double terminalHpPercent = 0;
            int terminalChestCount = 0;

            for (int turn = 2; turn <= scenario.maxTurns + 1; turn++)
            {
                elapsed++;
                manager.AdvanceAll(turn);
                guild.PayUpkeepForAll(turn);
                bankrupt |= guild.Gold <= 0;

                if (questRun.HasPendingChoice)
                {
                    var option = questRun.pendingChoice!.Event.options[0];
                    var target = option.targetsOneMember
                        ? questRun.EnumerateMembers().FirstOrDefault(x => x.isAlive)
                        : null;
                    if (!manager.ResolveChoice(questRun, 0, target, out var choiceError))
                        throw new InvalidOperationException($"{scenario.id}: choice could not resolve: {choiceError}");
                }
                if (questRun.HasGatherDecision)
                {
                    bool continueSearch = questRun.gatherExtensions < scenario.maxGatherExtensions;
                    if (!manager.ResolveGatherDecision(questRun, continueSearch, out var gatherError))
                        throw new InvalidOperationException($"{scenario.id}: gather decision could not resolve: {gatherError}");
                }
                if (questRun.CanComplete || questRun.failed)
                {
                    terminalHpPercent = RemainingHpPercent(party);
                    terminalChestCount = questRun.chests.Count;
                    terminalStateCaptured = true;
                    manager.FinalizeQuest(questRun);
                    break;
                }
            }

            if (!terminalStateCaptured)
            {
                terminalHpPercent = RemainingHpPercent(party);
                terminalChestCount = questRun.chests.Count;
            }
            if (!questRun.rewarded && (questRun.CanComplete || questRun.failed)) manager.FinalizeQuest(questRun);
            if (questRun.IsCleared) clears++;
            if (questRun.retreated) retreats++;
            if (questRun.failed || (!questRun.rewarded && !questRun.IsCleared && !questRun.retreated)) failures++;
            if (bankrupt) bankruptcies++;
            turns += elapsed;
            gatherExtensions += questRun.gatherExtensions;
            chests += terminalChestCount;
            hpPercent += terminalHpPercent;
            goldDelta += guild.Gold - scenario.startingGold;
        }

        return new BalanceScenarioResult
        {
            id = scenario.id,
            name = DisplayName(scenario),
            type = "quest",
            runs = runs,
            seed = seed,
            winRatePercent = Rate(clears, runs),
            clearRatePercent = Rate(clears, runs),
            retreatRatePercent = Rate(retreats, runs),
            failureRatePercent = Rate(failures, runs),
            bankruptcyRatePercent = Rate(bankruptcies, runs),
            meanTurns = Mean(turns, runs),
            meanRemainingHpPercent = Mean(hpPercent, runs),
            meanGoldDelta = Mean(goldDelta, runs),
            meanGatherExtensions = Mean(gatherExtensions, runs),
            meanChests = Mean(chests, runs),
        };
    }

    AdventurerData?[] CreateParty(IEnumerable<string> ids)
    {
        var party = ids.Select(id => (AdventurerData?)new AdventurerData(adventurers[id])).ToArray();
        Array.Resize(ref party, 6);
        return party;
    }

    static void InitializeCombatStats(IUnitMember?[] members, bool allies)
    {
        foreach (var (member, stats) in UnitCalculator.CalcPerMember(members, allies))
        {
            member.CombatHpMax = stats.hp;
            member.CombatHp = stats.hp;
        }
    }

    static ExpeditionPolicy ParsePolicy(string value, string scenarioId) =>
        Enum.TryParse<ExpeditionPolicy>(value, true, out var policy)
            ? policy
            : throw new InvalidDataException($"{scenarioId}: unknown policy '{value}'.");

    static string DisplayName(BalanceScenario scenario) =>
        string.IsNullOrWhiteSpace(scenario.name) ? scenario.id : scenario.name;

    static double RemainingHpPercent(IEnumerable<AdventurerData?> party)
    {
        var members = party.Where(x => x != null).Select(x => x!).ToList();
        double max = members.Sum(x => Math.Max(0, x.CombatHpMax));
        double current = members.Where(x => x.isAlive).Sum(x => Math.Max(0, x.CombatHp));
        return max <= 0 ? 0 : current / max * 100;
    }

    static double Rate(long value, int runs) => Math.Round(value * 100d / runs, 4);
    static double Mean(long value, int runs) => Math.Round(value / (double)runs, 4);
    static double Mean(double value, int runs) => Math.Round(value / runs, 4);

    static BalanceScenarioDelta Delta(BalanceScenarioResult current, BalanceScenarioResult previous) => new()
    {
        winRatePoints = Round(current.winRatePercent - previous.winRatePercent),
        clearRatePoints = Round(current.clearRatePercent - previous.clearRatePercent),
        retreatRatePoints = Round(current.retreatRatePercent - previous.retreatRatePercent),
        failureRatePoints = Round(current.failureRatePercent - previous.failureRatePercent),
        bankruptcyRatePoints = Round(current.bankruptcyRatePercent - previous.bankruptcyRatePercent),
        meanRounds = Round(current.meanRounds - previous.meanRounds),
        meanTurns = Round(current.meanTurns - previous.meanTurns),
        meanRemainingHpPoints = Round(current.meanRemainingHpPercent - previous.meanRemainingHpPercent),
        meanGoldDelta = Round(current.meanGoldDelta - previous.meanGoldDelta),
        meanGatherExtensions = Round(current.meanGatherExtensions - previous.meanGatherExtensions),
        meanChests = Round(current.meanChests - previous.meanChests),
    };

    static double Round(double value) => Math.Round(value, 4);
}
