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
                "campaign" => RunCampaign(scenario, runs, scenarioSeed),
                _ => throw new InvalidDataException($"{scenario.id}: type must be battle, quest, or campaign."),
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
        if (scenario.party.Count > 0 && scenario.partyIds.Count > 0)
            throw new InvalidDataException($"{scenario.id}: use either party or partyIds, not both.");

        var members = PartyMembers(scenario).ToList();
        if (members.Count == 0 || members.Count > GuildManager.FormationSlotCount)
            throw new InvalidDataException(
                $"{scenario.id}: party must contain 1 to {GuildManager.FormationSlotCount} adventurers.");
        if (members.Select(x => x.id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != members.Count)
            throw new InvalidDataException($"{scenario.id}: party contains duplicate adventurer ids.");
        foreach (var member in members)
            if (member.formationSlot < 0 || member.formationSlot > GuildManager.FormationSlotCount)
                throw new InvalidDataException(
                    $"{scenario.id}: {member.id}.formationSlot must be 0 to {GuildManager.FormationSlotCount}.");
        var fixedSlots = members.Where(member => member.formationSlot > 0).ToList();
        if (fixedSlots.Select(member => member.formationSlot).Distinct().Count() != fixedSlots.Count)
            throw new InvalidDataException($"{scenario.id}: party contains duplicate formation slots.");

        ValidateLevel(scenario.partyLevel, 0, $"{scenario.id}: partyLevel");
        ValidateRank(scenario.partyRank, 0, $"{scenario.id}: partyRank");
        ValidateRank(scenario.startingGuildRank, 0, $"{scenario.id}: startingGuildRank");
        if (scenario.partyCapacityUpgrades < 0
            || scenario.partyCapacityUpgrades > GuildManager.PartyCapacityUpgradeMaximum)
            throw new InvalidDataException(
                $"{scenario.id}: partyCapacityUpgrades must be 0 to {GuildManager.PartyCapacityUpgradeMaximum}.");
        foreach (var member in members)
        {
            if (!adventurers.TryGetValue(member.id, out var master))
                throw new InvalidDataException($"{scenario.id}: unknown adventurer '{member.id}'.");
            ValidateLevel(member.level, master.defaultLevel, $"{scenario.id}: {member.id}.level");
            ValidateRank(member.rank, master.defaultRank, $"{scenario.id}: {member.id}.rank");
            int effectiveLevel = member.level > 0 ? member.level : scenario.partyLevel;
            int effectiveRank = member.rank > 0 ? member.rank : scenario.partyRank;
            ValidateLevel(effectiveLevel, master.defaultLevel, $"{scenario.id}: {member.id} effective level");
            ValidateRank(effectiveRank, master.defaultRank, $"{scenario.id}: {member.id} effective rank");
            foreach (var (slotName, equipmentId) in member.equipment)
            {
                if (!Enum.TryParse<EquipSlot>(slotName, true, out var slot))
                    throw new InvalidDataException($"{scenario.id}: {member.id} has unknown equipment slot '{slotName}'.");
                if (string.IsNullOrWhiteSpace(equipmentId)) continue;
                if (!db.equipment.TryGetValue(equipmentId, out var item))
                    throw new InvalidDataException($"{scenario.id}: {member.id} has unknown equipment '{equipmentId}'.");
                if (!item.CanEquipTo(slot))
                    throw new InvalidDataException($"{scenario.id}: {equipmentId} cannot be equipped in {slot}.");
            }
        }

        string type = scenario.type.ToLowerInvariant();
        if (type is "quest" or "campaign"
            && members.Count > GuildManager.BasePartyCapacity + scenario.partyCapacityUpgrades)
            throw new InvalidDataException(
                $"{scenario.id}: {members.Count} quest members need partyCapacityUpgrades="
                + $"{members.Count - GuildManager.BasePartyCapacity}.");
        if (type == "battle" && !db.enemyUnits.ContainsKey(scenario.enemyUnitId))
            throw new InvalidDataException($"{scenario.id}: unknown enemy unit '{scenario.enemyUnitId}'.");
        if (type == "quest" && !quests.ContainsKey(scenario.questId))
            throw new InvalidDataException($"{scenario.id}: unknown quest '{scenario.questId}'.");
        if (type == "campaign")
        {
            if (scenario.questIds.Count == 0)
                throw new InvalidDataException($"{scenario.id}: campaign questIds must not be empty.");
            foreach (var questId in scenario.questIds)
                if (!quests.ContainsKey(questId))
                    throw new InvalidDataException($"{scenario.id}: unknown campaign quest '{questId}'.");
        }
        if (type is not ("battle" or "quest" or "campaign"))
            throw new InvalidDataException($"{scenario.id}: type must be battle, quest, or campaign.");
        if (scenario.maxTurns <= 0)
            throw new InvalidDataException($"{scenario.id}: maxTurns must be greater than 0.");
        _ = ParsePolicy(scenario.policy, scenario.id);
    }

    static void ValidateLevel(int value, int minimum, string field)
    {
        if (value < 0 || value > 100 || (value > 0 && value < minimum))
            throw new InvalidDataException($"{field} must be 0 or {Math.Max(1, minimum)}..100.");
    }

    static void ValidateRank(int value, int minimum, string field)
    {
        if (value < 0 || value > Rank.Max || (value > 0 && value < minimum))
            throw new InvalidDataException($"{field} must be 0 or {Math.Max(Rank.Min, minimum)}..{Rank.Max}.");
    }

    BalanceScenarioResult RunBattle(BalanceScenario scenario, int runs, int seed)
    {
        int wins = 0, retreats = 0, failures = 0;
        long rounds = 0;
        double hpPercent = 0, endingLevel = 0, endingRank = 0;
        var enemyTemplate = db.enemyUnits[scenario.enemyUnitId];
        var policy = ParsePolicy(scenario.policy, scenario.id);

        for (int runIndex = 0; runIndex < runs; runIndex++)
        {
            using var random = GameRandom.UseSeed(unchecked(seed + runIndex));
            var party = CreateParty(scenario);
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
            endingLevel += PartyAverage(party, x => x.level);
            endingRank += PartyAverage(party, x => x.rank);
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
            meanEndingLevel = Mean(endingLevel, runs),
            meanEndingRank = Mean(endingRank, runs),
        };
    }

    BalanceScenarioResult RunQuest(BalanceScenario scenario, int runs, int seed)
    {
        int clears = 0, retreats = 0, failures = 0, bankruptcies = 0;
        long turns = 0, gatherExtensions = 0, chests = 0;
        double hpPercent = 0, goldDelta = 0, endingLevel = 0, endingRank = 0;
        var quest = quests[scenario.questId];

        for (int runIndex = 0; runIndex < runs; runIndex++)
        {
            using var random = GameRandom.UseSeed(unchecked(seed + runIndex));
            int guildRank = scenario.startingGuildRank > 0 ? scenario.startingGuildRank : quest.rank;
            var guild = new GuildManager(scenario.startingGold, guildRank);
            var party = CreateParty(scenario);
            RestoreConfiguredPartyCapacity(guild, scenario.partyCapacityUpgrades);
            foreach (var member in party.Where(x => x != null)) guild.AddAdventurer(member!);
            var manager = new QuestManager(guild);
            int currentTurn = 1;
            var play = PlayQuest(scenario, quest, manager, guild, party, ref currentTurn);

            if (play.Run.IsCleared) clears++;
            if (play.Run.retreated) retreats++;
            if (play.IsFailure) failures++;
            if (play.Bankrupt) bankruptcies++;
            turns += play.Turns;
            gatherExtensions += play.Run.gatherExtensions;
            chests += play.Chests;
            hpPercent += play.HpPercent;
            goldDelta += guild.Gold - scenario.startingGold;
            endingLevel += PartyAverage(party, x => x.level);
            endingRank += PartyAverage(party, x => x.rank);
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
            meanEndingLevel = Mean(endingLevel, runs),
            meanEndingRank = Mean(endingRank, runs),
        };
    }

    BalanceScenarioResult RunCampaign(BalanceScenario scenario, int runs, int seed)
    {
        int clears = 0, retreats = 0, failures = 0, bankruptcies = 0;
        long turns = 0, completedSteps = 0;
        double hpPercent = 0, goldDelta = 0, endingLevel = 0, endingRank = 0;
        var stepStats = scenario.questIds.Select(id => new CampaignStepAccumulator(quests[id])).ToList();

        for (int runIndex = 0; runIndex < runs; runIndex++)
        {
            using var random = GameRandom.UseSeed(unchecked(seed + runIndex));
            int guildRank = scenario.startingGuildRank > 0
                ? scenario.startingGuildRank
                : quests[scenario.questIds[0]].rank;
            var guild = new GuildManager(scenario.startingGold, guildRank);
            var party = CreateParty(scenario);
            RestoreConfiguredPartyCapacity(guild, scenario.partyCapacityUpgrades);
            foreach (var member in party.Where(x => x != null)) guild.AddAdventurer(member!);
            var manager = new QuestManager(guild);
            int currentTurn = 1;
            int clearedCount = 0;
            bool anyRetreat = false, anyFailure = false, bankrupt = false;
            double terminalHp = 100;

            for (int stepIndex = 0; stepIndex < scenario.questIds.Count; stepIndex++)
            {
                // A fixed campaign formation cannot depart while it still contains a dead member.
                // The next quest is therefore not reached; callers can model replacement with a new campaign.
                if (party.Any(x => x != null && !x.isAlive))
                {
                    anyFailure = true;
                    break;
                }
                var stats = stepStats[stepIndex];
                double startingLevel = PartyAverage(party, x => x.level);
                double startingRank = PartyAverage(party, x => x.rank);
                stats.Reached++;
                stats.StartingLevel += startingLevel;
                stats.StartingRank += startingRank;

                var play = PlayQuest(scenario, stats.Quest, manager, guild, party, ref currentTurn);

                turns += play.Turns;
                terminalHp = play.HpPercent;
                bankrupt |= play.Bankrupt;
                if (play.Run.IsCleared)
                {
                    stats.Clears++;
                    clearedCount++;
                    if (scenario.autoRankUp)
                        AutoRankUp(party);
                }
                else if (play.Run.retreated)
                {
                    stats.Retreats++;
                    anyRetreat = true;
                }
                else
                {
                    stats.Failures++;
                    anyFailure = true;
                }

                stats.EndingLevel += PartyAverage(party, x => x.level);
                stats.EndingRank += PartyAverage(party, x => x.rank);
                if (!play.Run.IsCleared && !scenario.continueOnFailure) break;
                if (play.IsFailure && !play.Run.rewarded) break;
                if (!party.Any(x => x != null && x.isAlive)) break;
            }

            bool campaignCleared = clearedCount == scenario.questIds.Count;
            if (campaignCleared) clears++;
            else if (anyFailure) failures++;
            else if (anyRetreat) retreats++;
            else failures++;
            if (bankrupt) bankruptcies++;
            completedSteps += clearedCount;
            hpPercent += terminalHp;
            goldDelta += guild.Gold - scenario.startingGold;
            endingLevel += PartyAverage(party, x => x.level);
            endingRank += PartyAverage(party, x => x.rank);
        }

        return new BalanceScenarioResult
        {
            id = scenario.id,
            name = DisplayName(scenario),
            type = "campaign",
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
            meanEndingLevel = Mean(endingLevel, runs),
            meanEndingRank = Mean(endingRank, runs),
            meanCompletedSteps = Mean(completedSteps, runs),
            campaignSteps = stepStats.Select(x => x.Result(runs)).ToList(),
        };
    }

    QuestPlayResult PlayQuest(
        BalanceScenario scenario,
        QuestMasterData quest,
        QuestManager manager,
        GuildManager guild,
        AdventurerData?[] party,
        ref int currentTurn)
    {
        var policy = ParsePolicy(scenario.policy, scenario.id);
        if (!manager.TryStartQuest(quest, party, currentTurn, out var error, policy: policy))
            throw new InvalidOperationException($"{scenario.id}: quest '{quest.id}' could not start: {error}");
        var questRun = manager.activeQuests.Single();
        int elapsed = 0;
        bool bankrupt = false, captured = false;
        double terminalHp = 0;
        int terminalChests = 0;

        for (int i = 0; i < scenario.maxTurns; i++)
        {
            currentTurn++;
            elapsed++;
            manager.AdvanceAll(currentTurn);
            guild.PayUpkeepForAll(currentTurn);
            bankrupt |= guild.Gold <= 0;

            if (questRun.HasPendingChoice)
                ResolveAutomaticChoice(scenario.id, manager, questRun);
            if (questRun.HasGatherDecision)
            {
                bool continueSearch = questRun.gatherExtensions < scenario.maxGatherExtensions;
                if (!manager.ResolveGatherDecision(questRun, continueSearch, out var gatherError))
                    throw new InvalidOperationException($"{scenario.id}: gather decision could not resolve: {gatherError}");
            }
            if (questRun.CanComplete || questRun.failed)
            {
                terminalHp = RemainingHpPercent(party);
                terminalChests = questRun.chests.Count;
                captured = true;
                manager.FinalizeQuest(questRun);
                break;
            }
        }

        if (!captured)
        {
            terminalHp = RemainingHpPercent(party);
            terminalChests = questRun.chests.Count;
        }
        if (!questRun.rewarded && (questRun.CanComplete || questRun.failed))
            manager.FinalizeQuest(questRun);
        bool failure = questRun.failed
            || (!questRun.rewarded && !questRun.IsCleared && !questRun.retreated);
        return new QuestPlayResult(questRun, elapsed, bankrupt, terminalHp, terminalChests, failure);
    }

    static void ResolveAutomaticChoice(string scenarioId, QuestManager manager, QuestRun questRun)
    {
        var pending = questRun.pendingChoice
            ?? throw new InvalidOperationException($"{scenarioId}: choice is no longer pending.");
        string lastError = "no usable option";
        for (int optionIndex = 0; optionIndex < pending.Event.options.Count; optionIndex++)
        {
            var option = pending.Event.options[optionIndex];
            if (!option.targetsOneMember)
            {
                if (manager.ResolveChoice(questRun, optionIndex, null, out lastError)) return;
                continue;
            }

            foreach (var target in questRun.EnumerateMembers().Where(x => x.isAlive))
                if (manager.ResolveChoice(questRun, optionIndex, target, out lastError)) return;
        }
        throw new InvalidOperationException($"{scenarioId}: choice could not resolve: {lastError}");
    }

    AdventurerData?[] CreateParty(BalanceScenario scenario)
    {
        var members = PartyMembers(scenario).Select(spec =>
        {
            var member = new AdventurerData(adventurers[spec.id]);
            int targetLevel = spec.level > 0 ? spec.level : scenario.partyLevel;
            while (targetLevel > 0 && member.level < targetLevel)
                member.AddExperience(member.RequiredExpForNextLevel, out _);

            int targetRank = spec.rank > 0 ? spec.rank : scenario.partyRank;
            while (targetRank > 0 && member.rank < targetRank)
            {
                var requirement = member.NextRankRequirement!.Value;
                member.higherRankClears = requirement.higherRankClears;
                member.suitableRankClearsTotal = Math.Max(
                    member.suitableRankClearsTotal, requirement.suitableTotalClears);
                if (!member.TryRankUp(out _))
                    throw new InvalidOperationException($"Could not promote {spec.id} to rank {targetRank}.");
            }

            foreach (var (slotName, equipmentId) in spec.equipment)
            {
                var slot = Enum.Parse<EquipSlot>(slotName, true);
                member.SetEquipped(slot,
                    string.IsNullOrWhiteSpace(equipmentId) ? null : db.equipment[equipmentId]);
            }
            if (member.GetEquipped(EquipSlot.RightHand) is { isTwoHanded: true }
                && member.GetEquipped(EquipSlot.LeftHand) != null)
                throw new InvalidDataException($"{scenario.id}: {spec.id} cannot combine a two-handed weapon with left-hand equipment.");
            return (spec, member);
        }).ToArray();

        var party = new AdventurerData?[GuildManager.FormationSlotCount];
        foreach (var (spec, member) in members.Where(entry => entry.spec.formationSlot > 0))
            party[spec.formationSlot - 1] = member;
        foreach (var (_, member) in members.Where(entry => entry.spec.formationSlot == 0))
        {
            int openSlot = Array.FindIndex(party, existing => existing == null);
            if (openSlot < 0)
                throw new InvalidDataException($"{scenario.id}: no open formation slot remains.");
            party[openSlot] = member;
        }
        return party;
    }

    static void RestoreConfiguredPartyCapacity(GuildManager guild, int upgradeCount)
    {
        // 比較条件を明示できるよう、ゲーム内の建設費・ランク条件とは分けて
        // シナリオに指定した強化段階だけを試験用施設効果として再現する。
        if (upgradeCount == 0) return;
        guild.RestoreFacilities(new[]
        {
            new FacilityMasterData
            {
                id = "balance_party_capacity",
                displayName = "Balance Lab 編成枠",
                partySlotBonus = upgradeCount,
            },
        });
    }

    static IEnumerable<BalancePartyMember> PartyMembers(BalanceScenario scenario) =>
        scenario.party.Count > 0
            ? scenario.party
            : scenario.partyIds.Select(id => new BalancePartyMember { id = id });

    static void AutoRankUp(IEnumerable<AdventurerData?> party)
    {
        foreach (var member in party.Where(x => x != null).Select(x => x!))
            while (member.CanRankUp)
                member.TryRankUp(out _);
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

    static double PartyAverage(IEnumerable<AdventurerData?> party, Func<AdventurerData, int> selector)
    {
        var members = party.Where(x => x != null).Select(x => x!).ToList();
        return members.Count == 0 ? 0 : members.Average(selector);
    }

    static double Rate(long value, int runs) => runs <= 0 ? 0 : Math.Round(value * 100d / runs, 4);
    static double Mean(long value, int runs) => runs <= 0 ? 0 : Math.Round(value / (double)runs, 4);
    static double Mean(double value, int runs) => runs <= 0 ? 0 : Math.Round(value / runs, 4);

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
        meanEndingLevel = Round(current.meanEndingLevel - previous.meanEndingLevel),
        meanEndingRank = Round(current.meanEndingRank - previous.meanEndingRank),
        meanCompletedSteps = Round(current.meanCompletedSteps - previous.meanCompletedSteps),
    };

    static double Round(double value) => Math.Round(value, 4);

    sealed record QuestPlayResult(
        QuestRun Run,
        int Turns,
        bool Bankrupt,
        double HpPercent,
        int Chests,
        bool IsFailure);

    sealed class CampaignStepAccumulator(QuestMasterData quest)
    {
        public QuestMasterData Quest { get; } = quest;
        public int Reached;
        public int Clears;
        public int Retreats;
        public int Failures;
        public double StartingLevel;
        public double EndingLevel;
        public double StartingRank;
        public double EndingRank;

        public BalanceCampaignStepResult Result(int totalRuns) => new()
        {
            questId = Quest.id,
            name = Quest.questName,
            reachedRuns = Reached,
            reachRatePercent = Rate(Reached, totalRuns),
            clearRatePercent = Rate(Clears, Reached),
            retreatRatePercent = Rate(Retreats, Reached),
            failureRatePercent = Rate(Failures, Reached),
            meanStartingLevel = Mean(StartingLevel, Reached),
            meanEndingLevel = Mean(EndingLevel, Reached),
            meanStartingRank = Mean(StartingRank, Reached),
            meanEndingRank = Mean(EndingRank, Reached),
        };
    }
}
