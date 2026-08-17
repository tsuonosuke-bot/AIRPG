using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.Systems.Quest;

/// <summary>
/// クエスト（＝ダンジョン構成）から戦闘の激しさを見積もり、受注前の判断材料を出す。
/// マスターデータのみから算出し、実プレイの乱数には依存しない目安値。
/// </summary>
public static class DungeonDifficulty
{
    public sealed record PartyAssessment(
        string Label,
        int RecommendedSize,
        int MemberCount,
        int AverageRank,
        int TargetThreat,
        int MaximumEncounterThreat,
        float OutrankedEncounterChancePercent,
        int InjuredCount,
        int Score)
    {
        public string TargetThreatLabel => Rank.Label(TargetThreat);
        public string MaximumEncounterThreatLabel => Rank.Label(MaximumEncounterThreat);
        public string AverageRankLabel => Rank.Label(AverageRank);
    }

    public class Rating
    {
        public double score;         // 難易度の総合スコア（下限なし、段階数に縛られない目安値）
        public string label = "";
        public float combatChance;   // 1エリアあたりの敵遭遇率 0..1
        public float trapChance;     // 1エリアあたりの罠率 0..1
        public int enemyThreatMin;
        public int enemyThreatTypical;
        public int enemyThreatMax;
        public float enemyThreatMaxEncounterChancePercent;
        public float enemyFormationTypicalSize;
        public int enemyFormationMaxSize;
        public bool hasBoss;
        public int bossThreat;
        public int bossMemberCount;
        public float expectedFights; // 見込み戦闘回数（ボス除く）

        /// <summary>出現する敵の脅威度の帯（F〜S）。</summary>
        public string EnemyThreatRange =>
            enemyThreatMax <= 0 ? "―"
            : enemyThreatMin == enemyThreatMax ? Rank.Label(enemyThreatMin)
            : $"{Rank.Label(enemyThreatMin)}〜{Rank.Label(enemyThreatMax)}";

        public string EnemyThreatSummary => enemyThreatMax <= 0
            ? "―"
            : enemyThreatTypical == enemyThreatMax
                ? Rank.Label(enemyThreatTypical)
                : $"中心{Rank.Label(enemyThreatTypical)} / 最大{Rank.Label(enemyThreatMax)}"
                    + $"（遭遇見込み{enemyThreatMaxEncounterChancePercent:0.#}%）";

        public string EnemyFormationSummary => enemyFormationMaxSize <= 0
            ? "―"
            : $"平均{enemyFormationTypicalSize:0.#}体 / 最大{enemyFormationMaxSize}体";

        public string BossThreatLabel => Rank.Label(bossThreat);
    }

    public static Rating Evaluate(QuestMasterData q)
    {
        var r = new Rating();
        var d = q.Dungeon;

        // --- イベント構成から戦闘率・罠率 ---
        if (d != null && d.eventTable.Count > 0)
        {
            int total = 0;
            foreach (var w in d.eventTable.Values) if (w > 0) total += w;
            if (total > 0)
            {
                d.eventTable.TryGetValue(DungeonEventType.EnemyEncounter, out int enc);
                d.eventTable.TryGetValue(DungeonEventType.Trap, out int trap);
                r.combatChance = (float)Math.Max(0, enc) / total;
                r.trapChance = (float)Math.Max(0, trap) / total;
            }
        }
        int normalPhaseCount = Enumerable.Range(1, q.totalPhases)
            .Count(phase => !IsBossPhase(q, phase));
        r.expectedFights = r.combatChance * normalPhaseCount;

        // --- 敵の脅威度帯 ---
        // 倍率でのスケーリングは廃止したので、出現しうる敵ユニットの脅威度をそのまま見る。
        // 最大値だけでは、低確率の格上1体がクエスト全体の評価を支配してしまう。
        // 全エリアの重み付き平均を「中心」、少なくとも一度最大脅威に遭う確率を「最悪候補」として分ける。
        if (d != null && d.encounterTable.Count > 0)
        {
            var normalPhases = Enumerable.Range(1, q.totalPhases)
                .Where(phase => !IsBossPhase(q, phase))
                .ToList();
            var entries = d.encounterTable
                .Where(e => e.Unit != null && e.weight > 0
                    && normalPhases.Any(e.IsEligible))
                .ToList();
            var units = entries.Select(e => e.Unit!).ToList();
            if (units.Count > 0)
            {
                r.enemyThreatMin = Rank.Clamp(units.Min(u => u.Threat));
                r.enemyThreatMax = Rank.Clamp(units.Max(u => u.Threat));

                double threatTotal = 0;
                double formationSizeTotal = 0;
                int threatPhases = 0;
                double noMaximumEncounter = 1;
                foreach (int phase in normalPhases)
                {
                    var eligible = entries.Where(e => e.IsEligible(phase)).ToList();
                    int totalWeight = eligible.Sum(e => e.weight);
                    if (totalWeight <= 0) continue;

                    threatTotal += eligible.Sum(e => e.weight * e.Unit!.Threat) / (double)totalWeight;
                    formationSizeTotal += eligible.Sum(e => e.weight
                        * e.Unit!.Formation.Count(member => member != null)) / (double)totalWeight;
                    threatPhases++;

                    int maximumWeight = eligible
                        .Where(e => e.Unit!.Threat == r.enemyThreatMax)
                        .Sum(e => e.weight);
                    double maximumChanceThisPhase = r.combatChance * maximumWeight / totalWeight;
                    noMaximumEncounter *= 1 - maximumChanceThisPhase;
                }

                r.enemyThreatTypical = threatPhases == 0
                    ? r.enemyThreatMin
                    : Rank.Clamp((int)Math.Round(threatTotal / threatPhases));
                r.enemyFormationTypicalSize = threatPhases == 0
                    ? 0
                    : (float)(formationSizeTotal / threatPhases);
                r.enemyFormationMaxSize = units.Max(unit =>
                    unit.Formation.Count(member => member != null));
                r.enemyThreatMaxEncounterChancePercent =
                    (float)((1 - noMaximumEncounter) * 100);
            }
        }

        // --- ボス ---
        r.hasBoss = q.BossEnemy != null;
        if (r.hasBoss)
        {
            r.bossThreat = Rank.Clamp(q.BossEnemy!.Threat);
            r.bossMemberCount = q.BossEnemy!.Formation.Count(member => member != null);
        }

        // --- 総合スコア ---
        // クエストランクを土台に、戦闘頻度・罠・敵の脅威度・ボスで上積みする。
        // 段階数に縛られる星評価はやめ、連続値のスコアとラベルで表す。
        // 脅威度はF〜Sの7段階しかなく、旧来の敵レベル（青天井）より粗いので係数を上げてある。
        r.score =
            q.rank * 4.0
          + r.combatChance * 100 * 0.35
          + r.trapChance * 100 * 0.20
          + r.enemyThreatTypical * 3.0
          + (r.hasBoss ? 8 : 0);

        r.label = r.score switch
        {
            < 18 => "楽勝",
            < 26 => "軽め",
            < 34 => "標準",
            < 42 => "危険",
            _ => "過酷",
        };
        return r;
    }

    /// <summary>
    /// クエスト固有の難易度とは別に、現在の編成が十分かを人数・認定ランク・負傷から評価する。
    /// 数値を隠した勝率予測にはせず、何を根拠にした目安かを画面で説明できる値を返す。
    /// </summary>
    public static PartyAssessment EvaluateParty(
        QuestMasterData quest,
        IReadOnlyCollection<AdventurerData> members)
    {
        var difficulty = Evaluate(quest);
        int recommendedSize = difficulty.score switch
        {
            < 26 => 2,
            < 34 => 3,
            < 42 => 4,
            _ => 5,
        };
        recommendedSize = Math.Max(recommendedSize, Math.Min(5,
            (int)Math.Ceiling(Math.Max(
                difficulty.enemyFormationTypicalSize * 0.75f,
                difficulty.bossMemberCount * 0.75f))));

        int memberCount = members.Count;
        int averageRank = memberCount == 0
            ? Rank.Min
            : Rank.Clamp((int)Math.Round(members.Average(member => (double)member.rank)));
        int targetThreat = Math.Max(
            quest.rank,
            Math.Max(difficulty.enemyThreatTypical, difficulty.hasBoss ? difficulty.bossThreat : Rank.Min));
        targetThreat = Rank.Clamp(targetThreat);
        float outrankedEncounterChance = EstimateEncounterChance(
            quest,
            threat => threat > averageRank);
        int injuredCount = members.Count(member => member.IsInjured);

        int sizeGap = memberCount - recommendedSize;
        int score = Math.Clamp(sizeGap, -2, 2)
            + Math.Clamp(averageRank - targetThreat, -2, 2)
            - Math.Min(2, injuredCount);
        string label = score switch
        {
            <= -3 => "非常に危険",
            <= -1 => "不利",
            <= 1 => "適正",
            _ => "有利",
        };

        return new PartyAssessment(
            label,
            recommendedSize,
            memberCount,
            averageRank,
            targetThreat,
            difficulty.enemyThreatMax,
            outrankedEncounterChance,
            injuredCount,
            score);
    }

    static float EstimateEncounterChance(QuestMasterData quest, Func<int, bool> matches)
    {
        var dungeon = quest.Dungeon;
        if (dungeon == null || dungeon.encounterTable.Count == 0) return 0;

        var entries = dungeon.encounterTable
            .Where(entry => entry.Unit != null && entry.weight > 0)
            .ToList();
        float combatChance = Evaluate(quest).combatChance;
        double noMatchingEncounter = 1;
        for (int phase = 1; phase <= quest.totalPhases; phase++)
        {
            if (IsBossPhase(quest, phase)) continue;
            var eligible = entries.Where(entry => entry.IsEligible(phase)).ToList();
            int totalWeight = eligible.Sum(entry => entry.weight);
            if (totalWeight <= 0) continue;

            int matchingWeight = eligible
                .Where(entry => matches(entry.Unit!.Threat))
                .Sum(entry => entry.weight);
            double chanceThisPhase = combatChance * matchingWeight / totalWeight;
            noMatchingEncounter *= 1 - chanceThisPhase;
        }
        return (float)((1 - noMatchingEncounter) * 100);
    }

    static bool IsBossPhase(QuestMasterData quest, int phase) =>
        quest.BossEnemy != null && quest.bossPhase == phase;
}
